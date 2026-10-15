#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DeployCraft.Runner
{
    /// <summary>
    /// Étape « message » : fenêtre lancée par Windows PowerShell dans chaque session ouverte (une fenêtre par
    /// processus, donc jamais mise en attente derrière une autre), et résultat selon les codes de sortie.
    /// </summary>
    internal static class MessageOutcome
    {
        /// <summary>Code de sortie : OK cliqué (ou fenêtre fermée par l'utilisateur).</summary>
        public const int Clicked = 0;
        /// <summary>Code de sortie : fenêtre fermée à la fin de la durée d'affichage, sans clic.</summary>
        public const int Timeout = 2;

        /// <summary>Arguments de powershell.exe : script encodé, le texte n'a donc aucun caractère à échapper sur la ligne de commande.</summary>
        public static string Arguments(string title, string text, int seconds) =>
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " +
            Convert.ToBase64String(Encoding.Unicode.GetBytes(Script(title, text, seconds)));

        /// <param name="seconds">Fermeture automatique après ce délai (0 = jamais), avec le code <see cref="Timeout"/>.</param>
        public static string Script(string title, string text, int seconds)
        {
            string Literal(string s) => "'" + (s ?? "").Replace("'", "''") + "'";
            var script = new StringBuilder()
                .AppendLine("Add-Type -AssemblyName System.Windows.Forms")
                .AppendLine("[System.Windows.Forms.Application]::EnableVisualStyles()")
                .AppendLine("$f = New-Object System.Windows.Forms.Form -Property @{ Text = " + Literal(string.IsNullOrWhiteSpace(title) ? "Information" : title) +
                    "; TopMost = $true; StartPosition = 'CenterScreen'; FormBorderStyle = 'FixedDialog'; MinimizeBox = $false; MaximizeBox = $false; " +
                    "AutoSize = $true; AutoSizeMode = 'GrowAndShrink'; ShowInTaskbar = $true }")
                .AppendLine("$p = New-Object System.Windows.Forms.FlowLayoutPanel -Property @{ FlowDirection = 'TopDown'; AutoSize = $true; AutoSizeMode = 'GrowAndShrink'; Padding = 12 }")
                .AppendLine("$l = New-Object System.Windows.Forms.Label -Property @{ Text = " + Literal(text) + "; UseMnemonic = $false; AutoSize = $true; MaximumSize = New-Object System.Drawing.Size(480, 0); MinimumSize = New-Object System.Drawing.Size(280, 0) }")
                .AppendLine("$b = New-Object System.Windows.Forms.Button -Property @{ Text = 'OK'; DialogResult = 'OK'; Anchor = 'Right'; Margin = '3,16,3,3' }")
                .AppendLine("$p.Controls.Add($l); $p.Controls.Add($b); $f.Controls.Add($p); $f.AcceptButton = $b")
                .AppendLine("$f.add_Shown({ $f.Activate() })")
                .AppendLine("$script:code = " + Clicked);
            if (seconds > 0)
                script.AppendLine("$t = New-Object System.Windows.Forms.Timer -Property @{ Interval = " + seconds * 1000L + " }")
                    .AppendLine("$t.add_Tick({ $t.Stop(); $script:code = " + Timeout + "; $f.Close() })")
                    .AppendLine("$t.Start()");
            return script
                .AppendLine("[void]$f.ShowDialog()")
                .AppendLine("exit $script:code")
                .ToString();
        }

        /// <param name="sessions">Sessions ouvertes où le message a été affiché.</param>
        /// <param name="exitCodes">Codes de sortie des fenêtres, null si l'étape ne les attend pas.</param>
        /// <param name="untilClick">Pas de durée d'affichage : seul le délai max de l'étape a pu fermer la fenêtre.</param>
        public static bool Succeeded(int sessions, IList<int> exitCodes, bool untilClick, out string text)
        {
            var errors = exitCodes?.Count(c => c != Clicked && c != Timeout) ?? 0;
            var timedOut = exitCodes?.Count(c => c == Timeout) ?? 0;
            if (sessions == 0) text = "Aucun utilisateur connecté : message non affiché.";
            else if (errors > 0) text = $"Fenêtre du message en erreur dans {errors} session(s) sur {sessions}.";
            else if (exitCodes == null) text = $"Message affiché dans {sessions} session(s).";
            else if (timedOut == 0) text = $"OK cliqué dans {sessions} session(s).";
            else if (untilClick) text = $"Personne n'a cliqué sur OK avant le délai max de l'étape ({timedOut} session(s) sur {sessions}).";
            else text = $"Message fermé à la fin de la durée d'affichage, sans clic, dans {timedOut} session(s) sur {sessions}.";
            return errors == 0 && !(untilClick && timedOut > 0);
        }
    }
}
