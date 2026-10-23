using System.Globalization;
using DeployCraft.Console;
using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

public sealed class MainWindowUpdatesTests : IDisposable
{
    private readonly CultureInfo _before = CultureInfo.CurrentUICulture;
    private static readonly UpdateInfo Release = new("2.0.0", "https://example.test/release", "https://example.test/setup.exe", "digest");

    public void Dispose() => CultureInfo.CurrentUICulture = _before;

    private static MainWindowUpdates Updates(Func<Task<UpdateInfo?>>? find = null,
        Func<Task<List<Deployment>>>? deployments = null, Func<string?>? server = null) =>
        new(find ?? (() => Task.FromResult<UpdateInfo?>(Release)), deployments ?? (() => Task.FromResult(new List<Deployment>())),
            server ?? (() => "SERVEUR-01"), "serveur-01", "1.0.0-rc.1");

    [Theory]
    [InlineData("SERVEUR-01", true, true)]
    [InlineData("serveur-01", true, true)]
    [InlineData("PC-TEST-01", true, false)]
    [InlineData(null, true, false)]
    [InlineData("SERVEUR-01", false, false)]
    public async Task InstallEligibility_RequiresLocalServerAndInstaller(string? server, bool installer, bool expected)
    {
        var release = installer ? Release : Release with { SetupUrl = null };
        var calls = 0;
        var updates = Updates(server: () => server, deployments: () => { calls++; return Task.FromResult(new List<Deployment>()); });

        Assert.Equal(expected, updates.CanInstallHere(release));
        Assert.Equal(expected, await updates.ProposalAsync(release) != null);
        Assert.Equal(expected ? 1 : 0, calls);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    public async Task CheckHideAndRecheck_PreserveReleaseSelectionAndLocalizedBanner(string language)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        UpdateInfo? next = Release;
        string? server = "PC-TEST-01";
        var updates = Updates(find: () => Task.FromResult<UpdateInfo?>(next), server: () => server);
        Assert.Null(updates.Update);
        Assert.False(updates.Visible);
        Assert.False(updates.ShowInstall);

        Assert.Same(Release, await updates.CheckAsync());
        Assert.True(updates.Visible);
        Assert.Equal(L.Format("MainWindow_UpdateAvailable", "2.0.0", "1.0.0-rc.1") + " " + L.Get("MainWindow_UpdateInstallOnServer"), updates.AvailableText);
        Assert.False(updates.ShowInstall);
        server = "SERVEUR-01";
        Assert.Equal(L.Format("MainWindow_UpdateAvailable", "2.0.0", "1.0.0-rc.1"), updates.AvailableText);
        Assert.True(updates.ShowInstall);

        updates.Hide();
        Assert.False(updates.Visible);
        Assert.Same(Release, updates.Update);
        await updates.CheckAsync();
        Assert.True(updates.Visible);
        next = null;
        Assert.Null(await updates.CheckAsync());
        Assert.False(updates.Visible);
        Assert.False(updates.ShowInstall);
        Assert.Empty(updates.AvailableText);
        Assert.Equal(L.Format("MainWindow_UpdateNoneFound", "1.0.0-rc.1"), updates.NoneFoundText);
    }

    [Theory]
    [InlineData("fr", false)]
    [InlineData("fr", true)]
    [InlineData("en", false)]
    [InlineData("en", true)]
    public async Task Proposal_WarnsOnlyForRunningAndQueuedDeployments(string language, bool active)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        List<Deployment> list = [new() { Status = DeploymentStatus.Successful }, new() { Status = DeploymentStatus.Failed }];
        if (active) list.AddRange([new() { Status = DeploymentStatus.Running }, new() { Status = DeploymentStatus.Queued }]);
        var updates = Updates(deployments: () => Task.FromResult(list));

        var text = await updates.ProposalAsync(Release);

        Assert.Equal(L.Format("MainWindow_UpdateProposal", "2.0.0", "1.0.0-rc.1") +
            (active ? "\n\n" + L.Format("MainWindow_UpdateRunningWarning", 2) : ""), text);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    public async Task DeploymentApiFailure_StillOffersUpdateWithoutWarning(string language)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var updates = Updates(deployments: () => Task.FromException<List<Deployment>>(new ApiException("Unavailable")));

        Assert.Equal(L.Format("MainWindow_UpdateProposal", "2.0.0", "1.0.0-rc.1"), await updates.ProposalAsync(Release));
    }

    [Fact]
    public async Task Installing_SuppressesProposalsAndFailureRestoresOriginalReleaseAfterAnotherCheck()
    {
        var calls = 0;
        var updates = Updates(find: () => Task.FromResult<UpdateInfo?>(null),
            deployments: () => { calls++; return Task.FromResult(new List<Deployment>()); });
        updates.BeginInstall(Release);
        Assert.True(updates.Installing);
        Assert.True(updates.Visible);
        Assert.False(updates.ShowInstall);
        Assert.Null(await updates.ProposalAsync(Release));
        Assert.Equal(0, calls);
        updates.Hide();
        await updates.CheckAsync();

        updates.InstallFailed(Release);

        Assert.False(updates.Installing);
        Assert.True(updates.Visible);
        Assert.True(updates.ShowInstall);
        Assert.Same(Release, updates.Update);
        Assert.NotNull(await updates.ProposalAsync(Release));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("fr", "Téléchargement de DeployCraft 2.0.0… 42 %", "Téléchargement de la nouvelle version impossible : Unavailable")]
    [InlineData("en", "Downloading DeployCraft 2.0.0… 42%", "Could not download the new version: Unavailable")]
    public void DownloadTexts_ReuseExistingResources(string language, string progress, string error)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        Assert.Equal(progress, MainWindowUpdates.DownloadText(Release, 42));
        Assert.Equal(error, MainWindowUpdates.DownloadFailedText(new ApiException("Unavailable")));
    }

    [Fact]
    public async Task UnexpectedFailures_AreNotHidden()
    {
        var error = new InvalidOperationException("Unexpected failure");
        var updates = Updates(deployments: () => Task.FromException<List<Deployment>>(error));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => updates.ProposalAsync(Release)));
        updates = Updates(find: () => Task.FromException<UpdateInfo?>(error));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(updates.CheckAsync));
    }
}
