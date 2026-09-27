using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace SerialDebuggerSetup
{
    /// <summary>
    /// SerialDebugger-Setup.exe            -> setup window
    /// Uninstall.exe /uninstall [/quiet]   -> (Settings > Apps) copies itself to %TEMP% and runs from there,
    ///                                        because a program cannot delete its own file while running
    /// ... /uninstall-from "dir" pid       -> the actual uninstall
    /// </summary>
    internal static class Program
    {
        private const string Title = InstallerCore.AppName + " Setup";

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool quiet = args.Any(a => a.Equals("/quiet", StringComparison.OrdinalIgnoreCase));
            try
            {
                if (args.Length > 0 && args[0].Equals("/uninstall", StringComparison.OrdinalIgnoreCase))
                    return RelaunchFromTemp(quiet);
                if (args.Length > 2 && args[0].Equals("/uninstall-from", StringComparison.OrdinalIgnoreCase))
                    return RunUninstall(args[1], int.TryParse(args[2], out int parent) ? parent : 0, quiet);

                Application.Run(new SetupForm());
                return 0;
            }
            catch (Exception ex)
            {
                if (!quiet)
                    MessageBox.Show(ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int RelaunchFromTemp(bool quiet)
        {
            string self = Assembly.GetExecutingAssembly().Location;
            string installDir = Path.GetDirectoryName(self);
            string tempCopy = Path.Combine(Path.GetTempPath(), "SerialDebugger-Uninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
            File.Copy(self, tempCopy, true);

            string arguments = $"/uninstall-from \"{installDir}\" {Process.GetCurrentProcess().Id}" + (quiet ? " /quiet" : "");
            Process.Start(new ProcessStartInfo(tempCopy, arguments) { UseShellExecute = false });
            return 0;
        }

        private static int RunUninstall(string installDir, int parentId, bool quiet)
        {
            WaitForExit(parentId);

            if (!quiet && MessageBox.Show($"Remove {InstallerCore.AppName} from this PC?", "Uninstall " + InstallerCore.AppName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                DeleteSelfLater();
                return 1;
            }

            if (!EnsureAppClosed(null, installDir, quiet))
            {
                DeleteSelfLater();
                return 1;
            }

            InstallerCore.Uninstall(installDir);
            if (!quiet)
                MessageBox.Show($"{InstallerCore.AppName} was removed from your PC.", "Uninstall " + InstallerCore.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            DeleteSelfLater();
            return 0;
        }

        /// <summary>Asks to close running copies of the app (their files cannot be replaced or removed).</summary>
        public static bool EnsureAppClosed(IWin32Window owner, string dir, bool quiet = false)
        {
            var running = InstallerCore.GetRunningInstances(dir);
            if (running.Count == 0)
                return true;

            if (!quiet && MessageBox.Show(owner, $"{InstallerCore.AppName} is open. Close it and continue?", Title,
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return false;

            foreach (var process in running)
            {
                try
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(5000))
                        process.Kill();
                    process.WaitForExit(5000);
                }
                catch (Exception)
                {
                    // Already exited.
                }
            }
            return InstallerCore.GetRunningInstances(dir).Count == 0;
        }

        public static Icon LoadIcon(int size)
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("App.ico"))
                    return stream == null ? null : new Icon(stream, new Size(size, size));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void WaitForExit(int processId)
        {
            try
            {
                if (processId > 0)
                    Process.GetProcessById(processId).WaitForExit(10000);
            }
            catch (ArgumentException)
            {
                // Already gone.
            }
        }

        /// <summary>The temp copy removes itself a few seconds after exiting.</summary>
        private static void DeleteSelfLater()
        {
            string self = Assembly.GetExecutingAssembly().Location;
            if (!self.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
                return;
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 4 127.0.0.1 >nul & del /f /q \"{self}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
            }
            catch (Exception)
            {
                // Windows cleans %TEMP% eventually.
            }
        }
    }
}
