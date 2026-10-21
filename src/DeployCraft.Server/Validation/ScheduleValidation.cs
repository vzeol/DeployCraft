using DeployCraft.Server.Data;

namespace DeployCraft.Server.Validation;

/// <summary>Checks a schedule before it is saved.</summary>
internal static class ScheduleValidation
{
    internal static string? Validate(Schedule s, Func<long, bool> credentialExists)
    {
        if (string.IsNullOrWhiteSpace(s.Name)) return Msg.Get("ScheduleValidation_NameRequired");
        if (s.CredentialId is { } credentialId && !credentialExists(credentialId)) return Msg.Get("ScheduleValidation_CredentialGone");
        foreach (var t in s.Triggers)
        {
            if (t.Type == TriggerType.Interval && t.IntervalMinutes < 1) return Msg.Get("ScheduleValidation_IntervalMin");
            if (t.Type == TriggerType.Weekly && t.Days.Count == 0) return Msg.Get("ScheduleValidation_WeeklyDay");
            if (t.Type != TriggerType.Once && t.End is { } end && end <= t.Start) return Msg.Get("ScheduleValidation_EndAfterStart");
        }
        if (s.StopAfterFailures is < 1) return Msg.Get("ScheduleValidation_FailuresMin");
        if (s.QueueTimeoutMinutes is < 1) return Msg.Get("ScheduleValidation_QueueTimeoutMin");
        return null;
    }
}
