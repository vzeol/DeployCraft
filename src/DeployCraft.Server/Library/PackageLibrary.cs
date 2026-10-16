using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using DeployCraft.Server.Data;
using DeployCraft.Server.Remote;
using Microsoft.Data.Sqlite;

namespace DeployCraft.Server.Library;

/// <summary>
/// Bibliothèque de paquets : recherche dans l'index du catalogue public winget, lit la fiche du logiciel choisi,
/// télécharge son installeur (empreinte vérifiée) dans le dossier de la bibliothèque et crée ou met à jour le paquet
/// DeployCraft correspondant. Index et téléchargements sont traités en arrière-plan ; la console suit l'état.
/// </summary>
public class PackageLibrary(Store store, DeployCredentials credentials, string dataDir, ILogger<PackageLibrary> log)
{
    private const string SettingKey = "library";
    private const string CatalogRaw = "https://raw.githubusercontent.com/microsoft/winget-pkgs/master/";
    /// <summary>Résultats affichés au plus pour une recherche.</summary>
    private const int SearchLimit = 1000;
    /// <summary>L'index est relu de lui-même quand il a plus d'un jour (vérifié chaque heure, et quand la console l'affiche).</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);

    private static readonly HttpClient Http = CreateClient();

    private const string StageDownload = "Téléchargement";
    private const string StageCheck = "Vérification du fichier";
    private const string StageCopy = "Copie dans le dossier";
    private const string StagePackage = "Création du paquet";

    private readonly object _lock = new();
    private readonly Dictionary<string, (string Stage, int Percent)> _progress = [];
    private readonly CatalogIndex _index = new(Path.Combine(dataDir, "library-index.db"));
    private bool _refreshing;

    public string DefaultFolder => Path.Combine(dataDir, "Bibliotheque");

    // ---------- État ----------

    /// <summary>
    /// Sans recherche : tout le catalogue, avec en tête les logiciels déjà téléchargés puis les logiciels courants.
    /// Avec recherche : les logiciels qui correspondent. La console charge cette liste une fois, puis suit seulement
    /// les changements avec <see cref="Status"/>.
    /// </summary>
    public LibraryInfo Info(string? search) => Read(state =>
    {
        if (string.IsNullOrWhiteSpace(search)) return Ordered(_index.All(), state);
        return _index.Search(search, SearchLimit);
    });

    /// <summary>
    /// Seulement les logiciels qui ont un état propre : téléchargement en cours, erreur, ou paquet créé.
    /// Petit : la console le demande toutes les deux secondes pour mettre à jour ces lignes.
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
                // Index remplacé pendant la lecture : la console redemandera dans un instant.
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

    /// <summary>Logiciels déjà téléchargés, puis logiciels courants, puis le reste par ordre alphabétique.</summary>
    private List<IndexEntry> Ordered(List<IndexEntry> all, LibraryState state)
    {
        var linked = state.Links.Where(l => store.GetPackage(l.Value.PackageId) != null).Select(l => l.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var suggested = LibraryCatalog.Suggested.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. all.OrderBy(e => linked.Contains(e.Id) ? 0 : suggested.Contains(e.Id) ? 1 : 2)];
    }

    private LibraryItem Item(IndexEntry e, LibraryState state)
    {
        // La fiche lue ne vaut que pour la version qu'elle décrit.
        var entry = state.Entries.GetValueOrDefault(e.Id) is { } known && known.Version == e.Version ? known : null;
        var link = state.Links.GetValueOrDefault(e.Id);
        // Un paquet supprimé dans DeployCraft n'est plus relié.
        if (link != null && store.GetPackage(link.PackageId) == null) link = null;
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

    public const int MinAutoUpdateDays = 1;
    public const int MaxAutoUpdateDays = 90;

    /// <summary>Mise à jour des paquets de la bibliothèque : manuelle, dès la nouvelle version, ou après quelques jours.</summary>
    public string? SetAutoUpdate(LibraryAutoUpdate mode, int days)
    {
        if (!Enum.IsDefined(mode)) return "Mode de mise à jour inconnu.";
        if (days is < MinAutoUpdateDays or > MaxAutoUpdateDays)
            return $"Le délai doit être compris entre {MinAutoUpdateDays} et {MaxAutoUpdateDays} jours.";
        lock (_lock)
        {
            var state = Load();
            state.AutoUpdate = mode;
            state.AutoUpdateDays = days;
            Save(state);
        }
        return null;
    }

    // ---------- Lecture du catalogue ----------

    /// <summary>Relit l'index en arrière-plan (sans effet si une lecture est déjà en cours).</summary>
    public void StartRefresh()
    {
        if (TryStartRefresh()) _ = Task.Run(RefreshAsync);
    }

    /// <summary>
    /// Passage du service (chaque heure) : relit l'index s'il a plus d'un jour, même si personne n'ouvre la console.
    /// Les mises à jour automatiques suivent chaque lecture réussie.
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
    /// Télécharge l'index, puis relit les fiches des logiciels affichés sans recherche dont la version a changé
    /// (pour indiquer tout de suite leur installeur, ou pourquoi ils ne sont pas disponibles).
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
            state.RefreshError = error;
            if (error == null) state.LastRefreshUtc = DateTime.UtcNow;
            Save(state);
            _refreshing = false;
        }
        if (error == null) await StartAutoUpdatesAsync(DateTime.UtcNow);
    }

    /// <summary>Lance la mise à jour des paquets dont la nouvelle version est due, selon le réglage.</summary>
    private async Task StartAutoUpdatesAsync(DateTime nowUtc)
    {
        var state = Load();
        if (state.AutoUpdate == LibraryAutoUpdate.Manual) return;
        var latest = _index.Get(state.Links.Keys);
        foreach (var (id, link) in state.Links)
        {
            if (store.GetPackage(link.PackageId) == null || latest.GetValueOrDefault(id) is not { } e) continue;
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
    /// Une nouvelle version est à installer d'elle-même : tout de suite, ou une fois le délai passé depuis que le
    /// catalogue l'a fait connaître (date inconnue : version lue avant ce réglage, donc déjà ancienne).
    /// </summary>
    internal static bool IsUpdateDue(LibraryAutoUpdate mode, int days, string current, string latest, DateTime? seenUtc, DateTime nowUtc)
    {
        if (mode == LibraryAutoUpdate.Manual || CatalogManifest.CompareVersions(latest, current) <= 0) return false;
        return mode == LibraryAutoUpdate.Immediate || seenUtc == null || nowUtc - seenUtc.Value >= TimeSpan.FromDays(days);
    }

    /// <summary>Fiche d'installation de la dernière version : installeur retenu, ou raison de son absence.</summary>
    private static async Task<LibraryEntry> ReadEntryAsync(IndexEntry e)
    {
        var url = $"{CatalogRaw}{Escape(LibraryCatalog.ManifestFolder(e.Id))}/{Escape(e.Version)}/{Escape(e.Id)}.installer.yaml";
        using var response = await Http.GetAsync(url);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new LibraryEntry { Version = e.Version, Problem = "fiche d'installation introuvable dans le catalogue.", SeenUtc = DateTime.UtcNow };
        response.EnsureSuccessStatusCode();
        var installer = CatalogManifest.Choose(await response.Content.ReadAsStringAsync(), out var problem);
        return new LibraryEntry { Version = e.Version, Installer = installer, Problem = problem, SeenUtc = DateTime.UtcNow };
    }

    private static string Escape(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    // ---------- Téléchargement ----------

    /// <summary>
    /// Lit la fiche du logiciel puis lance le téléchargement en arrière-plan ; renvoie une erreur s'il ne peut pas
    /// commencer (pas d'installeur silencieux pour tout le poste…).
    /// </summary>
    public async Task<string?> StartDownloadAsync(string id)
    {
        if (!_index.Exists) return "Le catalogue n'a pas encore été lu : patientez quelques secondes.";
        if (_index.Get([id]).GetValueOrDefault(id) is not { } e) return "Logiciel introuvable dans le catalogue.";
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
                return $"Catalogue injoignable ({ex.Message}). Vérifiez l'accès à Internet du serveur.";
            }
            lock (_lock)
            {
                var state = Load();
                state.Entries[e.Id] = entry;
                Save(state);
            }
        }
        if (entry.Installer is not { } installer) return $"{e.Name} : {entry.Problem}";

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
            // Un partage s'écrit avec l'identifiant par défaut, comme il sera lu au déploiement.
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
            throw new InvalidDataException("Le fichier téléchargé ne correspond pas à l'empreinte du catalogue : il n'a pas été gardé. Réessayez plus tard.");
    }

    /// <summary>Copie dans le dossier de la bibliothèque, en suivant l'avancement.</summary>
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
    /// Crée le paquet, ou met à jour celui déjà relié : version, fichier et paramètres de son étape d'installation.
    /// Les autres réglages modifiés à la main (conditions, étapes ajoutées…) sont gardés.
    /// </summary>
    private Package SavePackage(IndexEntry app, string version, LibraryInstaller installer, string file, LibraryLink? previous)
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
            Description = $"Bibliothèque de paquets (winget : {app.Id})",
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
        /// <summary>Première lecture de cette version (point de départ du délai des mises à jour automatiques).</summary>
        public DateTime? SeenUtc { get; set; }
    }

    private sealed class LibraryLink
    {
        public long PackageId { get; set; }
        public string Version { get; set; } = "";
        public string File { get; set; } = "";
    }
}
