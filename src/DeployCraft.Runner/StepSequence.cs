#nullable disable
using System;

namespace DeployCraft.Runner
{
    /// <summary>
    /// Sequencing of a job's steps: conditions, disabled steps, stop as failed or as succeeded.
    /// Launching a step is delegated to <c>runStep</c> (testable without a computer).
    /// </summary>
    internal static class StepSequence
    {
        public const string Running = "running";
        public const string Success = "success";
        public const string Failed = "failed";
        public const string Skipped = "skipped";
        /// <summary>The step failed and asked to stop the package as succeeded.</summary>
        public const string Stopped = "stopped";

        public static void Run(Job job, ITargetFacts facts, RunStatus status,
            Action<int, JobStep, StepResult> runStep, Action save, Func<bool> aborted, Action<string> log)
        {
            // A progress update that is not saved is caught up at the next one: it must never interrupt the steps.
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

            // Package conditions not met: all steps are skipped, without failure.
            var packageSkip = Unmet(job.Conditions, facts, out var packageError);
            if (packageSkip != null) log("Conditions du paquet non remplies : " + packageSkip);

            var first = Math.Max(1, job.FirstNumber);
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
                    stopReason = $"Paquet arrêté en succès par l'étape {first + i} « {step.Name} ».";
                }
                if (result.State == Stopped || result.State == Failed && (packageError != null || !step.ContinueOnError))
                    stop = true;

                log($"Étape {first + i} « {step.Name} » : {result.State}" +
                    (result.Error != null ? $" ({result.Error})" : result.ExitCode != null ? $" (code {result.ExitCode})" : ""));
                Save();
            }

            // For the server, which chains any other identities: should it stop here?
            status.Halt = stop || packageSkip != null;
            status.HaltReason = stop ? stopReason : packageSkip != null ? "Conditions du paquet non remplies. " + packageSkip : null;
        }

        /// <summary>Waits <paramref name="seconds"/> seconds in short slices; false if the deployment is cancelled in the meantime.</summary>
        public static bool Wait(int seconds, Func<bool> aborted, Action<int> sleep)
        {
            for (var left = Math.Max(0, seconds) * 1000; left > 0; left -= 500)
            {
                if (aborted()) return false;
                sleep(Math.Min(500, left));
            }
            return !aborted();
        }

        /// <summary>First additional condition not met, with its origin (null = all met).</summary>
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
        /// Reason why the conditions are not met (null = met).
        /// A condition that cannot be checked (misspelled registry key...) is an error, returned in <paramref name="error"/>.
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
