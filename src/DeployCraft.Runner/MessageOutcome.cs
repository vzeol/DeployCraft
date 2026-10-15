#nullable disable
using System.Collections.Generic;
using System.Linq;

namespace DeployCraft.Runner
{
    /// <summary>Étape « message » : résultat selon les réponses des sessions où le message a été affiché.</summary>
    internal static class MessageOutcome
    {
        /// <summary>Message fermé à la fin de la durée d'affichage, sans clic.</summary>
        public const int Timeout = 32000;
        /// <summary>Affichage impossible dans cette session.</summary>
        public const int Error = -1;

        /// <param name="responses">Une réponse par session ouverte.</param>
        /// <param name="untilClick">Pas de durée d'affichage : seul le délai max de l'étape a pu fermer le message.</param>
        public static bool Succeeded(IList<int> responses, bool wait, bool untilClick, out string text)
        {
            var count = responses.Count;
            var errors = responses.Count(r => r == Error);
            var timedOut = responses.Count(r => r == Timeout);
            if (count == 0) text = "Aucun utilisateur connecté : message non affiché.";
            else if (errors > 0) text = $"Message impossible à afficher dans {errors} session(s) sur {count}.";
            else if (!wait) text = $"Message affiché dans {count} session(s).";
            else if (timedOut == 0) text = $"OK cliqué dans {count} session(s).";
            else if (untilClick) text = $"Personne n'a cliqué sur OK avant le délai max de l'étape ({timedOut} session(s) sur {count}).";
            else text = $"Message fermé à la fin de la durée d'affichage, sans clic, dans {timedOut} session(s) sur {count}.";
            return errors == 0 && !(wait && untilClick && timedOut > 0);
        }
    }
}
