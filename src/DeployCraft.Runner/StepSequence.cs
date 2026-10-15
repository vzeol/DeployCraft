#nullable disable
using System;

namespace DeployCraft.Runner
{
    /// <summary>
    /// Enchaînement des étapes d'un job : conditions, étapes désactivées, arrêt en échec ou en succès.
    /// Le lancement d'une étape est confié à <c>runStep</c> (testable sans poste).
    /// </summary>
    internal static class StepSequence
    {
        public const string Running = "running";
        public const string Success = "success";
        public const string Failed = "failed";
        public const string Skipped = "skipped";
        /// <summary>L'étape a échoué et demandait d'arrêter le paquet en succès.</summary>
        public const string Stopped = "stopped";

        public static void Run(Job job, ITargetFacts facts, RunStatus status,
            Action<int, JobStep, StepResult> runStep, Action save, Func<bool> aborted, Action<string> log)
        {
            // Un avancement non enregistré se rattrape au prochain : il ne doit jamais interrompre les étapes.
            void Save()
            {
                try
                {
                    save();
                }
                catch (Exception ex)
                {
                    log("Enregistrement de l'avancement impossible : " + ex.Message);
                }
            }

            // Conditions du paquet non remplies : toutes les étapes sont sautées, sans échec.
            var packageSkip = Unmet(job.Conditions, facts, out var packageError);
            if (packageSkip != null) log("Conditions du paquet non remplies : " + packageSkip);

            var stop = false;
            string stopReason = null;
            for (var i = 0; i < job.Steps.Count; i++)
            {
                var step = job.Steps[i];
                var result = new StepResult { Name = step.Name, State = Running, StartedUtc = Json.Now() };
                status.Steps.Add(result);

                if (stop || aborted())
                {
                    result.State = Skipped;
                    result.Error = stopReason;
                    result.EndedUtc = Json.Now();
                    continue;
                }

                string stepSkip = null, stepError = null;
                if (step.Disabled)
                {
                    result.State = Skipped;
                    result.Error = "Étape désactivée.";
                }
                else if (packageSkip != null)
                {
                    result.State = Skipped;
                    result.Error = "Conditions du paquet non remplies. " + packageSkip;
                }
                else if (packageError != null)
                {
                    result.State = Failed;
                    result.Error = packageError;
                }
                else if ((stepSkip = UnmetExtra(step.ExtraConditions, facts, out stepError)) != null)
                {
                    result.State = Skipped;
                    result.Error = stepSkip;
                }
                else if (stepError != null)
                {
                    result.State = Failed;
                    result.Error = stepError;
                }
                else if ((stepSkip = Unmet(step.Conditions, facts, out stepError)) != null)
                {
                    result.State = Skipped;
                    result.Error = "Condition non remplie. " + stepSkip;
                }
                else if (stepError != null)
                {
                    result.State = Failed;
                    result.Error = stepError;
                }

                if (result.State == Running)
                {
                    Save();
                    runStep(i, step, result);
                }
                result.EndedUtc = Json.Now();

                if (result.State == Failed && packageError == null && step.StopSuccessOnError)
                {
                    result.State = Stopped;
                    stopReason = $"Paquet arrêté en succès par l'étape {i + 1} « {step.Name} ».";
                }
                if (result.State == Stopped || result.State == Failed && (packageError != null || !step.ContinueOnError))
                    stop = true;

                log($"Étape {i + 1} « {step.Name} » : {result.State}" +
                    (result.Error != null ? $" ({result.Error})" : result.ExitCode != null ? $" (code {result.ExitCode})" : ""));
                Save();
            }
        }

        /// <summary>Attend <paramref name="seconds"/> secondes par tranches courtes ; false si le déploiement est annulé entre-temps.</summary>
        public static bool Wait(int seconds, Func<bool> aborted, Action<int> sleep)
        {
            for (var left = Math.Max(0, seconds) * 1000; left > 0; left -= 500)
            {
                if (aborted()) return false;
                sleep(Math.Min(500, left));
            }
            return !aborted();
        }

        /// <summary>Première condition supplémentaire non remplie, avec son origine (null = toutes remplies).</summary>
        private static string UnmetExtra(System.Collections.Generic.List<JobConditionGroup> groups, ITargetFacts facts, out string error)
        {
            error = null;
            if (groups == null) return null;
            foreach (var group in groups)
            {
                var unmet = Unmet(group.Conditions, facts, out error);
                if (error != null) return null;
                if (unmet != null) return $"{group.Label} non remplies. {unmet}";
            }
            return null;
        }

        /// <summary>
        /// Raison pour laquelle les conditions ne sont pas remplies (null = remplies).
        /// Une condition impossible à vérifier (clé de registre mal écrite…) est une erreur, rendue dans <paramref name="error"/>.
        /// </summary>
        private static string Unmet(JobConditions conditions, ITargetFacts facts, out string error)
        {
            error = null;
            try
            {
                return ConditionCheck.Failure(conditions, facts);
            }
            catch (Exception ex)
            {
                error = "Vérification des conditions impossible : " + ex.Message;
                return null;
            }
        }
    }
}
