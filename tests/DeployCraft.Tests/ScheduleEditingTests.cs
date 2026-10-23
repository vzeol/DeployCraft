using DeployCraft.Console;
using DeployCraft.Server.Data;

namespace DeployCraft.Tests;

/// <summary>Rules of the schedule window that do not need the window.</summary>
public class ScheduleEditingTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 14, 37, 12);

    [Fact]
    public void NewTrigger_StartsAtTheNextFullHour()
    {
        var daily = ScheduleEditing.NewTrigger(TriggerType.Daily, Now);
        var interval = ScheduleEditing.NewTrigger(TriggerType.Interval, Now);
        var weekly = ScheduleEditing.NewTrigger(TriggerType.Weekly, Now);
        var monthly = ScheduleEditing.NewTrigger(TriggerType.Monthly, Now);

        Assert.Equal(new DateTime(2026, 10, 21, 15, 0, 0), daily.Start);
        Assert.Equal(new DateTime(2026, 10, 21, 14, 37, 0), interval.Start);
        Assert.Equal(60, interval.IntervalMinutes);
        Assert.Equal([DayOfWeek.Wednesday], weekly.Days);
        Assert.Equal(21, monthly.MonthDay);
    }

    [Theory]
    [InlineData("8:30", 8, 30)]
    [InlineData("8h30", 8, 30)]
    [InlineData(" 23:59 ", 23, 59)]
    public void ParseTime_ReadsHoursAndMinutes(string text, int hours, int minutes) =>
        Assert.Equal(new TimeSpan(hours, minutes, 0), ScheduleEditing.ParseTime(text));

    [Theory]
    [InlineData("")]
    [InlineData("demain")]
    [InlineData("25:00")]
    public void ParseTime_RefusesWhatIsNotATimeOfDay(string text) => Assert.Null(ScheduleEditing.ParseTime(text));

    [Theory]
    [InlineData("12", 12)]
    [InlineData("-3", 0)]
    [InlineData("abc", 0)]
    public void ParseInt_KeepsPositiveNumbers(string text, int expected) => Assert.Equal(expected, ScheduleEditing.ParseInt(text));

    [Fact]
    public void ComputerNames_SplitsAPastedList() =>
        Assert.Equal(["PC-TEST-01", "PC-TEST-02", "PC-TEST-03", "PC-TEST-04"],
            WindowInputs.ComputerNames("PC-TEST-01, \\\\PC-TEST-02;PC-TEST-03\r\n\tPC-TEST-04 "));

    [Fact]
    public void TriggerProblem_FindsTheFirstInvalidTrigger()
    {
        var ok = new ScheduleTrigger { Type = TriggerType.Daily, Start = Now };
        var noDay = new ScheduleTrigger { Type = TriggerType.Weekly, Start = Now, Days = [] };
        var endsBefore = new ScheduleTrigger { Type = TriggerType.Daily, Start = Now, End = Now.AddHours(-1) };

        Assert.Null(ScheduleEditing.TriggerProblem([ok, new ScheduleTrigger { Type = TriggerType.Once, Start = Now, End = Now.AddDays(-1) }]));
        Assert.Equal(1, ScheduleEditing.TriggerProblem([ok, noDay, endsBefore])?.Index);
        Assert.Contains("fin", ScheduleEditing.TriggerProblem([endsBefore])?.Message);
    }

    [Fact]
    public void Texts()
    {
        Assert.Equal("2. Tool (1 étape)", WindowInputs.PackageLine(1, new Package { Name = "Tool", Steps = [new()] }));
        Assert.Equal("Paramètre du paquet (A)", WindowInputs.PackageSetting(["A", "A"], v => v));
        Assert.Equal("Paramètre de chaque paquet", WindowInputs.PackageSetting(["A", "B"], v => v));
        Assert.Equal("Déclencheur désactivé.", ScheduleEditing.NextText(new ScheduleTrigger { Enabled = false }, Now));
        Assert.Equal("Hebdomadaire", ScheduleEditing.TypeText(TriggerType.Weekly));
        Assert.Equal("1 réussi(s) sur 2 poste(s) × paquet", ScheduleEditing.HistorySummary(
            [new HistoryRow("PC-TEST-01", 1, "Tool", true, 0, null), new HistoryRow("PC-TEST-02", 1, "Tool", false, 2, null)]));
    }
}
