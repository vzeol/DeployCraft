using DeployCraft.Server.Data;

namespace DeployCraft.Server.Validation;

/// <summary>Checks a schedule before it is saved.</summary>
internal static class ScheduleValidation
{
    internal static string? Validate(Schedule s, Func<long, bool> credentialExists)
    {
        if (string.IsNullOrWhiteSpace(s.Name)) return "La planification doit avoir un nom.";
        if (s.CredentialId is { } credentialId && !credentialExists(credentialId)) return "L'identifiant choisi n'existe plus.";
        foreach (var t in s.Triggers)
        {
            if (t.Type == TriggerType.Interval && t.IntervalMinutes < 1) return "L'intervalle doit être d'au moins 1 minute.";
            if (t.Type == TriggerType.Weekly && t.Days.Count == 0) return "Déclencheur hebdomadaire : cochez au moins un jour.";
            if (t.Type != TriggerType.Once && t.End is { } end && end <= t.Start) return "La date de fin d'un déclencheur doit suivre son début.";
        }
        if (s.StopAfterFailures is < 1) return "Le nombre d'échecs doit être d'au moins 1.";
        if (s.QueueTimeoutMinutes is < 1) return "Le délai d'attente doit être d'au moins 1 minute.";
        return null;
    }
}
