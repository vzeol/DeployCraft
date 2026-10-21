#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DeployCraft.Runner
{
    /// <summary>
    /// "Message" step: window launched by Windows PowerShell in each open session (one window per
    /// process, so never queued behind another), and result according to the exit codes.
    /// </summary>
    internal static class MessageOutcome
    {
        /// <summary>Exit code: OK clicked (or window closed by the user).</summary>
        public const int Clicked = 0;
        /// <summary>Exit code: window closed at the end of the display duration, without a click.</summary>
        public const int Timeout = 2;

        /// <summary>powershell.exe arguments: encoded script, so the text has no character to escape on the command line.</summary>
        public static string Arguments(string title, string text, int seconds) =>
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " +
            Convert.ToBase64String(Encoding.Unicode.GetBytes(Script(title, text, seconds)));

        /// <param name="seconds">Automatic close after this delay (0 = never), with the <see cref="Timeout"/> code.</param>
        public static string Script(string title, string text, int seconds)
        {
            string Literal(string s) => "'" + (s ?? "").Replace("'", "''") + "'";
            var script = new StringBuilder()
                .AppendLine("Add-Type -AssemblyName System.Windows.Forms")
                .AppendLine("[System.Windows.Forms.Application]::EnableVisualStyles()")
                .AppendLine("$f = New-Object System.Windows.Forms.Form -Property @{ Text = " + Literal(string.IsNullOrWhiteSpace(title) ? RunnerTexts.Get("MessageOutcome_DefaultTitle") : title) +
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

        /// <param name="sessions">Open sessions where the message was shown.</param>
        /// <param name="exitCodes">Exit codes of the windows, null if the step does not wait for them.</param>
        /// <param name="untilClick">No display duration: only the step's max delay could close the window.</param>
        public static bool Succeeded(int sessions, IList<int> exitCodes, bool untilClick, out string text)
        {
            var errors = exitCodes?.Count(c => c != Clicked && c != Timeout) ?? 0;
            var timedOut = exitCodes?.Count(c => c == Timeout) ?? 0;
            if (sessions == 0) text = RunnerTexts.Get("MessageOutcome_NoUser");
            else if (errors > 0) text = RunnerTexts.Format("MessageOutcome_WindowError", errors, sessions);
            else if (exitCodes == null) text = RunnerTexts.Format("MessageOutcome_Shown", sessions);
            else if (timedOut == 0) text = RunnerTexts.Format("MessageOutcome_Clicked", sessions);
            else if (untilClick) text = RunnerTexts.Format("MessageOutcome_NoClickTimeout", timedOut, sessions);
            else text = RunnerTexts.Format("MessageOutcome_ClosedNoClick", timedOut, sessions);
            return errors == 0 && !(untilClick && timedOut > 0);
        }
    }
}
