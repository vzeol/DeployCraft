using System.Diagnostics;
using System.Text;
using DeployCraft.Runner;
using DeployCraft.Runner.Steps;

namespace DeployCraft.Tests;

/// <summary>Real local Windows PowerShell 5.1 processes; no service or remote computer.</summary>
public sealed class PowerShellOutputTests : IDisposable, IStepHost
{
    static PowerShellOutputTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    private static Encoding Oem => Encoding.GetEncoding(850);
    private readonly string _exec = Directory.CreateTempSubdirectory("dc-output-tests-").FullName;
    string IStepHost.ExecDir => _exec;
    void IStepHost.Pull(JobStep step) => throw new NotSupportedException();
    void IStepHost.Log(string message) { }
    public void Dispose() => Directory.Delete(_exec, recursive: true);

    [Fact]
    public async Task PowerShell51_PreservesUnicodeOnBothOutputStreams()
    {
        var result = await Run(new JobStep
        {
            Type = "powershell",
            Command = """
                Write-Output $PSVersionTable.PSVersion.ToString()
                Write-Output 'œ — é 😀'
                Write-Host 'host: œ — é 😀'
                [Console]::Error.WriteLine('error: œ — é 😀')
                """,
        }, Oem);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("5.1.", result.Output);
        Assert.Contains("œ — é 😀", result.Output);
        Assert.Contains("host: œ — é 😀", result.Output);
        Assert.Contains("error: œ — é 😀", result.Error);
    }

    [Fact]
    public async Task PowerShell_KeepsParamUsingDirectivesLiteralArgumentsAndExitCode()
    {
        var result = await Run(new JobStep
        {
            Type = "powershell",
            Command = """
                #requires -Version 5.1
                using namespace System.Text
                [CmdletBinding()]
                param([string]$Text, [int]$Code)
                Write-Output $Text
                Write-Output ([Encoding]::UTF8.WebName)
                Write-Output (Split-Path -Leaf $PSCommandPath)
                exit $Code
                """,
            Arguments = "-Text \"œ — é 😀 $literal; text\" -Code 7",
        }, Oem);

        Assert.Equal(7, result.ExitCode);
        Assert.Contains("œ — é 😀 $literal; text", result.Output);
        Assert.Contains("utf-8", result.Output);
        Assert.Contains("step1.ps1", result.Output);
        Assert.Equal("", result.Error);
    }

    [Fact]
    public async Task PowerShell_TerminatingErrorStillFailsAndKeepsUnicode()
    {
        var result = await Run(new JobStep { Type = "powershell", Command = "throw 'œ — é 😀'" }, Oem);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("œ — é 😀", result.Error);
    }

    [Fact]
    public async Task PowerShell_CanStillCaptureANativeCmdCommand()
    {
        var result = await Run(new JobStep
        {
            Type = "powershell",
            Command = "& $env:ComSpec /c 'echo Opération réussie'",
        }, Oem);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Opération réussie", result.Output);
    }

    [Fact]
    public async Task Command_KeepsItsExistingOemOutputAndExitCode()
    {
        // chcp makes the native regression independent of the test machine's language.
        var result = await Run(new JobStep
        {
            Type = "command",
            Command = "chcp 850 >nul & echo Opération réussie & echo erreur 1>&2 & exit /b 7",
        }, Encoding.GetEncoding(850));

        Assert.Equal(7, result.ExitCode);
        Assert.Contains("Opération réussie", result.Output);
        Assert.Contains("erreur", result.Error);
    }

    private async Task<(int ExitCode, string Output, string Error)> Run(JobStep step, Encoding encoding)
    {
        var psi = StepLaunchers.For(step.Type).Build(this, 0, step);
        psi.WorkingDirectory = _exec;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardInput = psi.RedirectStandardOutput = psi.RedirectStandardError = true;
        using var process = new Process { StartInfo = psi };
        Assert.True(process.Start());
        process.StandardInput.Close();
        using var stdout = new MemoryStream();
        using var stderr = new MemoryStream();
        var readOutput = process.StandardOutput.BaseStream.CopyToAsync(stdout);
        var readError = process.StandardError.BaseStream.CopyToAsync(stderr);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(readOutput, readError).WaitAsync(timeout.Token);
            var outputEncoding = ProcessOutput.EncodingFor(step.Type, encoding);
            return (process.ExitCode, ProcessOutput.Decode(stdout.ToArray(), outputEncoding), ProcessOutput.Decode(stderr.ToArray(), outputEncoding));
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
