using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using DeployCraft.Server.Data;
using DeployCraft.Server.Remote;

namespace DeployCraft.Server.Library;

/// <summary>
/// Bibliothèque de paquets : lit le catalogue public winget pour les logiciels de <see cref="LibraryCatalog"/>,
/// télécharge l'installeur choisi (empreinte vérifiée) dans le dossier de la bibliothèque et crée ou met à jour
/// le paquet DeployCraft correspondant. Lecture et téléchargements se font en arrière-plan ; la console suit l'état.
/// </summary>
public class PackageLibrary(Store store, DeployCredentials credentials, string dataDir, ILogger<PackageLibrary> log)
{
    private const string SettingKey = "library";
    private const string CatalogApi = "https://api.github.com/repos/microsoft/winget-pkgs/contents/";
    private const string CatalogRaw = "https://raw.githubusercontent.com/microsoft/winget-pkgs/master/";
    /// <summary>Le catalogue est relu de lui-même quand la console l'affiche et qu'il a plus d'un jour.</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);

    private static readonly HttpClient Http = CreateClient();

    private readonly object _lock = new();
    private readonly Dictionary<string, int> _progress = [];
    private bool _refreshing;

    public string DefaultFolder => Path.Combine(dataDir, "Bibliotheque");

    // ---------- État ----------

    public LibraryInfo Info()
    {
        var state = Load();
        if (!_refreshing && (state.LastRefreshUtc == null || DateTime.UtcNow - state.LastRefreshUtc > MaxAge)) StartRefresh();
        lock (_lock)
        {
            return new LibraryInfo
            {
                Folder = Folder(state),
                DefaultFolder = string.IsNullOrWhiteSpace(state.Folder),
                LastRefreshUtc = state.LastRefreshUtc,
                Refreshing = _refreshing,
                RefreshError = state.RefreshError,
                Items = LibraryCatalog.Apps.Select(app => Item(app, state)).ToList(),
            };
        }
    }

    private LibraryItem Item(LibraryApp app, LibraryState state)
    {
        var entry = state.Entries.GetValueOrDefault(app.Id);
        var link = state.Links.GetValueOrDefault(app.Id);
        // Un paquet supprimé dans DeployCraft n'est plus relié.
        if (link != null && store.GetPackage(link.PackageId) == null) link = null;
        return new LibraryItem
        {
            Id = app.Id,
            Name = app.Name,
            Category = app.Category,
            LatestVersion = entry?.Version,
            Installer = entry?.Installer is { } i ? $"{i.Type} {i.Architecture}" : null,
            Problem = entry?.Problem,
            PackageId = link?.PackageId,
            PackageVersion = link?.Version,
            Progress = _progress.TryGetValue(app.Id, out var p) ? p : null,
            Error = state.Errors.GetValueOrDefault(app.Id),
            UpdateAvailable = link != null && entry?.Installer != null && WingetManifest.CompareVersions(entry.Version, link.Version) > 0,
        };
    }

    /// <summary>Dossier des installeurs : un partage est conseillé (lisible par les postes en mode pull).</summary>
    public string? SetFolder(string? folder)
    {
        folder = folder?.Trim().TrimEnd('\\');
        if (!string.IsNullOrEmpty(folder) && !Path.IsPathFullyQualified(folder))
            return "Indiquez un chemin complet : un partage (\\\\SERVEUR\\Partage\\Bibliotheque) ou un dossier du serveur (D:\\Bibliotheque).";
        lock (_lock)
        {
            var state = Load();
            state.Folder = string.IsNullOrEmpty(folder) ? null : folder;
            Save(state);
        }
        return null;
    }

    // ---------- Lecture du catalogue ----------

    /// <summary>Relit le catalogue en arrière-plan (sans effet si une lecture est déjà en cours).</summary>
    public void StartRefresh()
    {
        lock (_lock)
        {
            if (_refreshing) return;
            _refreshing = true;
        }
        _ = Task.Run(RefreshAsync);
    }

    private async Task RefreshAsync()
    {
        var entries = new Dictionary<string, LibraryEntry>();
        string? error = null;
        try
        {
            foreach (var app in LibraryCatalog.Apps)
            {
                try
                {
                    entries[app.Id] = await ReadEntryAsync(app);
                }
                catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                {
                    error = "Le catalogue limite le nombre de lectures par heure : réessayez dans une heure.";
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            error = $"Catalogue injoignable ({ex.Message}). Vérifiez l'accès à Internet du serveur.";
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Lecture du catalogue de la bibliothèque");
            error = $"Lecture du catalogue impossible : {ex.Message}";
        }
        lock (_lock)
        {
            var state = Load();
            // Une lecture interrompue garde les fiches déjà connues des logiciels non relus.
            foreach (var (id, entry) in entries) state.Entries[id] = entry;
            state.RefreshError = error;
            if (error == null) state.LastRefreshUtc = DateTime.UtcNow;
            Save(state);
            _refreshing = false;
        }
    }

    private static async Task<LibraryEntry> ReadEntryAsync(LibraryApp app)
    {
        var folder = LibraryCatalog.ManifestFolder(app.Id);
        using var listing = JsonDocument.Parse(await Http.GetStringAsync(CatalogApi + Escape(folder)));
        var version = listing.RootElement.EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() == "dir")
            .Select(e => e.GetProperty("name").GetString() ?? "")
            .Where(v => v.Length > 0 && char.IsDigit(v[0]))
            .OrderByDescending(v => v, Comparer<string>.Create(WingetManifest.CompareVersions))
            .FirstOrDefault();
        if (version == null) return new LibraryEntry { Problem = "introuvable dans le catalogue." };
        var yaml = await Http.GetStringAsync($"{CatalogRaw}{Escape(folder)}/{Escape(version)}/{Escape(app.Id)}.installer.yaml");
        var installer = WingetManifest.Choose(yaml, out var problem);
        return new LibraryEntry { Version = version, Installer = installer, Problem = problem };
    }

    private static string Escape(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    // ---------- Téléchargement ----------

    /// <summary>Lance le téléchargement d'un logiciel ; renvoie une erreur s'il ne peut pas commencer.</summary>
    public string? StartDownload(string id)
    {
        if (LibraryCatalog.Apps.FirstOrDefault(a => a.Id == id) is not { } app) return "Logiciel inconnu de la bibliothèque.";
        LibraryEntry? entry;
        lock (_lock)
        {
            entry = Load().Entries.GetValueOrDefault(id);
            if (entry?.Installer == null) return entry?.Problem is { } p ? $"{app.Name} : {p}" : "Le catalogue n'a pas encore été lu : actualisez-le.";
            if (_progress.ContainsKey(id)) return null;
            _progress[id] = 0;
        }
        _ = Task.Run(() => DownloadAsync(app, entry.Version!, entry.Installer));
        return null;
    }

    private async Task DownloadAsync(LibraryApp app, string version, LibraryInstaller installer)
    {
        string? error = null;
        var temp = Path.Combine(dataDir, "temp", $"{Guid.NewGuid():N}{installer.Extension}");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
            await FetchAsync(app.Id, installer, temp);
            var folder = Folder(Load());
            var file = Path.Combine(folder, SafeName(app.Name), SafeName(version), SafeName($"{app.Name} {version}") + installer.Extension);
            // Un partage s'écrit avec l'identifiant par défaut, comme il sera lu au déploiement.
            credentials.RunAs(credentials.Default, null, () =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.Copy(temp, file, overwrite: true);
            });
            lock (_lock)
            {
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
            error = ex is DeployException or InvalidDataException ? ex.Message : $"Téléchargement impossible : {ex.Message}";
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
                if (total > 0) lock (_lock) _progress[id] = (int)(done * 100 / total.Value);
            }
        }
        await using var check = File.OpenRead(temp);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(check));
        if (!hash.Equals(installer.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Le fichier téléchargé ne correspond pas à l'empreinte du catalogue : il n'a pas été gardé. Réessayez plus tard.");
    }

    /// <summary>
    /// Crée le paquet, ou met à jour celui déjà relié : version, fichier et paramètres de son étape d'installation.
    /// Les autres réglages modifiés à la main (conditions, étapes ajoutées…) sont gardés.
    /// </summary>
    private Package SavePackage(LibraryApp app, string version, LibraryInstaller installer, string file, LibraryLink? previous)
    {
        if (previous != null && store.GetPackage(previous.PackageId) is { } existing)
        {
            var step = existing.Steps.FirstOrDefault(s => s.Type == StepType.Install && string.Equals(s.File, previous.File, StringComparison.OrdinalIgnoreCase));
            if (step != null)
            {
                step.File = file;
                step.Arguments = NullIfEmpty(installer.Arguments);
                existing.Version = version;
                return store.SavePackage(existing);
            }
        }
        return store.SavePackage(new Package
        {
            Name = app.Name,
            Version = version,
            Description = $"Bibliothèque de paquets ({app.Category})",
            Steps =
            [
                new PackageStep
                {
                    Type = StepType.Install,
                    Name = $"Installer {app.Name}",
                    File = file,
                    Arguments = NullIfEmpty(installer.Arguments),
                },
            ],
        });
    }

    /// <summary>Supprime le dossier de l'ancienne version, seulement s'il est bien dans le dossier de la bibliothèque.</summary>
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

    // ---------- Stockage ----------

    private string Folder(LibraryState state) => string.IsNullOrWhiteSpace(state.Folder) ? DefaultFolder : state.Folder;

    private LibraryState Load() =>
        store.GetSetting(SettingKey) is { } json ? JsonSerializer.Deserialize<LibraryState>(json, Store.Json) ?? new() : new();

    private void Save(LibraryState state) => store.SetSetting(SettingKey, JsonSerializer.Serialize(state, Store.Json));

    /// <summary>Nom utilisable comme nom de dossier ou de fichier.</summary>
    internal static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        // Le catalogue refuse les demandes sans nom d'application.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DeployCraft");
        return client;
    }

    private sealed class LibraryState
    {
        public string? Folder { get; set; }
        public DateTime? LastRefreshUtc { get; set; }
        public string? RefreshError { get; set; }
        public Dictionary<string, LibraryEntry> Entries { get; set; } = [];
        public Dictionary<string, LibraryLink> Links { get; set; } = [];
        public Dictionary<string, string> Errors { get; set; } = [];
    }

    private sealed class LibraryEntry
    {
        public string? Version { get; set; }
        public LibraryInstaller? Installer { get; set; }
        public string? Problem { get; set; }
    }

    private sealed class LibraryLink
    {
        public long PackageId { get; set; }
        public string Version { get; set; } = "";
        public string File { get; set; } = "";
    }
}
