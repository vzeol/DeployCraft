using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace DeployCraft.Runner
{
    /// <summary>
    /// Process started in the logged-on user's session (desktop winsta0\default),
    /// with its output collected through a pipe. The runner must run as Local System.
    /// </summary>
    internal sealed class UserProcess : IDisposable
    {
        private IntPtr _process;
        private Thread _reader;

        public int Id { get; private set; }

        /// <summary>User of the targeted session (DOMAIN\name), for the log.</summary>
        public string User { get; private set; }

        /// <param name="sessionId">Session where to start the process (null = the first open session).</param>
        /// <param name="onOutput">Receives the program output in chunks of bytes (decoded later).</param>
        public static UserProcess Start(string fileName, string arguments, string workingDirectory, Action<byte[], int> onOutput, uint? sessionId = null)
        {
            string user;
            uint session;
            if (sessionId is uint id)
            {
                session = id;
                user = SessionUser(id);
            }
            else session = FindActiveSession(out user);
            if (!WTSQueryUserToken(session, out var token))
            {
                var code = Marshal.GetLastWin32Error();
                if (code == ERROR_NO_TOKEN) throw new InvalidOperationException(RunnerTexts.Get("UserProcess_NoUser"));
                throw new Win32Exception(code, RunnerTexts.Format("UserProcess_TokenFailed", new Win32Exception(code).Message));
            }

            IntPtr environment = IntPtr.Zero, outRead = IntPtr.Zero, outWrite = IntPtr.Zero, inRead = IntPtr.Zero, inWrite = IntPtr.Zero;
            try
            {
                if (!CreateEnvironmentBlock(out environment, token, false)) environment = IntPtr.Zero;

                var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)), bInheritHandle = true };
                if (!CreatePipe(out outRead, out outWrite, ref sa, 0) || !CreatePipe(out inRead, out inWrite, ref sa, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), RunnerTexts.Get("UserProcess_PipesFailed"));
                // Only the child-side ends are inherited.
                SetHandleInformation(outRead, HANDLE_FLAG_INHERIT, 0);
                SetHandleInformation(inWrite, HANDLE_FLAG_INHERIT, 0);

                var si = new STARTUPINFO
                {
                    cb = Marshal.SizeOf(typeof(STARTUPINFO)),
                    lpDesktop = @"winsta0\default",
                    dwFlags = STARTF_USESTDHANDLES,
                    hStdInput = inRead,
                    hStdOutput = outWrite,
                    hStdError = outWrite,
                };
                var commandLine = new StringBuilder($"\"{fileName}\" {arguments}".Trim());
                if (!CreateProcessAsUser(token, null, commandLine, IntPtr.Zero, IntPtr.Zero, true,
                        CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW, environment, workingDirectory, ref si, out var pi))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), RunnerTexts.Get("UserProcess_LaunchFailed"));

                CloseHandle(pi.hThread);
                var result = new UserProcess { _process = pi.hProcess, Id = pi.dwProcessId, User = user };

                // No input possible: the input is closed right away, as for the System steps.
                CloseHandle(inWrite); inWrite = IntPtr.Zero;
                var readHandle = new SafeFileHandle(outRead, true);
                outRead = IntPtr.Zero;
                result._reader = new Thread(() =>
                {
                    using (var stream = new FileStream(readHandle, FileAccess.Read))
                    {
                        var buffer = new byte[4096];
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) onOutput(buffer, read);
                    }
                })
                { IsBackground = true, Name = "UserProcessOutput" };
                result._reader.Start();
                return result;
            }
            finally
            {
                // The ends inherited by the child are closed on our side: reading stops when it ends.
                foreach (var h in new[] { outWrite, inRead, inWrite, outRead })
                    if (h != IntPtr.Zero) CloseHandle(h);
                if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
                CloseHandle(token);
            }
        }

        public bool WaitForExit(int milliseconds) => WaitForSingleObject(_process, (uint)milliseconds) == 0;

        /// <summary>Waits for the end of the output reading (after the process has ended).</summary>
        public void WaitForOutput() => _reader?.Join(10000);

        public int ExitCode => GetExitCodeProcess(_process, out var code) ? (int)code : -1;

        public void Dispose()
        {
            if (_process != IntPtr.Zero) CloseHandle(_process);
            _process = IntPtr.Zero;
        }

        /// <summary>Has someone opened a session on the computer (locked screen included)?</summary>
        public static bool IsUserLoggedOn()
        {
            try
            {
                FindActiveSession(out var user);
                return !string.IsNullOrEmpty(user);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>Sessions open on the computer (except the services session), with their user.</summary>
        public static List<KeyValuePair<uint, string>> ActiveSessions()
        {
            var list = new List<KeyValuePair<uint, string>>();
            if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var sessions, out var count)) return list;
            try
            {
                var size = Marshal.SizeOf(typeof(WTS_SESSION_INFO));
                for (var i = 0; i < count; i++)
                {
                    var info = (WTS_SESSION_INFO)Marshal.PtrToStructure(new IntPtr(sessions.ToInt64() + i * size), typeof(WTS_SESSION_INFO));
                    if (info.State != WTSActive || info.SessionId == 0) continue;
                    list.Add(new KeyValuePair<uint, string>((uint)info.SessionId, SessionUser((uint)info.SessionId)));
                }
            }
            finally
            {
                WTSFreeMemory(sessions);
            }
            return list;
        }

        private static uint FindActiveSession(out string user)
        {
            var sessions = ActiveSessions();
            if (sessions.Count > 0)
            {
                user = sessions[0].Value;
                return sessions[0].Key;
            }
            var console = WTSGetActiveConsoleSessionId();
            if (console == 0xFFFFFFFF) throw new InvalidOperationException(RunnerTexts.Get("UserProcess_NoUser"));
            user = SessionUser(console);
            return console;
        }

        private static string SessionUser(uint session)
        {
            string Query(int infoClass)
            {
                if (!WTSQuerySessionInformation(IntPtr.Zero, session, infoClass, out var buffer, out _)) return null;
                try { return Marshal.PtrToStringUni(buffer); }
                finally { WTSFreeMemory(buffer); }
            }
            var name = Query(WTSUserName);
            if (string.IsNullOrEmpty(name)) return null;
            var domain = Query(WTSDomainName);
            return string.IsNullOrEmpty(domain) ? name : domain + "\\" + name;
        }

        // ---------- Win32 ----------

        private const int ERROR_NO_TOKEN = 1008;
        private const int WTSActive = 0;
        private const int WTSUserName = 5;
        private const int WTSDomainName = 7;
        private const uint HANDLE_FLAG_INHERIT = 1;
        private const int STARTF_USESTDHANDLES = 0x100;
        private const uint CREATE_UNICODE_ENVIRONMENT = 0x400;
        private const uint CREATE_NO_WINDOW = 0x08000000;

        [StructLayout(LayoutKind.Sequential)]
        private struct WTS_SESSION_INFO
        {
            public int SessionId;
            public IntPtr pWinStationName;
            public int State;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public bool bInheritHandle;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr sessions, out int count);

        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WTSQuerySessionInformation(IntPtr server, uint sessionId, int infoClass, out IntPtr buffer, out int bytes);

        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr memory);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

        [DllImport("kernel32.dll")]
        private static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool DestroyEnvironmentBlock(IntPtr environment);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CreatePipe(out IntPtr read, out IntPtr write, ref SECURITY_ATTRIBUTES attributes, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessAsUser(IntPtr token, string applicationName, StringBuilder commandLine,
            IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment,
            string currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
