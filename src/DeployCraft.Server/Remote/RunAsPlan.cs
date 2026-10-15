using DeployCraft.Server.Data;

namespace DeployCraft.Server.Remote;

/// <summary>
/// Identité de chaque étape et « tranches » lancées sur le poste. Un service runner n'a qu'une identité : compte de
/// déploiement, ou Système local (qui sert aussi à lancer dans la session de l'utilisateur connecté). Les étapes
/// consécutives de même service forment une tranche ; le serveur lance les tranches l'une après l'autre. Le mot de
/// passe du compte ne quitte jamais le serveur (il ne sert qu'à créer le service).
/// </summary>
internal static class RunAsPlan
{
    /// <param name="Start">Position de la première étape dans la liste envoyée (0 = la première).</param>
    /// <param name="AsDeployUser">Service sous le compte de déploiement ; sinon Système local.</param>
    public sealed record Tranche(int Start, int Count, bool AsDeployUser);

    /// <summary>Identité réelle d'une étape : la sienne, sinon celle du déploiement ; sans identifiant, Système local.</summary>
    public static RunAsMode Effective(PackageStep step, RunAsMode deployment, bool hasAccount) =>
        (step.RunAs ?? deployment) is var mode && mode == RunAsMode.DeployUser && !hasAccount ? RunAsMode.LocalSystem : mode;

    public static List<Tranche> Split(IReadOnlyList<RunAsMode> modes)
    {
        var tranches = new List<Tranche>();
        for (var i = 0; i < modes.Count;)
        {
            var asDeployUser = modes[i] == RunAsMode.DeployUser;
            var j = i;
            while (j < modes.Count && (modes[j] == RunAsMode.DeployUser) == asDeployUser) j++;
            tranches.Add(new Tranche(i, j - i, asDeployUser));
            i = j;
        }
        return tranches;
    }

    /// <summary>
    /// Avancement complet : résultats des tranches finies, puis ceux de la tranche en cours. Si la tranche en cours
    /// arrête le paquet, les étapes des tranches suivantes y sont ajoutées « ignorées » avec sa raison.
    /// </summary>
    public static RunnerStatus Combine(IReadOnlyList<RunnerStepResult> done, RunnerStatus current, IReadOnlyList<string> later, bool last)
    {
        var steps = new List<RunnerStepResult>(done);
        steps.AddRange(current.Steps);
        var finished = current.State == "done";
        if (finished && current.Halt)
            steps.AddRange(later.Select(name => new RunnerStepResult { Name = name, State = "skipped", Error = current.HaltReason }));
        return new RunnerStatus
        {
            State = finished && (last || current.Halt) ? "done" : "running",
            Error = current.Error,
            Steps = steps,
            Network = current.Network,
            Halt = current.Halt,
            HaltReason = current.HaltReason,
        };
    }
}
