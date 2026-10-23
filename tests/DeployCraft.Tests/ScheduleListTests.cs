using System.Globalization;
using DeployCraft.Console;
using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

public sealed class ScheduleListTests : IDisposable
{
    private readonly CultureInfo _before = CultureInfo.CurrentUICulture;
    public void Dispose() => CultureInfo.CurrentUICulture = _before;

    private sealed class Service
    {
        public List<Schedule> Schedules { get; set; } = [];
        public Schedule Full { get; set; } = new();
        public List<Schedule> Saved { get; } = [];
        public List<long> Deleted { get; } = [];
        public ScheduleRunResult Run { get; set; } = new([], null);
        public ScheduleList List() => new(() => Task.FromResult(Schedules), _ => Task.FromResult(Full),
            s => { Saved.Add(s); return Task.FromResult(s); },
            id => { Deleted.Add(id); return Task.CompletedTask; }, _ => Task.FromResult(Run));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    public async Task Refresh_PreservesSelectionAndExplicitSelection_WithoutSelectingFirstRow(string language)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var service = new Service { Schedules = [new() { Id = 1, Name = "Tools", Enabled = true }, new() { Id = 2, Name = "Daily", Enabled = false }] };
        var list = service.List();
        var all = await list.RefreshAsync(() => null);
        Assert.Null(all.Selected);
        Assert.False(all.CanRun);
        Assert.Equal(L.Format("MainWindow_SchedulesSubtitleAll", 2, 1), all.Subtitle);
        var selected = await list.RefreshAsync(() => 1);
        Assert.Equal(1, selected.Selected?.Id);
        Assert.True(selected.CanRun);
        Assert.Equal(L.Format("MainWindow_SchedulesSubtitleOne", 2, 1, "Tools"), selected.Subtitle);
        Assert.Equal(2, (await list.RefreshAsync(() => 1, 2)).Selected?.Id);
        service.Schedules.RemoveAt(0);
        Assert.Null((await list.RefreshAsync(() => 1)).Selected);
        service.Schedules.Clear();
        Assert.Equal(L.Get("MainWindow_SchedulesEmpty"), (await list.RefreshAsync(() => 1)).Subtitle);
    }

    [Fact]
    public async Task Refresh_ReadsSelectionAfterLoading_AndPropagatesFailure()
    {
        var pending = new TaskCompletionSource<List<Schedule>>();
        long? selected = null;
        var list = new ScheduleList(() => pending.Task, _ => throw new InvalidOperationException(),
            _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException());
        var refresh = list.RefreshAsync(() => selected);
        selected = 7;
        pending.SetResult([new() { Id = 7 }]);
        Assert.Equal(7, (await refresh).Selected?.Id);
        pending = new();
        var error = new ApiException("Unavailable");
        pending.SetException(error);
        Assert.Same(error, await Assert.ThrowsAsync<ApiException>(() => list.RefreshAsync(() => selected)));
    }

    [Fact]
    public void NewAndDeploymentFilter_PreservePackageAndScheduleScope()
    {
        Assert.Empty(ScheduleList.New(null).PackageIds);
        var schedule = ScheduleList.New(new Package { Id = 5, Name = "Tools" });
        Assert.Equal([5L], schedule.PackageIds);
        Assert.Equal("Tools", schedule.Name);
        List<Deployment> deployments = [new() { Id = 1 }, new() { Id = 2, ScheduleId = 7 }, new() { Id = 3, ScheduleId = 8 }];
        Assert.Equal([2L, 3L], ScheduleList.Deployments(deployments, null).Select(d => d.Id));
        Assert.Equal([2L], ScheduleList.Deployments(deployments, 7).Select(d => d.Id));
    }

    [Fact]
    public async Task Attach_FiltersCandidatesAndSavesOnlyANewAttachment()
    {
        var service = new Service { Schedules = [new() { Id = 1, PackageIds = [5] }, new() { Id = 2, PackageIds = [6] }], Full = new() { Id = 2, PackageIds = [6] } };
        var list = service.List();
        Assert.Equal(2, Assert.Single(await list.CandidatesAsync(5)).Id);
        await list.AttachAsync(2, 5);
        await list.AttachAsync(2, 5);
        Assert.Single(service.Saved);
        Assert.Equal([6L, 5L], service.Full.PackageIds);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    public async Task Detach_ProtectsLastPackageAndHonorsConfirmation(string language)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var package = new Package { Id = 5, Name = "Tools" };
        var service = new Service { Full = new() { Id = 2, Name = "Daily", PackageIds = [5] } };
        var list = service.List();
        string? information = null;
        Assert.False(await list.DetachAsync(2, package, text => information = text, _ => throw new InvalidOperationException()));
        Assert.Equal(L.Format("MainWindow_OnlyPackageInSchedule", "Tools", "Daily"), information);
        service.Full.PackageIds.Add(6);
        Assert.False(await list.DetachAsync(2, package, _ => throw new InvalidOperationException(), text =>
        {
            Assert.Equal(L.Format("MainWindow_DetachConfirm", "Tools", "Daily"), text);
            return false;
        }));
        Assert.Empty(service.Saved);
        Assert.True(await list.DetachAsync(2, package, _ => throw new InvalidOperationException(), _ => true));
        Assert.Equal([6L], service.Full.PackageIds);
        Assert.Single(service.Saved);
        Assert.True(await list.DetachAsync(2, package, _ => throw new InvalidOperationException(), _ => true));
        Assert.Single(service.Saved);
    }

    [Theory]
    [InlineData("fr", "Tools (copie)", "Rien à déployer.")]
    [InlineData("en", "Tools (copy)", "Nothing to deploy.")]
    public async Task Actions_UseFullScheduleAndPreserveDuplicationAndRunDecisions(string language, string copyName, string emptyRun)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var service = new Service { Full = new() { Id = 9, Name = "Tools", Enabled = true, PackageIds = [5, 6] } };
        var list = service.List();
        Assert.Same(service.Full, await list.GetAsync(9));
        await list.ToggleAsync(9);
        Assert.False(service.Full.Enabled);
        await list.ToggleAsync(9);
        Assert.True(service.Full.Enabled);
        var copy = await list.DuplicateAsync(9);
        Assert.Equal(0, copy.Id);
        Assert.Equal(copyName, copy.Name);
        Assert.False(copy.Enabled);
        Assert.Equal([5L, 6L], copy.PackageIds);
        Assert.Equal(emptyRun, await list.RunAsync(9));
        service.Run = new([], "No targets");
        Assert.Equal("No targets", await list.RunAsync(9));
        service.Run = new([new()], "Started");
        Assert.Null(await list.RunAsync(9));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    public async Task Delete_OnlyCallsServiceAfterConfirmation(string language)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var service = new Service();
        var list = service.List();
        var schedule = new Schedule { Id = 7, Name = "Daily" };
        Assert.False(await list.DeleteAsync(schedule, text =>
        {
            Assert.Equal(L.Format("MainWindow_DeleteScheduleConfirm", "Daily"), text);
            return false;
        }));
        Assert.Empty(service.Deleted);
        Assert.True(await list.DeleteAsync(schedule, _ => true));
        Assert.Equal([7L], service.Deleted);
    }

    [Fact]
    public async Task ActionFailure_PropagatesAndDoesNotSaveAnUnloadedSchedule()
    {
        var error = new ApiException("Unavailable");
        var requested = new List<long>();
        var list = new ScheduleList(() => Task.FromResult(new List<Schedule>()), id =>
        {
            requested.Add(id);
            return Task.FromException<Schedule>(error);
        }, _ => throw new InvalidOperationException("Must not save."),
            _ => Task.FromException(error), _ => Task.FromException<ScheduleRunResult>(error));

        Assert.Same(error, await Assert.ThrowsAsync<ApiException>(() => list.ToggleAsync(7)));
        Assert.Same(error, await Assert.ThrowsAsync<ApiException>(() => list.DuplicateAsync(8)));
        Assert.Same(error, await Assert.ThrowsAsync<ApiException>(() => list.AttachAsync(9, 5)));
        Assert.Same(error, await Assert.ThrowsAsync<ApiException>(() => list.RunAsync(10)));
        Assert.Same(error, await Assert.ThrowsAsync<ApiException>(() => list.DeleteAsync(new() { Id = 11 }, _ => true)));
        Assert.Equal([7L, 8L, 9L], requested);
    }
}
