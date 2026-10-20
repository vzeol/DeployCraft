using System.Text.Json;

namespace DeployCraft.Server.Data;

/// <summary>Export et import de paquets (fichiers JSON DeployCraft, ou fichiers XML de paquets).</summary>
public static class PackageExchange
{
    private static readonly JsonSerializerOptions WriteOptions = new(Database.Json) { WriteIndented = true };

    /// <summary>
    /// Paquets à exporter : ceux demandés puis les paquets imbriqués qu'ils utilisent (à tous les niveaux),
    /// pour que le fichier se suffise à lui-même. Numéro inconnu : <see cref="ArgumentException"/>.
    /// </summary>
    public static PackageExport Export(IEnumerable<long> ids, Func<long, Package?> lookup)
    {
        var result = new List<Package>();
        var seen = new HashSet<long>();
        var pending = new Queue<long>(ids);
        while (pending.TryDequeue(out var id))
        {
            if (!seen.Add(id)) continue;
            var p = lookup(id) ?? throw new ArgumentException($"Paquet introuvable (n° {id}).");
            p.FolderId = null; // les dossiers de ce serveur ne veulent rien dire ailleurs
            result.Add(p);
            foreach (var s in p.Steps)
                if (s.Type == StepType.Nested && s.NestedPackageId is { } nested) pending.Enqueue(nested);
        }
        return new PackageExport { Format = PackageExport.FormatName, Version = PackageExport.CurrentVersion, ExportedUtc = DateTime.UtcNow, Packages = result };
    }

    public static string Serialize(PackageExport export) => JsonSerializer.Serialize(export, WriteOptions);

    /// <summary>
    /// Lit un fichier d'import : export DeployCraft (JSON) ou fichier XML de paquets.
    /// <paramref name="repository"/> remplace <c>$(Repository)</c> dans les paquets XML.
    /// </summary>
    public static ImportReport Read(string content, string? repository = null)
    {
        var text = content.TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (text.StartsWith('<')) return XmlPackageImport.Read(text, repository);
        if (text.StartsWith('{')) return ReadDeployCraft(text);
        throw new FormatException("Ce fichier n'est ni un export DeployCraft (.json) ni un export de paquets XML (.xml).");
    }

    private static ImportReport ReadDeployCraft(string json)
    {
        PackageExport? export;
        try
        {
            export = JsonSerializer.Deserialize<PackageExport>(json, Database.Json);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Fichier DeployCraft illisible : {ex.Message}");
        }
        if (export?.Format != PackageExport.FormatName) throw new FormatException("Ce fichier JSON n'est pas un export de paquets DeployCraft.");
        if (export.Version > PackageExport.CurrentVersion)
            throw new FormatException("Ce fichier vient d'une version plus récente de DeployCraft : mettez à jour le service avant de l'importer.");

        var report = new ImportReport { Source = ImportReport.SourceDeployCraft };
        foreach (var p in export.Packages)
        {
            var result = new ImportPackageResult { Name = p.Name, Package = p, Keys = [p.Id.ToString()] };
            p.Conditions ??= new();
            foreach (var s in p.Steps)
            {
                s.Conditions ??= new();
                var step = new ImportStepResult { Name = StepLabel(s) };
                result.Steps.Add(step);
                result.StepResults[s] = step;
                if (s.Type == StepType.Nested && s.NestedPackageId is { } target)
                {
                    result.NestedRefs[s] = target.ToString();
                    s.NestedPackageId = null;
                }
            }
            report.Packages.Add(result);
        }
        return report;
    }

    /// <summary>
    /// Termine l'import : relie les paquets imbriqués, rend les noms uniques, vérifie chaque paquet
    /// (<paramref name="validate"/> renvoie un message d'erreur ou null) et, si <paramref name="save"/> est donné,
    /// l'enregistre. Sans <paramref name="save"/>, c'est un aperçu : rien n'est créé.
    /// Un paquet imbriqué est traité avant le paquet qui l'utilise.
    /// </summary>
    public static void Complete(ImportReport report, IReadOnlyCollection<Package> existing,
        Func<Package, Func<long, Package?>, string?> validate, Func<Package, Package>? save)
    {
        var byKey = new Dictionary<string, ImportPackageResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in report.Packages.Where(r => r.Package != null))
            foreach (var key in r.Keys)
                byKey.TryAdd(key, r);

        var existingById = existing.ToDictionary(p => p.Id);
        var batchById = new Dictionary<long, Package>();
        var usedNames = new HashSet<string>(existing.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        var nextTempId = -1L;
        var done = new HashSet<ImportPackageResult>();
        var inProgress = new HashSet<ImportPackageResult>();

        foreach (var r in report.Packages) Process(r);

        foreach (var r in report.Packages) Finish(r);
        report.Applied = save != null;
        return;

        void Process(ImportPackageResult r)
        {
            if (r.Package == null || done.Contains(r)) return;
            if (!inProgress.Add(r))
            {
                r.Skip("Boucle de paquets imbriqués dans le fichier : paquet non repris.");
                return;
            }

            var p = r.Package;
            foreach (var (step, key) in r.NestedRefs)
            {
                if (byKey.TryGetValue(key, out var target) && target != r)
                {
                    Process(target);
                    if (target.Package != null)
                    {
                        step.NestedPackageId = target.Package.Id;
                        continue;
                    }
                }
                // Export XML sans le paquet imbriqué : un paquet du même nom déjà dans DeployCraft fait l'affaire.
                var name = key.Split('\\')[^1];
                if (report.Source != ImportReport.SourceDeployCraft && existing.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } found)
                {
                    step.NestedPackageId = found.Id;
                    r.StepResults[step].Warn($"Relié au paquet « {found.Name} » déjà présent dans DeployCraft.");
                    continue;
                }
                r.StepResults[step].Skip("Paquet imbriqué absent du fichier et de DeployCraft : étape non reprise.");
                p.Steps.Remove(step);
            }

            inProgress.Remove(r);
            if (r.Package == null) return; // boucle détectée plus bas dans la chaîne

            p.Name = UniqueName(p.Name.Trim(), usedNames);
            if (!p.Name.Equals(r.Name.Trim(), StringComparison.Ordinal)) r.Warn($"Nom déjà pris : importé sous le nom « {p.Name} ».");
            p.Id = 0;
            p.FolderId = null;

            var error = validate(p, id => id == p.Id ? p : batchById.GetValueOrDefault(id) ?? existingById.GetValueOrDefault(id));
            if (error != null)
            {
                usedNames.Remove(p.Name);
                r.Skip($"Non repris : {error}");
                done.Add(r);
                return;
            }

            if (save != null)
            {
                save(p);
                r.NewId = p.Id;
            }
            else
            {
                p.Id = nextTempId--;
            }
            batchById[p.Id] = p;
            r.FinalName = p.Name;
            done.Add(r);
        }
    }

    private static void Finish(ImportPackageResult r)
    {
        if (r.Package == null)
        {
            r.State = ImportState.Skipped;
            r.FinalName = null;
            r.NewId = null;
            return;
        }
        r.State = r.Notes.Count > 0 || r.Steps.Any(s => s.State != ImportState.Ok) ? ImportState.Warning : ImportState.Ok;
    }

    /// <summary>« Nom », sinon « Nom (2) », « Nom (3) »… ; le nom retenu est ajouté à <paramref name="used"/>.</summary>
    public static string UniqueName(string name, ISet<string> used)
    {
        var candidate = name;
        for (var i = 2; !used.Add(candidate); i++) candidate = $"{name} ({i})";
        return candidate;
    }

    public static string StepLabel(PackageStep s) => string.IsNullOrWhiteSpace(s.Name) ? s.Type.ToString() : s.Name;
}
