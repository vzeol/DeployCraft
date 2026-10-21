using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using DeployCraft.Server.Data;
using DeployCraft.Server.Remote;
using Microsoft.Data.Sqlite;

namespace DeployCraft.Server.Library;

/// <summary>
/// Package library: searches the public winget catalog index, reads the sheet of the chosen software,
/// downloads its installer (hash checked) into the library folder and creates or updates the matching
/// DeployCraft package. Index and downloads are handled in the background; the console follows the state.
/// </summary>
public class PackageLibrary(IPackageRepository packageRepository, ISettingsRepository settingsRepository, DeployCredentials credentials, string dataDir, ILogger<PackageLibrary> log)
{
    private const string SettingKey = "library";
    private const string CatalogRaw = "https://raw.githubusercontent.com/microsoft/winget-pkgs/master/";
    /// <summary>At most this many results are shown for a search.</summary>
    private const int SearchLimit = 1000;
    /// <summary>The index is re-read on its own when it is more than a day old (checked every hour, and when the console shows it).</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);

    private static readonly HttpClient Http = CreateClient();

    private static string StageDownload => Msg.Get("PackageLibrary_StageDownload");
    private static string StageCheck => Msg.Get("PackageLibrary_StageCheck");
    private static string StageCopy => Msg.Get("PackageLibrary_StageCopy");
    private static string StagePackage => Msg.Get("PackageLibrary_StagePackage");

    private readonly object _lock = new();
    private readonly Dictionary<string, (string Stage, int Percent)> _progress = [];
    private readonly CatalogIndex _index = new(Path.Combine(dataDir, "library-index.db"));
    private bool _refreshing;

    public string DefaultFolder => Path.Combine(dataDir, "Bibliotheque");

    // ---------- State ----------

    /// <summary>
    /// Without a search: the whole catalog, with already downloaded software first, then common software.
    /// With a search: the matching software. The console loads this list once, then only follows
    /// the changes with <see cref="Status"/>.
    /// </summary>
    public LibraryInfo Info(string? search) => Read(state =>
    {
        if (string.IsNullOrWhiteSpace(search)) return Ordered(_index.All(), state);
        return _index.Search(search, SearchLimit);
    });

    /// <summary>
    /// Only the software that has a state of its own: download in progress, error, or package created.
    /// Small: the console asks for it every two seconds to update these rows.
    /// </summary>
    public LibraryInfo Status() => Read(state =>
    {
        IEnumerable<string> ids;
        lock (_lock) ids = [.. _progress.Keys, .. state.Links.Keys, .. state.Errors.Keys];
        return [.. _index.Get(ids).Values];
    });

    private LibraryInfo Read(Func<LibraryState, List<IndexEntry>> list)
    {
        var state = Load();
        if (!_index.Exists || state.LastRefreshUtc == null || DateTime.UtcNow - state.LastRefreshUtc > MaxAge) StartRefresh();

        var entries = new List<IndexEntry>();
        var count = 0;
        if (_index.Exists)
        {
            try
            {
                count = _index.Count();
                entries = list(state);
            }
            catch (SqliteException ex)
            {
                // Index replaced during the read: the console will ask again in a moment.
                log.LogDebug(ex, "Lecture de l'index de la bibliothèque");
            }
        }

        lock (_lock)
        {
            return new LibraryInfo
            {
                Folder = Folder(state),
                DefaultFolder = string.IsNullOrWhiteSpace(state.Folder),
                LastRefreshUtc = state.LastRefreshUtc,
                Refreshing = _refreshing,
                RefreshError = state.RefreshError,
                AutoUpdate = state.AutoUpdate,
                AutoUpdateDays = state.AutoUpdateDays,
                CatalogCount = count,
                Items = entries.Select(e => Item(e, state)).ToList(),
            };
        }
    }

    /// <summary>Already downloaded software first, then common software, then the rest in alphabetical order.</summary>
    private List<IndexEntry> Ordered(List<IndexEntry> all, LibraryState state)
    {
        var linked = state.Links.Where(l => packageRepository.Get(l.Value.PackageId) != null).Select(l => l.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var suggested = LibraryCatalog.Suggested.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. all.OrderBy(e => linked.Contains(e.Id) ? 0 : suggested.Contains(e.Id) ? 1 : 2)];
    }

    private LibraryItem Item(IndexEntry e, LibraryState state)
    {
        // The sheet that was read is only valid for the version it describes.
        var entry = state.Entries.GetValueOrDefault(e.Id) is { } known && known.Version == e.Version ? known : null;
        var link = state.Links.GetValueOrDefault(e.Id);
        // A package deleted in DeployCraft is no longer linked.
        if (link != null && packageRepository.Get(link.PackageId) == null) link = null;
        return new LibraryItem
        {
            Id = e.Id,
            Name = e.Name,
            LatestVersion = e.Version,
            Installer = entry?.Installer is { } i ? $"{i.Type} {i.Architecture}" : null,
            Problem = entry?.Problem,
            PackageId = link?.PackageId,
            PackageVersion = link?.Version,
            Stage = _progress.TryGetValue(e.Id, out var p) ? p.Stage : null,
            Progress = _progress.ContainsKey(e.Id) ? p.Percent : null,
            Error = state.Errors.GetValueOrDefault(e.Id),
            UpdateAvailable = link != null && CatalogManifest.CompareVersions(e.Version, link.Version) > 0,
        };
    }

    /// <summary>Installer folder: a share is recommended (readable by computers in pull mode).</summary>
    public string? SetFolder(string? folder)
    {
        folder = folder?.Trim().TrimEnd('\\');
        if (!string.IsNullOrEmpty(folder) && !Path.IsPathFullyQualified(folder))
            return Msg.Get("PackageLibrary_FullPath");
        lock (_lock)
        {
            var state = Load();
            state.Folder = string.IsNullOrEmpty(folder) ? null : folder;
            Save(state);
        }
        return null;
    }

    public const int MinAutoUpdateDays = 1;
    public const int MaxAutoUpdateDays = 90;

    /// <summary>Update of the library packages: manual, as soon as the new version appears, or after a few days.</summary>
    public string? SetAutoUpdate(LibraryAutoUpdate mode, int days)
    {
        if (!Enum.IsDefined(mode)) return Msg.Get("PackageLibrary_UnknownUpdateMode");
        if (days is < MinAutoUpdateDays or > MaxAutoUpdateDays)
            return Msg.Format("PackageLibrary_DelayRange", MinAutoUpdateDays, MaxAutoUpdateDays);
        lock (_lock)
        {
            var state = Load();
            state.AutoUpdate = mode;
            state.AutoUpdateDays = days;
            Save(state);
        }
        return null;
    }

    // ---------- Reading the catalog ----------

    /// <summary>Re-reads the index in the background (no effect if a read is already in progress).</summary>
    public void StartRefresh()
    {
        if (TryStartRefresh()) _ = Task.Run(RefreshAsync);
    }

    /// <summary>
    /// Service tick (every hour): re-reads the index if it is more than a day old, even if nobody opens the console.
    /// Automatic updates follow each successful read.
    /// </summary>
    public async Task RefreshIfStaleAsync()
    {
        var state = Load();
        if (_index.Exists && state.LastRefreshUtc is { } last && DateTime.UtcNow - last <= MaxAge) return;
        if (TryStartRefresh()) await RefreshAsync();
    }

    private bool TryStartRefresh()
    {
        lock (_lock)
        {
            if (_refreshing) return false;
            _refreshing = true;
            return true;
        }
    }

    /// <summary>
    /// Downloads the index, then re-reads the sheets of the software shown without a search whose version changed
    /// (to show their installer right away, or why they are not available).
    /// </summary>
    private async Task RefreshAsync()
    {
        string? error = null;
        try
        {
            await _index.DownloadAsync(Http);
            var state = Load();
            var known = _index.Get(state.Links.Keys.Concat(LibraryCatalog.Suggested));
            foreach (var e in known.Values.Where(e => state.Entries.GetValueOrDefault(e.Id)?.Version != e.Version))
            {
                var entry = await ReadEntryAsync(e);
                lock (_lock)
                {
                    var current = Load();
                    current.Entries[e.Id] = entry;
                    Save(current);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            error = Msg.Format("PackageLibrary_CatalogUnreachable", ex.Message);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Lecture du catalogue de la bibliothèque");
            error = Msg.Format("PackageLibrary_CatalogReadFailed", ex.Message);
        }
        lock (_lock)
        {
            var state = Load();
            state.RefreshError = error;
            if (error == null) state.LastRefreshUtc = DateTime.UtcNow;
            Save(state);
            _refreshing = false;
        }
        if (error == null) await StartAutoUpdatesAsync(DateTime.UtcNow);
    }

    /// <summary>Starts the update of the packages whose new version is due, according to the setting.</summary>
    private async Task StartAutoUpdatesAsync(DateTime nowUtc)
    {
        var state = Load();
        if (state.AutoUpdate == LibraryAutoUpdate.Manual) return;
        var latest = _index.Get(state.Links.Keys);
        foreach (var (id, link) in state.Links)
        {
            if (packageRepository.Get(link.PackageId) == null || latest.GetValueOrDefault(id) is not { } e) continue;
            var seen = state.Entries.GetValueOrDefault(id) is { } entry && entry.Version == e.Version ? entry.SeenUtc : null;
            if (!IsUpdateDue(state.AutoUpdate, state.AutoUpdateDays, link.Version, e.Version, seen, nowUtc)) continue;
            log.LogInformation("Mise à jour automatique de {App} : {Old} → {New}", e.Name, link.Version, e.Version);
            if (await StartDownloadAsync(id) is { } error)
                lock (_lock)
                {
                    var current = Load();
                    current.Errors[id] = error;
                    Save(current);
                }
        }
    }

    /// <summary>
    /// A new version is to be installed on its own: right away, or once the delay has passed since the
    /// catalog announced it (unknown date: version read before this setting, so already old).
    /// </summary>
    internal static bool IsUpdateDue(LibraryAutoUpdate mode, int days, string current, string latest, DateTime? seenUtc, DateTime nowUtc)
    {
        if (mode == LibraryAutoUpdate.Manual || CatalogManifest.CompareVersions(latest, current) <= 0) return false;
        return mode == LibraryAutoUpdate.Immediate || seenUtc == null || nowUtc - seenUtc.Value >= TimeSpan.FromDays(days);
    }

    /// <summary>Install sheet of the latest version: chosen installer, or the reason why there is none.</summary>
    private static async Task<LibraryEntry> ReadEntryAsync(IndexEntry e)
    {
        var url = $"{CatalogRaw}{Escape(LibraryCatalog.ManifestFolder(e.Id))}/{Escape(e.Version)}/{Escape(e.Id)}.installer.yaml";
        using var response = await Http.GetAsync(url);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new LibraryEntry { Version = e.Version, Problem = Msg.Get("PackageLibrary_SheetNotFound"), SeenUtc = DateTime.UtcNow };
        response.EnsureSuccessStatusCode();
        var installer = CatalogManifest.Choose(await response.Content.ReadAsStringAsync(), out var problem);
        return new LibraryEntry { Version = e.Version, Installer = installer, Problem = problem, SeenUtc = DateTime.UtcNow };
    }

    private static string Escape(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    // ---------- Download ----------

    /// <summary>
    /// Reads the software sheet then starts the download in the background; returns an error if it cannot
    /// start (no silent installer for the whole computer...).
    /// </summary>
    public async Task<string?> StartDownloadAsync(string id)
    {
        if (!_index.Exists) return Msg.Get("PackageLibrary_CatalogNotRead");
        if (_index.Get([id]).GetValueOrDefault(id) is not { } e) return Msg.Get("PackageLibrary_SoftwareNotFound");
        lock (_lock)
            if (_progress.ContainsKey(e.Id)) return null;

        var entry = Load().Entries.GetValueOrDefault(e.Id);
        if (entry?.Version != e.Version)
        {
            try
            {
                entry = await ReadEntryAsync(e);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return Msg.Format("PackageLibrary_CatalogUnreachable", ex.Message);
            }
            lock (_lock)
            {
                var state = Load();
                state.Entries[e.Id] = entry;
                Save(state);
            }
        }
        if (entry.Installer is not { } installer) return Msg.Format("PackageLibrary_AppProblem", e.Name, entry.Problem);

        lock (_lock)
        {
            if (_progress.ContainsKey(e.Id)) return null;
            _progress[e.Id] = (StageDownload, 0);
        }
        _ = Task.Run(() => DownloadAsync(e, installer));
        return null;
    }

    private async Task DownloadAsync(IndexEntry app, LibraryInstaller installer)
    {
        string? error = null;
        var temp = Path.Combine(dataDir, "temp", $"{Guid.NewGuid():N}{installer.Extension}");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
            await FetchAsync(app.Id, installer, temp);
            var folder = Folder(Load());
            var version = app.Version;
            var file = Path.Combine(folder, SafeName(app.Name), SafeName(version), SafeName($"{app.Name} {version}") + installer.Extension);
            // A share is written with the default identity, as it will be read at deployment.
            credentials.RunAs(credentials.Default, null, () =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                CopyWithProgress(app.Id, temp, file);
            });
            lock (_lock)
            {
                _progress[app.Id] = (StagePackage, 50);
                var state = Load();
                var previous = state.Links.GetValueOrDefault(app.Id);
                var package = SavePackage(app, version, installer, file, previous);
                state.Links[app.Id] = new LibraryLink { PackageId = package.Id, Version = version, File = file };
                state.Errors.Remove(app.Id);
                Save(state);
                if (previous != null && !string.Equals(Path.GetDirectoryName(previous.File), Path.GetDirectoryName(file), StringComparison.OrdinalIgnoreCase))
                    RemoveOldVersion(previous.File, folder);
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Téléchargement de {App} depuis la bibliothèque", app.Name);
            error = ex is DeployException or InvalidDataException ? ex.Message : Msg.Format("PackageLibrary_DownloadFailed", ex.Message);
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { }
            lock (_lock)
            {
                _progress.Remove(app.Id);
                if (error != null)
                {
                    var state = Load();
                    state.Errors[app.Id] = error;
                    Save(state);
                }
            }
        }
    }

    private async Task FetchAsync(string id, LibraryInstaller installer, string temp)
    {
        using var response = await Http.GetAsync(installer.Url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using (var input = await response.Content.ReadAsStreamAsync())
        await using (var output = File.Create(temp))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read));
                done += read;
                if (total > 0) Report(id, StageDownload, done, total.Value);
            }
        }

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var check = File.OpenRead(temp))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await check.ReadAsync(buffer)) > 0)
            {
                sha.AppendData(buffer, 0, read);
                done += read;
                Report(id, StageCheck, done, check.Length);
            }
        }
        var hash = Convert.ToHexString(sha.GetHashAndReset());
        if (!hash.Equals(installer.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(Msg.Get("PackageLibrary_HashMismatch"));
    }

    /// <summary>Copies into the library folder, following the progress.</summary>
    private void CopyWithProgress(string id, string source, string destination)
    {
        using var input = File.OpenRead(source);
        using var output = File.Create(destination);
        var buffer = new byte[1024 * 1024];
        long done = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
            done += read;
            Report(id, StageCopy, done, input.Length);
        }
    }

    private void Report(string id, string stage, long done, long total)
    {
        lock (_lock) _progress[id] = (stage, total > 0 ? (int)(done * 100 / total) : 0);
    }

    /// <summary>
    /// Creates the package, or updates the one already linked: version, file and parameters of its install step.
    /// The other settings edited by hand (conditions, added steps...) are kept.
    /// </summary>
    private Package SavePackage(IndexEntry app, string version, LibraryInstaller installer, string file, LibraryLink? previous)
    {
        if (previous != null && packageRepository.Get(previous.PackageId) is { } existing)
        {
            var step = existing.Steps.FirstOrDefault(s => s.Type == StepType.Install && string.Equals(s.File, previous.File, StringComparison.OrdinalIgnoreCase));
            if (step != null)
            {
                step.File = file;
                step.Arguments = NullIfEmpty(installer.Arguments);
                existing.Version = version;
                return packageRepository.Save(existing);
            }
        }
        return packageRepository.Save(new Package
        {
            Name = app.Name,
            Version = version,
            Description = Msg.Format("PackageLibrary_Description", app.Id),
            Steps =
            [
                new PackageStep
                {
                    Type = StepType.Install,
                    Name = Msg.Format("PackageLibrary_InstallStep", app.Name),
                    File = file,
                    Arguments = NullIfEmpty(installer.Arguments),
                },
            ],
        });
    }

    /// <summary>Deletes the folder of the old version, only if it is inside the library folder.</summary>
    private void RemoveOldVersion(string oldFile, string folder)
    {
        var dir = Path.GetDirectoryName(oldFile);
        if (dir == null || !dir.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            credentials.RunAs(credentials.Default, null, () => { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "Suppression de l'ancienne version {Dir}", dir);
        }
    }

    // ---------- Storage ----------

    private string Folder(LibraryState state) => string.IsNullOrWhiteSpace(state.Folder) ? DefaultFolder : state.Folder;

    private LibraryState Load() =>
        settingsRepository.Get(SettingKey) is { } json ? JsonSerializer.Deserialize<LibraryState>(json, Database.Json) ?? new() : new();

    private void Save(LibraryState state) => settingsRepository.Set(SettingKey, JsonSerializer.Serialize(state, Database.Json));

    /// <summary>Name usable as a folder or file name.</summary>
    internal static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        // The catalog rejects requests without an application name.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DeployCraft");
        return client;
    }

    private sealed class LibraryState
    {
        public string? Folder { get; set; }
        public DateTime? LastRefreshUtc { get; set; }
        public string? RefreshError { get; set; }
        public LibraryAutoUpdate AutoUpdate { get; set; }
        public int AutoUpdateDays { get; set; } = 3;
        public Dictionary<string, LibraryEntry> Entries { get; set; } = [];
        public Dictionary<string, LibraryLink> Links { get; set; } = [];
        public Dictionary<string, string> Errors { get; set; } = [];
    }

    private sealed class LibraryEntry
    {
        public string? Version { get; set; }
        public LibraryInstaller? Installer { get; set; }
        public string? Problem { get; set; }
        /// <summary>First time this version was read (starting point of the delay for automatic updates).</summary>
        public DateTime? SeenUtc { get; set; }
    }

    private sealed class LibraryLink
    {
        public long PackageId { get; set; }
        public string Version { get; set; } = "";
        public string File { get; set; } = "";
    }
}
