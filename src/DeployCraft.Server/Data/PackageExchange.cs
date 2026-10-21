using System.Text.Json;

namespace DeployCraft.Server.Data;

/// <summary>Export and import of packages (DeployCraft JSON files, or package XML files).</summary>
public static class PackageExchange
{
    private static readonly JsonSerializerOptions WriteOptions = new(Database.Json) { WriteIndented = true };

    /// <summary>
    /// Packages to export: the requested ones then the nested packages they use (at all levels),
    /// so that the file is self-sufficient. Unknown number: <see cref="ArgumentException"/>.
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
            p.FolderId = null; // the folders of this server mean nothing elsewhere
            result.Add(p);
            foreach (var s in p.Steps)
                if (s.Type == StepType.Nested && s.NestedPackageId is { } nested) pending.Enqueue(nested);
        }
        return new PackageExport { Format = PackageExport.FormatName, Version = PackageExport.CurrentVersion, ExportedUtc = DateTime.UtcNow, Packages = result };
    }

    public static string Serialize(PackageExport export) => JsonSerializer.Serialize(export, WriteOptions);

    /// <summary>
    /// Reads an import file: DeployCraft export (JSON) or package XML file.
    /// <paramref name="repository"/> replaces <c>$(Repository)</c> in the XML packages.
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
    /// Finishes the import: links the nested packages, makes the names unique, checks each package
    /// (<paramref name="validate"/> returns an error message or null) and, if <paramref name="save"/> is given,
    /// saves it. Without <paramref name="save"/>, it is a preview: nothing is created.
    /// A nested package is handled before the package that uses it.
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
                // XML export without the nested package: a package with the same name already in DeployCraft will do.
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
            if (r.Package == null) return; // loop detected further down the chain

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

    /// <summary>"Name", otherwise "Name (2)", "Name (3)"...; the chosen name is added to <paramref name="used"/>.</summary>
    public static string UniqueName(string name, ISet<string> used)
    {
        var candidate = name;
        for (var i = 2; !used.Add(candidate); i++) candidate = $"{name} ({i})";
        return candidate;
    }

    public static string StepLabel(PackageStep s) => string.IsNullOrWhiteSpace(s.Name) ? s.Type.ToString() : s.Name;
}
