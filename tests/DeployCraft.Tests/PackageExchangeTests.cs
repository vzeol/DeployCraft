using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

/// <summary>Export et import de paquets : format DeployCraft (JSON) et fichiers d'export de PDQ Deploy (XML).</summary>
public class PackageExchangeTests
{
    private readonly Dictionary<long, Package> _library = [];
    private long _nextId = 100;

    private Package Add(long id, string name, params PackageStep[] steps)
    {
        var package = new Package { Id = id, Name = name, Steps = [.. steps] };
        _library[id] = package;
        return package;
    }

    private static PackageStep Cmd(string name) => new() { Name = name, Type = StepType.Command, Command = "echo " + name };

    private static PackageStep Nested(long id) => new() { Name = "imbriqué", Type = StepType.Nested, NestedPackageId = id };

    /// <summary>Même vérification que le service : paquets imbriqués existants, sans boucle.</summary>
    private static string? Validate(Package p, Func<long, Package?> lookup)
    {
        try
        {
            p.Flatten(lookup);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    private Package Save(Package p)
    {
        p.Id = _nextId++;
        _library[p.Id] = p;
        return p;
    }

    private ImportReport Import(string content, bool apply = true, string? repository = null)
    {
        var report = PackageExchange.Read(content, repository);
        PackageExchange.Complete(report, [.. _library.Values], Validate, apply ? Save : null);
        return report;
    }

    // ---------- Format DeployCraft ----------

    [Fact]
    public void Export_IncludesNestedPackages_AtEveryLevel()
    {
        Add(3, "Pilotes", Cmd("a"));
        Add(2, "Outils", Nested(3));
        Add(1, "Poste", Nested(2), Cmd("fin"));
        Add(4, "Autre", Cmd("x"));

        var export = PackageExchange.Export([1], _library.GetValueOrDefault);

        Assert.Equal(["Poste", "Outils", "Pilotes"], export.Packages.Select(p => p.Name));
        Assert.Equal(PackageExport.FormatName, export.Format);
    }

    [Fact]
    public void Export_UnknownPackage_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => PackageExchange.Export([42], _library.GetValueOrDefault));
    }

    [Fact]
    public void Import_DeployCraft_CreatesPackages_AndRelinksNestedPackages()
    {
        Add(2, "Outils", Cmd("a"));
        Add(1, "Poste", Nested(2));
        var json = PackageExchange.Serialize(PackageExchange.Export([1], _library.GetValueOrDefault));

        var report = Import(json);

        Assert.True(report.Applied);
        var poste = report.Packages.Single(r => r.Name == "Poste");
        var outils = report.Packages.Single(r => r.Name == "Outils");
        Assert.Equal("Poste (2)", poste.FinalName);
        Assert.Equal("Outils (2)", outils.FinalName);
        Assert.Equal(outils.NewId, _library[poste.NewId!.Value].Steps[0].NestedPackageId);
        Assert.All(report.Packages, r => Assert.Equal(ImportState.Warning, r.State)); // renommés
    }

    [Fact]
    public void Import_Preview_CreatesNothing()
    {
        Add(1, "Poste", Cmd("a"));
        var json = PackageExchange.Serialize(PackageExchange.Export([1], _library.GetValueOrDefault));

        var report = Import(json, apply: false);

        Assert.False(report.Applied);
        Assert.Single(_library);
        Assert.Equal("Poste (2)", report.Packages[0].FinalName);
        Assert.Null(report.Packages[0].NewId);
    }

    [Fact]
    public void Import_UniqueNames_CountUp()
    {
        var used = new HashSet<string>(["A", "A (2)"], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("a (3)", PackageExchange.UniqueName("a", used));
        Assert.Equal("B", PackageExchange.UniqueName("B", used));
    }

    [Fact]
    public void Import_LoopBetweenFilePackages_IsNotImported()
    {
        const string json = """
            { "format": "DeployCraft.Packages", "version": 1, "packages": [
              { "id": 1, "name": "A", "steps": [ { "name": "x", "type": "Nested", "nestedPackageId": 2 } ] },
              { "id": 2, "name": "B", "steps": [ { "name": "y", "type": "Nested", "nestedPackageId": 1 } ] } ] }
            """;

        var report = Import(json);

        Assert.Contains(report.Packages, r => r.State == ImportState.Skipped && r.Notes.Any(n => n.Contains("Boucle")));
    }

    [Fact]
    public void Import_NewerVersionOrOtherJson_IsRefused()
    {
        Assert.Throws<FormatException>(() => PackageExchange.Read("""{ "format": "DeployCraft.Packages", "version": 99 }"""));
        Assert.Throws<FormatException>(() => PackageExchange.Read("""{ "name": "x" }"""));
        Assert.Throws<FormatException>(() => PackageExchange.Read("bonjour"));
    }

    // ---------- Export de PDQ Deploy ----------

    private static string Pdq(string packages) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <AdminArsenal.Export Code="PDQDeploy" Name="PDQ Deploy" Version="20.1.0.0" MinimumVersion="17.0">{packages}</AdminArsenal.Export>
        """;

    private static string PdqPackage(string name, string steps, string conditions = "", string options = "") => $"""
        <Package>
          <PackageDefinition name="Definition">
            <Conditions type="list">{conditions}</Conditions>
            <Steps type="list">{steps}</Steps>{options}
          </PackageDefinition>
          <Description>Exemple</Description>
          <Version>1.0</Version>
          <Name>{name}</Name>
          <Path>{name}</Path>
        </Package>
        """;

    private static string Step(string type, string element, string body, string extra = "") => $"""
        <{element}>{body}<Conditions type="list">{extra}</Conditions><ErrorMode>StopDeploymentFail</ErrorMode>
          <Title>{type}</Title><TypeName>{type}</TypeName><IsEnabled value="true" /><IsPostStep value="false" /><IsPreStep value="false" /></{element}>
        """;

    [Fact]
    public void Pdq_StepTypes_AreConverted()
    {
        var xml = Pdq(PdqPackage("Logiciel",
            Step("Install", "InstallStep", "<FileName>\\\\SERVEUR-01\\Partage\\app.msi</FileName><Parameters>ALLUSERS=1</Parameters><SuccessCodes>0,3010</SuccessCodes><MsiOperation>Install</MsiOperation><MsiQuiet value=\"true\" /><IncludeDirectory value=\"true\" />") +
            Step("Command", "CommandStep", "<Command>echo ok</Command><SuccessCodes>0</SuccessCodes>") +
            Step("PowerShell", "PowerShellStep", "<Script>Write-Output 1</Script><SuccessCodes>0</SuccessCodes>") +
            Step("FileCopy", "FileCopyStep", "<Mode>Folder</Mode><Source>\\\\SERVEUR-01\\Config</Source><Target>%ProgramData%\\App</Target><AllowOverwrite value=\"true\" /><IncludeSubFolders value=\"true\" /><CopyAllFiles value=\"false\" /><FilePatternsText>*.ini\r\n*.cfg</FilePatternsText>") +
            Step("Sleep", "SleepStep", "<Seconds value=\"20\" />"),
            options: "<Timeout value=\"45\" /><UseCustomTimeout value=\"true\" /><RunAs>LocalSystem</RunAs><CopyMode>Pull</CopyMode>"));

        var report = Import(xml);

        var result = report.Packages.Single();
        Assert.Equal(ImportState.Ok, result.State);
        var p = _library[result.NewId!.Value];
        Assert.Equal([StepType.Install, StepType.Command, StepType.PowerShell, StepType.CopyFiles, StepType.Pause], p.Steps.Select(s => s.Type));
        Assert.Equal(@"\\SERVEUR-01\Partage\app.msi", p.Steps[0].File);
        Assert.Equal("ALLUSERS=1", p.Steps[0].Arguments);
        Assert.Equal("0,3010", p.Steps[0].SuccessCodes);
        Assert.True(p.Steps[0].IncludeDirectory);
        Assert.Equal("Write-Output 1", p.Steps[2].Command);
        Assert.Equal("*.ini;*.cfg", p.Steps[3].CopyPatterns);
        Assert.True(p.Steps[3].CopyOverwrite);
        Assert.True(p.Steps[3].CopySubfolders);
        Assert.Equal(20, p.Steps[4].PauseSeconds);
        Assert.Equal(45, p.TimeoutMinutes);
        Assert.Equal(RunAsMode.LocalSystem, p.RunAs);
        Assert.Equal(CopyMode.Pull, p.CopyMode);
        Assert.Equal("1.0", p.Version);
    }

    [Fact]
    public void Pdq_UnavailableStepTypes_AreReported_NotImported()
    {
        var xml = Pdq(PdqPackage("Fin",
            Step("Command", "CommandStep", "<Command>echo ok</Command>") +
            Step("Reboot", "RebootStep", "<Message>m</Message><Timeout value=\"60\" />") +
            Step("Logoff", "PackageStep", "<Message>m</Message>")));

        var report = Import(xml);

        var result = report.Packages.Single();
        Assert.Equal(ImportState.Warning, result.State);
        Assert.Equal([ImportState.Ok, ImportState.Skipped, ImportState.Skipped], result.Steps.Select(s => s.State));
        Assert.Single(_library[result.NewId!.Value].Steps);
    }

    [Fact]
    public void Pdq_PreAndPostSteps_AreChainedInExecutionOrder()
    {
        var steps =
            Step("Command", "CommandStep", "<Command>main</Command>") +
            Step("Command", "CommandStep", "<Command>post</Command>").Replace("<IsPostStep value=\"false\" />", "<IsPostStep value=\"true\" />") +
            Step("Command", "CommandStep", "<Command>pre</Command>").Replace("<IsPreStep value=\"false\" />", "<IsPreStep value=\"true\" />");

        var report = Import(Pdq(PdqPackage("Ordre", steps)));

        Assert.Equal(["pre", "main", "post"], _library[report.Packages[0].NewId!.Value].Steps.Select(s => s.Command));
    }

    [Fact]
    public void Pdq_Conditions_AreConverted()
    {
        const string conditions = """
            <PackageStepCondition><Architecture>Bits64</Architecture><Version>Windows10, Windows11</Version><TypeName>OperatingSystem</TypeName></PackageStepCondition>
            <PackageStepCondition><Version>V4, V5</Version><TypeName>PowerShellVersion</TypeName></PackageStepCondition>
            <PackageStepCondition><IsUserLoggedOn>NoLoggedOnUser</IsUserLoggedOn><TypeName>LoggedOnUser</TypeName></PackageStepCondition>
            <PackageStepCondition><ConditionMode>None</ConditionMode><TypeName>Collection</TypeName></PackageStepCondition>
            """;
        const string stepConditions = """
            <PackageStepCondition><Directory>%ProgramFiles%\App\</Directory><Filename>app.exe</Filename><FileConditionMode>DoesNotExist</FileConditionMode><FileVersionCompare>NoMatch</FileVersionCompare><TypeName>File</TypeName></PackageStepCondition>
            <PackageStepCondition><Hive>HKEY_LOCAL_MACHINE</Hive><Key>SOFTWARE\App</Key><ValueName>Version</ValueName><ConditionMode>Exists</ConditionMode><ValueCompare>NoMatch</ValueCompare><TypeName>Registry</TypeName></PackageStepCondition>
            """;

        var report = Import(Pdq(PdqPackage("Conditions", Step("Command", "CommandStep", "<Command>x</Command>", stepConditions), conditions)));

        Assert.Equal(ImportState.Ok, report.Packages[0].State);
        var p = _library[report.Packages[0].NewId!.Value];
        Assert.Equal(WindowsVersions.Windows10 | WindowsVersions.Windows11, p.Conditions.Windows);
        Assert.Equal(ArchitectureCondition.X64, p.Conditions.Architecture);
        Assert.Equal(4, p.Conditions.MinPowerShell);
        Assert.Equal(LoggedOnCondition.NotLoggedOn, p.Conditions.LoggedOn);
        var c = p.Steps[0].Conditions;
        Assert.Equal(PresenceCondition.NotExists, c.File);
        Assert.Equal(@"%ProgramFiles%\App\app.exe", c.FilePath);
        Assert.Equal(PresenceCondition.Exists, c.Registry);
        Assert.Equal(@"HKLM\SOFTWARE\App", c.RegistryKey);
        Assert.Equal("Version", c.RegistryValue);
        Assert.True(p.Steps[0].Enabled);
    }

    [Fact]
    public void Pdq_InexactStepCondition_DisablesTheStep()
    {
        const string versionCheck = """
            <PackageStepCondition><Directory>C:\App</Directory><Filename>app.exe</Filename><FileConditionMode>Exists</FileConditionMode><FileVersion>2.0</FileVersion><FileVersionCompare>LessThan</FileVersionCompare><TypeName>File</TypeName></PackageStepCondition>
            """;

        var report = Import(Pdq(PdqPackage("Version", Step("Command", "CommandStep", "<Command>x</Command>", versionCheck))));

        Assert.Equal(ImportState.Warning, report.Packages[0].Steps[0].State);
        Assert.False(_library[report.Packages[0].NewId!.Value].Steps[0].Enabled);
    }

    [Fact]
    public void Pdq_InexactPackageCondition_DisablesEveryStep()
    {
        const string servers = """
            <PackageStepCondition><Architecture>All</Architecture><Version>Server2019, Server2022</Version><TypeName>OperatingSystem</TypeName></PackageStepCondition>
            """;

        var report = Import(Pdq(PdqPackage("Serveurs",
            Step("Command", "CommandStep", "<Command>a</Command>") + Step("Command", "CommandStep", "<Command>b</Command>"), servers)));

        var p = _library[report.Packages[0].NewId!.Value];
        Assert.Equal(WindowsVersions.Server, p.Conditions.Windows);
        Assert.All(p.Steps, s => Assert.False(s.Enabled));
    }

    [Fact]
    public void Pdq_Repository_IsReplaced_WhenGiven()
    {
        var xml = Pdq(PdqPackage("Dépôt", Step("Install", "InstallStep", "<FileName>$(Repository)\\App\\setup.exe</FileName><Parameters>/S</Parameters>")));

        var without = Import(xml, apply: false);
        var with = Import(xml, repository: @"\\SERVEUR-01\Depot\");

        Assert.Equal(ImportState.Warning, without.Packages[0].Steps[0].State);
        Assert.Equal(@"\\SERVEUR-01\Depot\App\setup.exe", _library[with.Packages[0].NewId!.Value].Steps[0].File);
        Assert.Equal(ImportState.Ok, with.Packages[0].Steps[0].State);
    }

    [Fact]
    public void Pdq_AdditionalFiles_DisableTheStep()
    {
        var report = Import(Pdq(PdqPackage("Fichiers", Step("Command", "CommandStep", "<Command>x</Command><Files>C:\\a.cfg</Files>"))));

        Assert.False(_library[report.Packages[0].NewId!.Value].Steps[0].Enabled);
        Assert.Equal(ImportState.Warning, report.Packages[0].Steps[0].State);
    }

    [Fact]
    public void Pdq_NestedPackage_IsLinked_InTheFile_OrToAnExistingPackage()
    {
        Add(7, "Commun", Cmd("c"));
        var nestedInFile = Step("NestedPackage", "NestedPackageStep", "<TargetPackageName>Outils</TargetPackageName><TargetPackagePath>Outils</TargetPackagePath>");
        var nestedExisting = Step("NestedPackage", "NestedPackageStep", "<TargetPackageName>Commun</TargetPackageName><TargetPackagePath>Dossier\\Commun</TargetPackagePath>");
        var nestedMissing = Step("NestedPackage", "NestedPackageStep", "<TargetPackageName>Absent</TargetPackageName>");
        var xml = Pdq(PdqPackage("Poste", nestedInFile + nestedExisting + nestedMissing) + PdqPackage("Outils", Step("Command", "CommandStep", "<Command>o</Command>")));

        var report = Import(xml);

        var poste = report.Packages.Single(r => r.Name == "Poste");
        var outils = report.Packages.Single(r => r.Name == "Outils");
        var steps = _library[poste.NewId!.Value].Steps;
        Assert.Equal([outils.NewId, 7], steps.Select(s => s.NestedPackageId));
        Assert.Equal([ImportState.Ok, ImportState.Warning, ImportState.Skipped], poste.Steps.Select(s => s.State));
    }

    [Fact]
    public void Pdq_Folders_AreFlattened_AndOtherObjectsReported()
    {
        var xml = Pdq($"<Folder><Name>Bureautique</Name><Children>{PdqPackage("Éditeur", Step("Command", "CommandStep", "<Command>x</Command>"))}</Children></Folder><Schedule><Name>Nuit</Name></Schedule>");

        var report = Import(xml, apply: false);

        Assert.Equal("Éditeur", report.Packages.Single().Name);
        Assert.Equal(2, report.Notes.Count);
    }

    [Fact]
    public void Pdq_OtherXml_IsRefused()
    {
        Assert.Throws<FormatException>(() => PackageExchange.Read("<AdminArsenal.Export Code=\"PDQInventory\" />"));
        Assert.Throws<FormatException>(() => PackageExchange.Read("<racine />"));
        Assert.Throws<FormatException>(() => PackageExchange.Read("<Package>"));
    }
}
