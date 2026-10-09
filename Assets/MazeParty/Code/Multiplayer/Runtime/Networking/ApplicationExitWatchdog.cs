using System;
using System.Threading;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System.Diagnostics;
using System.Runtime.InteropServices;
#endif

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Last-resort protection for the Unity 6000.6 Windows native-shutdown stall.
    /// Normal process exit tears this background thread down before it fires.
    /// </summary>
    internal static class ApplicationExitWatchdog
    {
        private static int _armed;

        public static void Arm(int timeoutMilliseconds, int exitCode)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (timeoutMilliseconds <= 0 ||
                Interlocked.CompareExchange(ref _armed, 1, 0) != 0)
            {
                return;
            }

            Environment.ExitCode = exitCode;
            var watchdog = new Thread(
                () => TerminateAfterDelay(timeoutMilliseconds, exitCode))
            {
                IsBackground = true,
                Name = "MazeParty Exit Watchdog"
            };
            watchdog.Start();
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private static void TerminateAfterDelay(
            int timeoutMilliseconds,
            int exitCode)
        {
            Thread.Sleep(timeoutMilliseconds);
            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    if (!TerminateProcess(
                            process.Handle,
                            unchecked((uint)exitCode)))
                    {
                        process.Kill();
                    }
                }
            }
            catch
            {
                // The process may already be exiting. There is no safe managed
                // recovery left at this point, so the watchdog stays silent.
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(
            IntPtr processHandle,
            uint exitCode);
#endif
    }
}
