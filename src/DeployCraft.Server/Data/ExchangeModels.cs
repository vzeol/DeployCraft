using System.Text.Json.Serialization;

namespace DeployCraft.Server.Data;

// Types partagés avec la console : fichier d'export et compte rendu d'import.

/// <summary>Fichier d'export de paquets DeployCraft (JSON). Les paquets imbriqués utilisés sont toujours inclus.</summary>
public class PackageExport
{
    public const string FormatName = "DeployCraft.Packages";
    public const int CurrentVersion = 1;

    /// <summary>Vide dans un fichier JSON qui n'est pas un export DeployCraft.</summary>
    public string Format { get; set; } = "";
    public int Version { get; set; }
    public DateTime ExportedUtc { get; set; }
    public List<Package> Packages { get; set; } = [];
}

/// <summary>Résultat d'un élément importé : repris tel quel, repris avec des réserves, ou non repris.</summary>
public enum ImportState { Ok, Warning, Skipped }

public class ImportStepResult
{
    public string Name { get; set; } = "";
    public ImportState State { get; set; }
    public List<string> Notes { get; set; } = [];

    public void Warn(string note)
    {
        Notes.Add(note);
        if (State == ImportState.Ok) State = ImportState.Warning;
    }

    public void Skip(string note)
    {
        Notes.Add(note);
        State = ImportState.Skipped;
    }
}

public class ImportPackageResult
{
    /// <summary>Nom dans le fichier importé.</summary>
    public string Name { get; set; } = "";
    /// <summary>Nom dans DeployCraft (« Nom (2) » si le nom est déjà pris) ; null si le paquet n'est pas repris.</summary>
    public string? FinalName { get; set; }
    public long? NewId { get; set; }
    public ImportState State { get; set; }
    public List<string> Notes { get; set; } = [];
    public List<ImportStepResult> Steps { get; set; } = [];

    /// <summary>Paquet à créer (null : non repris).</summary>
    [JsonIgnore] public Package? Package { get; set; }
    /// <summary>Références de ce paquet dans le fichier (numéro, chemin ou nom) pour les étapes « paquet imbriqué ».</summary>
    [JsonIgnore] public List<string> Keys { get; set; } = [];
    /// <summary>Étape « paquet imbriqué » → référence du paquet visé dans le fichier.</summary>
    [JsonIgnore] public Dictionary<PackageStep, string> NestedRefs { get; } = new(ReferenceEqualityComparer.Instance);
    /// <summary>Étape du paquet → sa ligne dans le compte rendu.</summary>
    [JsonIgnore] public Dictionary<PackageStep, ImportStepResult> StepResults { get; } = new(ReferenceEqualityComparer.Instance);

    public void Warn(string note) => Notes.Add(note);

    public void Skip(string note)
    {
        Notes.Add(note);
        Package = null;
    }
}

/// <summary>Compte rendu d'un import : aperçu (rien n'est créé) ou import réel.</summary>
public class ImportReport
{
    public const string SourceDeployCraft = "DeployCraft";
    /// <summary>Export XML de paquets d'un autre outil de déploiement.</summary>
    public const string SourceXml = "XML";

    /// <summary><see cref="SourceDeployCraft"/> ou <see cref="SourceXml"/>.</summary>
    public string Source { get; set; } = "";
    public bool Applied { get; set; }
    public List<string> Notes { get; set; } = [];
    public List<ImportPackageResult> Packages { get; set; } = [];
}
