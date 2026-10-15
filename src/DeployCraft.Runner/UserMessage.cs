#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace DeployCraft.Runner
{
    /// <summary>Fenêtre de message affichée par le service dans les sessions ouvertes du poste.</summary>
    internal static class UserMessage
    {
        /// <summary>
        /// Affiche le message dans chaque session, en parallèle. Renvoie une réponse par session
        /// (<see cref="MessageOutcome"/>), ou null si le déploiement est annulé pendant l'attente.
        /// </summary>
        /// <param name="seconds">Fermeture automatique après ce délai (0 = jamais).</param>
        public static int[] Show(IList<uint> sessions, string title, string text, int seconds, bool wait, Func<bool> aborted)
        {
            title = string.IsNullOrWhiteSpace(title) ? "Information" : title;
            text = text ?? "";
            var responses = new int[sessions.Count];
            var threads = new List<Thread>();
            for (var i = 0; i < sessions.Count; i++)
            {
                var index = i;
                var session = sessions[i];
                var thread = new Thread(() =>
                {
                    responses[index] = WTSSendMessage(IntPtr.Zero, session, title, title.Length * 2, text, text.Length * 2,
                        MB_OK | MB_ICONINFORMATION | MB_SETFOREGROUND, seconds, out var response, wait)
                        ? response : MessageOutcome.Error;
                });
                thread.IsBackground = true;
                threads.Add(thread);
                thread.Start();
            }
            foreach (var thread in threads)
                while (!thread.Join(500))
                    if (aborted()) return null;
            return responses;
        }

        private const int MB_OK = 0;
        private const int MB_ICONINFORMATION = 0x40;
        private const int MB_SETFOREGROUND = 0x10000;

        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WTSSendMessage(IntPtr server, uint sessionId, string title, int titleLength,
            string message, int messageLength, int style, int timeout, out int response, bool wait);
    }
}
