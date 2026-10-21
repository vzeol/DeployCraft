using System.Text.Json.Serialization;

namespace DeployCraft.Server.Data;

// Types shared with the console: export file and import report.

/// <summary>DeployCraft package export file (JSON). Nested packages that are used are always included.</summary>
public class PackageExport
{
    public const string FormatName = "DeployCraft.Packages";
    public const int CurrentVersion = 1;

    /// <summary>Empty in a JSON file that is not a DeployCraft export.</summary>
    public string Format { get; set; } = "";
    public int Version { get; set; }
    public DateTime ExportedUtc { get; set; }
    public List<Package> Packages { get; set; } = [];
}

/// <summary>Result of an imported item: taken as is, taken with reservations, or not taken.</summary>
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
    /// <summary>Name in the imported file.</summary>
    public string Name { get; set; } = "";
    /// <summary>Name in DeployCraft ("Name (2)" if the name is already taken); null if the package is not taken.</summary>
    public string? FinalName { get; set; }
    public long? NewId { get; set; }
    public ImportState State { get; set; }
    public List<string> Notes { get; set; } = [];
    public List<ImportStepResult> Steps { get; set; } = [];

    /// <summary>Package to create (null: not taken).</summary>
    [JsonIgnore] public Package? Package { get; set; }
    /// <summary>References of this package in the file (number, path or name) for the "nested package" steps.</summary>
    [JsonIgnore] public List<string> Keys { get; set; } = [];
    /// <summary>"Nested package" step → reference of the targeted package in the file.</summary>
    [JsonIgnore] public Dictionary<PackageStep, string> NestedRefs { get; } = new(ReferenceEqualityComparer.Instance);
    /// <summary>Package step → its line in the report.</summary>
    [JsonIgnore] public Dictionary<PackageStep, ImportStepResult> StepResults { get; } = new(ReferenceEqualityComparer.Instance);

    public void Warn(string note) => Notes.Add(note);

    public void Skip(string note)
    {
        Notes.Add(note);
        Package = null;
    }
}

/// <summary>Import report: preview (nothing is created) or actual import.</summary>
public class ImportReport
{
    public const string SourceDeployCraft = "DeployCraft";
    /// <summary>XML packages file.</summary>
    public const string SourceXml = "XML";

    /// <summary><see cref="SourceDeployCraft"/> or <see cref="SourceXml"/>.</summary>
    public string Source { get; set; } = "";
    public bool Applied { get; set; }
    public List<string> Notes { get; set; } = [];
    public List<ImportPackageResult> Packages { get; set; } = [];
}
