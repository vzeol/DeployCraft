using System.Globalization;
using DeployCraft.Console;
using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

public sealed class DeploymentPanelTests : IDisposable
{
    private readonly CultureInfo _before = CultureInfo.CurrentUICulture;
    public void Dispose() => CultureInfo.CurrentUICulture = _before;
    private static void Language(string language) => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
    private static DeploymentList List() => new(d => d == null ? "" : "local time", (s, _) => s == null ? "" : "duration");
    private static DeploymentDetails Details() => new(_ => "local time", (_, _) => "duration");

    [Theory]
    [InlineData("fr", "Aucun déploiement", "1 déploiement", "2 déploiements")]
    [InlineData("en", "No deployment", "1 deployment", "2 deployments")]
    public void Refresh_PreservesOrderSelectionAndLocalizedFilters(string language, string empty, string one, string many)
    {
        Language(language);
        var list = List();
        Assert.Equal(empty, list.Rows("", null).Title);
        var first = new Deployment { Id = 12, PackageName = "Tools", Status = DeploymentStatus.Running };
        var second = new Deployment { Id = 7, PackageName = "Other", Status = DeploymentStatus.Successful };
        list.SetDeployments([first, second]);
        Assert.Equal(new long[] { 12, 7 }, list.Rows("", null).Rows.Select(r => r.Id));
        Assert.Equal(many, list.Rows("", null).Title);
        Assert.Equal(12, list.Rows("", null).Selected?.Id);
        Assert.Equal(7, list.Rows("", 7).Selected?.Id);
        foreach (var filter in new[] { "  TOOLs ", "12", "LOCAL TIME", Ui.StatusText("Running") })
        {
            var view = list.Rows(filter, 12);
            Assert.Contains(view.Rows, r => r.Id == 12);
        }
        Assert.Equal(one, list.Rows("Tools", null).Title);
        Assert.Null(list.Rows("Tools", 7).Selected);
        var refreshed = new Deployment { Id = 7, PackageName = "Updated" };
        list.SetDeployments([refreshed]);
        Assert.Same(refreshed, list.Rows("", 7).Selected?.Deployment);
        Assert.Null(list.Rows("", 12).Selected);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    public void PendingSelection_WaitsForArrivalAndFilterThenIsConsumed(string language)
    {
        Language(language);
        var list = List();
        list.Select(9);
        Assert.Null(list.Rows("", null).Selected);
        list.SetDeployments([new() { Id = 1, PackageName = "First" }, new() { Id = 9, PackageName = "Later" }]);
        Assert.Null(list.Rows("First", 1).Selected);
        Assert.Equal(9, list.Rows("", 1).Selected?.Id);
        Assert.Equal(1, list.Rows("", 1).Selected?.Id);
    }

    [Theory]
    [InlineData("fr", "2 sur 4", "2 postes", "1 poste", "0 postes")]
    [InlineData("en", "2 of 4", "2 computers", "1 computer", "0 computers")]
    public void Targets_FilterDetailsAndStatusAndPreserveMultipleSelection(string language, string steps, string many, string one, string empty)
    {
        Language(language);
        var running = new DeploymentTarget
        {
            Id = 1,
            Computer = "PC-TEST-01",
            Status = TargetStatus.Running,
            Stage = "Copying",
            Error = "Hidden",
            StartStep = 2,
            Steps = [new() { State = "success" }, new() { State = "stopped" }, new() { State = "skipped" }]
        };
        var failed = new DeploymentTarget { Id = 2, Computer = "PC-TEST-02", Status = TargetStatus.Failed, Error = "Offline" };
        var d = new Deployment { Id = 8, Snapshot = new() { Steps = Enumerable.Range(1, 5).Select(_ => new PackageStep()).ToList() }, Targets = [running, failed] };
        var list = List();
        var view = list.Targets(d, "", [1, 2]);
        Assert.Equal(many, view.Title);
        Assert.Equal(2, view.Selected.Count);
        Assert.Equal(steps, view.Rows[0].Steps);
        Assert.Equal("Copying", view.Rows[0].Detail);
        Assert.Equal("Offline", view.Rows[1].Detail);
        Assert.Equal(8, view.Rows[0].DeploymentId);
        Assert.Equal(1, Assert.Single(list.Targets(d, "", [99]).Selected).Target.Id);
        foreach (var filter in new[] { " copying ", "pc-test-01", Ui.StatusText("Running") })
            Assert.Equal(1, Assert.Single(list.Targets(d, filter, [2]).Rows).Target.Id);
        Assert.Equal(one, list.Targets(d, "offline", []).Title);
        Assert.Equal(empty, list.Targets(d, "missing", []).Title);
        Assert.Empty(list.Targets(d, "missing", []).Selected);
        running.Status = TargetStatus.Successful;
        Assert.StartsWith("3", list.Targets(d, "PC-TEST-01", []).Rows[0].Steps);
    }

    [Theory]
    [InlineData(DeploymentStatus.Queued, true)]
    [InlineData(DeploymentStatus.Running, true)]
    [InlineData(DeploymentStatus.Successful, false)]
    [InlineData(DeploymentStatus.Failed, false)]
    public void ActionAvailability_UsesDeploymentAndTargetStates(DeploymentStatus status, bool active)
    {
        var d = new Deployment { Status = status };
        Assert.Equal(active, DeploymentActions.CanAbort(d));
        Assert.Equal(!active, DeploymentActions.CanDelete(d));
        Assert.True(DeploymentActions.CanRedeploy(d));
        Assert.False(DeploymentActions.CanRedeployFailed(d));
        d.Targets.Add(new() { Status = TargetStatus.Failed });
        Assert.True(DeploymentActions.CanRedeployFailed(d));
        Assert.False(DeploymentActions.CanAbort(null));
        Assert.False(DeploymentActions.CanDelete(null));
        Assert.False(DeploymentActions.CanRedeploy(null));
        Assert.False(DeploymentActions.CanRedeployFailed(null));
    }

    [Fact]
    public void Redeploy_PreservesComputerOrderAndResumesEachFailedStep()
    {
        var targets = new List<DeploymentTarget>
        {
            new() { Computer = "PC-TEST-01", Status = TargetStatus.Failed, StartStep = 3, Steps = [new() { State = "success" }, new() { State = "failed" }] },
            new() { Computer = "PC-TEST-02", Status = TargetStatus.Failed, StartStep = 2 },
            new() { Computer = "PC-TEST-03", Status = TargetStatus.Running },
            new() { Computer = "PC-TEST-04", Status = TargetStatus.Queued },
        };
        Assert.Equal(new[] { new TargetRequest("PC-TEST-01", 4), new TargetRequest("PC-TEST-02", 2) }, DeploymentActions.Resume(DeploymentActions.Failed(targets)));
        Assert.All(DeploymentActions.Fresh(targets.Select(t => t.Computer)), r => Assert.Equal(1, r.StartStep));
        Assert.Equal(targets.Select(t => t.Computer), DeploymentActions.Fresh(targets.Select(t => t.Computer)).Select(r => r.Computer));
        Assert.Equal(targets.Skip(2), targets.Where(DeploymentActions.IsActive));
    }

    [Fact]
    public void TargetActions_UseOnlySelectedComputersAndKeepCopyOrder()
    {
        var failed = new DeploymentTarget { Computer = "PC-TEST-01", Status = TargetStatus.Failed, Error = "Offline" };
        var queued = new DeploymentTarget { Computer = "PC-TEST-02", Status = TargetStatus.Queued };
        var actions = DeploymentActions.Targets([failed, queued]);
        Assert.Same(queued, Assert.Single(actions.Active));
        Assert.Same(failed, Assert.Single(actions.Failed));
        Assert.Equal("PC-TEST-01" + Environment.NewLine + "PC-TEST-02", actions.Names);
        Assert.Equal("Offline", actions.Errors);
        Assert.True(actions.CanCopyError);
        Assert.False(actions.CanOpenSteps);
        actions = DeploymentActions.Targets([queued]);
        Assert.True(actions.CanOpenSteps);
        Assert.False(actions.CanCopyError);
        Assert.Empty(actions.Errors);
        Assert.Empty(actions.Failed);
        Assert.Equal(new TargetRequest(queued.Computer), Assert.Single(actions.Fresh));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    public async Task Abort_ConfirmsAllOneOrManyAndForwardsOnlyRequestedIds(string language)
    {
        Language(language);
        var d = new Deployment { Id = 8, PackageName = "Tools" };
        var calls = 0;
        List<long>? ids = null;
        var actions = new DeploymentActions((id, selected) =>
        {
            Assert.Equal(8, id);
            calls++;
            ids = selected?.ToList();
            return Task.CompletedTask;
        }, _ => throw new InvalidOperationException());
        foreach (var count in new[] { 0, 1, 2 })
        {
            List<DeploymentTarget>? targets = count == 0 ? null : Enumerable.Range(1, count).Select(i => new DeploymentTarget { Id = i, Computer = $"PC-TEST-0{i}" }).ToList();
            var what = count == 0 ? L.Format("DeploymentsPanel_AbortWhatAll", 8, "Tools") : count == 1
                ? L.Format("DeploymentsPanel_AbortWhatOne", "PC-TEST-01") : L.Format("DeploymentsPanel_AbortWhatMany", 2);
            Assert.False(await actions.AbortAsync(d, targets, _ => false));
            Assert.Equal(count, calls);
            Assert.True(await actions.AbortAsync(d, targets, text => { Assert.Equal(L.Format("DeploymentsPanel_AbortConfirm", what), text); return true; }));
            Assert.Equal(targets?.Select(t => t.Id), ids);
        }
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    public async Task Delete_RequiresInactiveSelectionAndConfirmation(string language)
    {
        Language(language);
        var calls = 0;
        var actions = new DeploymentActions((_, _) => throw new InvalidOperationException(), id => { Assert.Equal(8, id); calls++; return Task.CompletedTask; });
        Assert.False(await actions.DeleteAsync(null, _ => throw new InvalidOperationException()));
        foreach (var status in new[] { DeploymentStatus.Queued, DeploymentStatus.Running })
            Assert.False(await actions.DeleteAsync(new() { Status = status }, _ => throw new InvalidOperationException()));
        var d = new Deployment { Id = 8, PackageName = "Tools", Status = DeploymentStatus.Failed };
        Assert.False(await actions.DeleteAsync(d, _ => false));
        Assert.Equal(0, calls);
        Assert.True(await actions.DeleteAsync(d, text => { Assert.Equal(L.Format("DeploymentsPanel_DeleteConfirm", 8, "Tools"), text); return true; }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ApiFailures_PropagateWithoutReportingSuccess()
    {
        var error = new ApiException("Unavailable");
        var actions = new DeploymentActions((_, _) => Task.FromException(error), _ => Task.FromException(error));
        Assert.Same(error, await Assert.ThrowsAsync<ApiException>(() => actions.AbortAsync(new(), null, _ => true)));
        Assert.Same(error, await Assert.ThrowsAsync<ApiException>(() => actions.DeleteAsync(new() { Status = DeploymentStatus.Successful }, _ => true)));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    public void DeploymentCard_PreservesStatusCountsRunAsAndTiming(string language)
    {
        Language(language);
        var d = new Deployment
        {
            Id = 8,
            PackageName = "Tools",
            StartStep = 2,
            Status = DeploymentStatus.Running,
            RunAsMode = RunAsMode.LocalSystem,
            CredentialId = 1,
            RunAs = "admin",
            CopyMode = CopyMode.Pull,
            Targets = [new() { Status = TargetStatus.Failed }, new() { Status = TargetStatus.Running }, new() { Status = TargetStatus.Queued }]
        };
        var card = Details().Deployment(d);
        Assert.Equal(L.Format("DeploymentsPanel_DeploymentTitle", 8), card.Title);
        Assert.Equal(L.Format("DeploymentsPanel_FromStep", "Tools", 2), card.Package);
        Assert.Equal(Ui.StatusText("Running"), card.StatusText);
        Assert.Equal("local time", card.Created);
        Assert.Equal("duration", card.Duration);
        Assert.Equal("—", card.LaunchedBy);
        Assert.Equal(L.Get("Ui_RunAsLocalSystem") + " " + L.Format("DeploymentsPanel_RunAsAccess", "admin") + " · pull", card.RunAs);
        Assert.Equal("3 ", card.Total);
        Assert.Equal("1 ", card.Failed);
        Assert.Equal(L.Format("DeploymentsPanel_RunningWaiting", 1, 1), card.Counts);
        Assert.True(card.CanRetry);
        Assert.True(card.Running);
        d.Status = DeploymentStatus.Successful;
        d.StartStep = 1;
        d.Targets.Clear();
        d.RunAsMode = RunAsMode.DeployUser;
        d.RunAs = null;
        d.CopyMode = CopyMode.Push;
        card = Details().Deployment(d);
        Assert.Equal("Tools", card.Package);
        Assert.Equal("—", card.RunAs);
        Assert.Empty(card.Counts);
        Assert.False(card.Running);
        Assert.False(card.CanRetry);
        d.RunAsMode = RunAsMode.LoggedOnUser;
        d.CredentialId = null;
        Assert.Equal(L.Get("Ui_RunAsLoggedOn"), Details().Deployment(d).RunAs);
    }

    [Theory]
    [InlineData("fr", TargetStatus.Failed, "ERREUR")]
    [InlineData("en", TargetStatus.Failed, "ERROR")]
    [InlineData("fr", TargetStatus.Successful, "REMARQUE")]
    [InlineData("en", TargetStatus.Successful, "NOTE")]
    [InlineData("fr", TargetStatus.Running, "REMARQUE")]
    [InlineData("en", TargetStatus.Running, "NOTE")]
    public void TargetCard_PreservesErrorNoteStageAndMissingTimes(string language, TargetStatus status, string title)
    {
        Language(language);
        var target = new DeploymentTarget { Computer = "PC-TEST-01", Status = status, Stage = "Copying", Error = "Detail" };
        var row = new TargetRow(target, 8, target.Computer, DeploymentSummary.TargetStatusOf(target), "1/2", "", "", "");
        var card = Details().Target(row);
        Assert.Equal(target.Computer, card.Name);
        Assert.Equal(title, card.ErrorTitle);
        Assert.Equal("Detail", card.Error);
        Assert.True(card.HasError);
        Assert.Equal(status == TargetStatus.Failed, card.Failed);
        Assert.Equal(status == TargetStatus.Running, card.Running);
        Assert.Equal(status == TargetStatus.Running ? "Copying" : "", card.Current);
        Assert.Equal("—", card.Started);
        Assert.Equal("—", card.Ended);
        Assert.Equal("—", card.Duration);
        Assert.Equal("1/2 " + L.Get("DeploymentsPanel_StepsWord"), card.Steps);
        target.Error = null;
        target.EndedUtc = DateTime.UtcNow;
        card = Details().Target(row with { Started = "start", Duration = "elapsed" });
        Assert.False(card.HasError);
        Assert.Equal("start", card.Started);
        Assert.Equal("local time", card.Ended);
        Assert.Equal("elapsed", card.Duration);
    }
}
