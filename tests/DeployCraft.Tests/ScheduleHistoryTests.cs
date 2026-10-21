using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

/// <summary>History of the targets of a schedule, on a throwaway SQLite database.</summary>
public sealed class ScheduleHistoryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("dc-tests-").FullName;
    private readonly Store _store;

    public ScheduleHistoryTests() => _store = new Store(Path.Combine(_dir, "test.db"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Failures_AddUp_AndSuccessIsFinal()
    {
        _store.Schedules.RecordResult(1, "PC01", 7, success: false);
        _store.Schedules.RecordResult(1, "PC01", 7, success: false);
        _store.Schedules.RecordResult(1, "PC01", 7, success: true);
        _store.Schedules.RecordResult(1, "PC01", 7, success: false);

        var h = Assert.Single(_store.Schedules.GetHistory(1));
        Assert.True(h.Succeeded);
        Assert.Equal(3, h.Failures);
    }

    [Fact]
    public void ComputerNames_IgnoreCase()
    {
        _store.Schedules.RecordResult(1, "PC01", 7, success: false);
        _store.Schedules.RecordResult(1, "pc01", 7, success: false);

        Assert.Equal(2, Assert.Single(_store.Schedules.GetHistory(1)).Failures);
    }

    [Fact]
    public void Clear_RemovesOnlyTheGivenLines()
    {
        _store.Schedules.RecordResult(1, "PC01", 7, success: true);
        _store.Schedules.RecordResult(1, "PC02", 7, success: true);
        _store.Schedules.RecordResult(2, "PC01", 7, success: true);

        _store.Schedules.ClearHistory(1, [new ScheduleHistoryKey("PC01", 7)]);

        Assert.Equal("PC02", Assert.Single(_store.Schedules.GetHistory(1)).Computer);
        Assert.Single(_store.Schedules.GetHistory(2));
    }
}
