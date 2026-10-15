using DeployCraft.Runner;
using DeployCraft.Server.Data;
using DeployCraft.Server.Remote;

namespace DeployCraft.Tests;

/// <summary>Étape « message » : consignes du runner et résultat selon les réponses des sessions.</summary>
public class MessageTests
{
    [Fact]
    public void JobStep_CarriesTitleTextDurationAndWait_AndNeverRunsAsTheLoggedOnUser()
    {
        var step = new PackageStep { Type = StepType.Message, MessageTitle = "Maintenance", MessageText = "Fermez vos applications.", MessageSeconds = 30, MessageWait = true };

        var job = TargetSession.ToJobStep(step, new Package(), RunAsMode.LoggedOnUser);

        Assert.Equal("message", job.Type);
        Assert.Equal("Maintenance", job.Arguments);
        Assert.Equal("Fermez vos applications.", job.Command);
        Assert.Equal(30, job.Seconds);
        Assert.True(job.WaitForUser);
        Assert.False(job.RunAsLoggedOnUser);
    }

    [Fact]
    public void JobStep_WaitLongerThanTheStepTimeout_KeepsTheServerWaiting()
    {
        var step = new PackageStep { Type = StepType.Message, MessageText = "x", MessageSeconds = 7200, MessageWait = true, TimeoutMinutes = 60 };

        Assert.True(TargetSession.ToJobStep(step, new Package(), RunAsMode.LocalSystem).TimeoutSeconds > 7200);
        step.MessageWait = false;
        Assert.Equal(3600, TargetSession.ToJobStep(step, new Package(), RunAsMode.LocalSystem).TimeoutSeconds);
    }

    [Fact]
    public void Effective_AMessageAlwaysRunsInASystemTranche()
    {
        var step = new PackageStep { Type = StepType.Message, RunAs = RunAsMode.DeployUser };
        Assert.Equal(RunAsMode.LocalSystem, RunAsPlan.Effective(step, RunAsMode.DeployUser, hasAccount: true));
    }

    [Fact]
    public void Script_IsEncoded_EscapesQuotes_AndClosesWithTheTimeoutCode()
    {
        var args = MessageOutcome.Arguments("L'équipe", "Fermez l'application.\r\nMerci", 20);
        var script = System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(args.Split(' ')[^1]));

        Assert.Equal(MessageOutcome.Script("L'équipe", "Fermez l'application.\r\nMerci", 20), script);
        Assert.Contains("Text = 'L''équipe'", script);
        Assert.Contains("Text = 'Fermez l''application.\r\nMerci'", script);
        Assert.Contains("Interval = 20000", script);
        Assert.Contains("$script:code = 2", script);
        // Sans durée : pas de minuterie, la fenêtre reste jusqu'au clic.
        Assert.DoesNotContain("Timer", MessageOutcome.Script(null!, "x", 0));
    }

    [Theory]
    [InlineData(0, null, true, true, "Aucun utilisateur")]
    [InlineData(1, null, false, true, "affiché dans 1")]
    [InlineData(2, new[] { MessageOutcome.Clicked, MessageOutcome.Clicked }, true, true, "OK cliqué dans 2")]
    [InlineData(2, new[] { MessageOutcome.Clicked, MessageOutcome.Timeout }, false, true, "fin de la durée d'affichage")]
    [InlineData(1, new[] { MessageOutcome.Timeout }, true, false, "Personne n'a cliqué")]
    [InlineData(2, new[] { MessageOutcome.Clicked, 1 }, false, false, "en erreur dans 1 session(s) sur 2")]
    public void Outcome_DependsOnTheExitCodes(int sessions, int[]? codes, bool untilClick, bool success, string text)
    {
        Assert.Equal(success, MessageOutcome.Succeeded(sessions, codes, untilClick, out var message));
        Assert.Contains(text, message);
    }
}
