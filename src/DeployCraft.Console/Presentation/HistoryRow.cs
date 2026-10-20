namespace DeployCraft.Console;

/// <summary>Line of the target history of a schedule.</summary>
public record HistoryRow(string Computer, long PackageId, string PackageName, bool Succeeded, int Failures, DateTime? LastUtc)
{
    public string SuccessIcon => Succeeded ? Ui.StatusIcon("Successful") : "";
    public string Last => Ui.LocalTime(LastUtc);
}
