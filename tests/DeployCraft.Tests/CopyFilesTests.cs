using DeployCraft.Runner;
using DeployCraft.Server.Data;
using DeployCraft.Server.Remote;

namespace DeployCraft.Tests;

/// <summary>"File copy" step: patterns, runner instructions, robocopy line and files dropped in push.</summary>
public sealed class CopyFilesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("deploycraft-copie-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("*.ini; *.cfg,*.INI", new[] { "*.ini", "*.cfg" })]
    [InlineData(" ; ", new[] { "*" })]
    [InlineData(null, new[] { "*" })]
    public void SplitPatterns_TrimsAndDropsDuplicates(string? text, string[] expected) =>
        Assert.Equal(expected, PackageStep.SplitPatterns(text));

    [Fact]
    public void JobStep_CarriesTheCopySettings_AndRobocopySuccessCodes()
    {
        var step = new PackageStep
        {
            Type = StepType.CopyFiles,
            CopySource = @"\\SERVEUR-01\Logiciels\Outil",
            CopyDestination = @"%ProgramData%\Outil",
            CopyPatterns = "*.ini;*.cfg",
            CopySubfolders = false,
            CopyOverwrite = false,
            SuccessCodes = "0",
        };

        var job = TargetSession.ToJobStep(step, new Package(), RunAsMode.LocalSystem);

        Assert.Equal("copyfiles", job.Type);
        Assert.Equal(@"%ProgramData%\Outil", job.CopyDestination);
        Assert.Equal(["*.ini", "*.cfg"], job.CopyPatterns);
        Assert.False(job.CopySubfolders);
        Assert.False(job.CopyOverwrite);
        // Robocopy codes, whatever success codes were entered: 8 and above = files not copied.
        Assert.Equal(Enumerable.Range(0, 8), job.SuccessCodes);

        step.CopyIgnoreErrors = true;
        Assert.Equal(Enumerable.Range(0, 16), TargetSession.ToJobStep(step, new Package(), RunAsMode.LocalSystem).SuccessCodes);

        Assert.Null(TargetSession.ToJobStep(new PackageStep { Type = StepType.Command, Command = "echo" }, new Package(), RunAsMode.LocalSystem).CopyDestination);
    }

    [Fact]
    public void RobocopyArguments_FollowTheOptions()
    {
        Assert.Equal("\"C:\\Windows\\DeployCraft\\slot-1\\exec\\copie-2\" \"%ProgramData%\\Outil\" \"*\" /E /R:1 /W:1 /NP",
            FileCopy.Arguments(@"C:\Windows\DeployCraft\slot-1\exec\copie-2", @"%ProgramData%\Outil\", ["*"], subfolders: true, overwrite: true));

        // Without overwriting: files present on the computer are kept; drive root: "C:\." (a trailing "\"" would be misread).
        Assert.Equal("\"\\\\SERVEUR-01\\Logiciels\" \"C:\\.\" \"*.ini\" \"*.cfg\" /XC /XN /XO /R:1 /W:1 /NP",
            FileCopy.Arguments(@"\\SERVEUR-01\Logiciels", @"C:\", ["*.ini", "*.cfg"], subfolders: false, overwrite: false));
    }

    [Fact]
    public void PullArguments_FileSource_CopiesOnlyThatFile_WithoutSubfolders()
    {
        // File source: its folder and its name, never /E (otherwise the folder tree would be recreated on the computer).
        Assert.Equal("\"\\\\SERVEUR-01\\Logiciels\\Outil\" \"C:\\Outil\" \"outil.ini\" /R:1 /W:1 /NP",
            FileCopy.PullArguments(@"\\SERVEUR-01\Logiciels\Outil\outil.ini", isFile: true, @"C:\Outil", ["*.txt"], subfolders: true, overwrite: true));

        Assert.Equal(FileCopy.Arguments(@"\\SERVEUR-01\Logiciels\Outil", @"C:\Outil", ["*.ini"], true, false),
            FileCopy.PullArguments(@"\\SERVEUR-01\Logiciels\Outil", isFile: false, @"C:\Outil", ["*.ini"], subfolders: true, overwrite: false));
    }

    [Fact]
    public void Stage_File_CopiesOnlyThatFile()
    {
        var source = Write("source", "outil.ini");
        Write("source", "autre.txt");
        var target = Path.Combine(_dir, "exec", "copie-1");

        TargetSession.StageCopy(source, "*.txt", subfolders: true, target);

        Assert.Equal(["outil.ini"], Files(target));
    }

    [Fact]
    public void Stage_Folder_KeepsMatchingFiles_WithOrWithoutSubfolders()
    {
        var source = Path.Combine(_dir, "source");
        Write("source", "a.ini");
        Write("source", "b.txt");
        Write("source", Path.Combine("conf", "c.ini"));
        Directory.CreateDirectory(Path.Combine(source, "vide"));

        var flat = Path.Combine(_dir, "plat");
        TargetSession.StageCopy(source, "*.ini", subfolders: false, flat);
        Assert.Equal(["a.ini"], Files(flat));

        var deep = Path.Combine(_dir, "complet");
        TargetSession.StageCopy(source, "*.ini;*.txt", subfolders: true, deep);
        Assert.Equal(["a.ini", "b.txt", Path.Combine("conf", "c.ini")], Files(deep));
        Assert.True(Directory.Exists(Path.Combine(deep, "vide")));
    }

    [Fact]
    public void Stage_MissingSource_IsAClearError()
    {
        var ex = Assert.Throws<DeployException>(() => TargetSession.StageCopy(Path.Combine(_dir, "absent"), "*", true, Path.Combine(_dir, "cible")));
        Assert.Contains("introuvable", ex.Message);
    }

    private string Write(string folder, string relative)
    {
        var path = Path.Combine(_dir, folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, relative);
        return path;
    }

    private static List<string> Files(string root) =>
        [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(root, f)).Order(StringComparer.Ordinal)];
}
