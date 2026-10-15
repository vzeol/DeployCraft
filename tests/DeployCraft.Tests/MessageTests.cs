using DeployCraft.Runner;
using DeployCraft.Server.Data;
using DeployCraft.Server.Remote;

namespace DeployCraft.Tests;

/// <summary>Étape « message » : consignes du runner et résultat selon les réponses des sessions.</summary>
public class MessageTests
{
    private const int Ok = 1;

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

    [Theory]
    [InlineData(new int[0], true, true, true, "Aucun utilisateur")]
    [InlineData(new[] { MessageOutcome.Timeout }, false, false, true, "affiché dans 1")]
    [InlineData(new[] { Ok, Ok }, true, true, true, "OK cliqué dans 2")]
    [InlineData(new[] { Ok, MessageOutcome.Timeout }, true, false, true, "fin de la durée d'affichage")]
    [InlineData(new[] { MessageOutcome.Timeout }, true, true, false, "Personne n'a cliqué")]
    [InlineData(new[] { Ok, MessageOutcome.Error }, false, false, false, "impossible à afficher dans 1 session(s) sur 2")]
    public void Outcome_DependsOnTheResponses(int[] responses, bool wait, bool untilClick, bool success, string text)
    {
        Assert.Equal(success, MessageOutcome.Succeeded(responses, wait, untilClick, out var message));
        Assert.Contains(text, message);
    }
}
