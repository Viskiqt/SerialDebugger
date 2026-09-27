using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace SerialDebuggerSetup
{
    /// <summary>Per-user install / uninstall of Serial Debugger (no administrator rights needed).</summary>
    internal static class InstallerCore
    {
        public const string AppName = "Serial Debugger";
        public const string ExeName = "SerialDebugger.exe";
        public const string UninstallerName = "Uninstall.exe";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SerialDebugger";
        private const string SettingsKey = @"Software\Serial Debugger";

        public static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        public static string DefaultInstallDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);

        private static string StartMenuShortcut => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

        private static string DesktopShortcut => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

        /// <summary>Folder and version of an existing install, or nulls.</summary>
        public static (string Dir, string Version) GetExistingInstall()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey))
                    return (key?.GetValue("InstallLocation") as string, key?.GetValue("DisplayVersion") as string);
            }
            catch (Exception)
            {
                return (null, null);
            }
        }

        /// <summary>Running copies of the app from the given folder (their files cannot be replaced).</summary>
        public static List<Process> GetRunningInstances(string dir)
        {
            string exePath = Path.GetFullPath(Path.Combine(dir, ExeName));
            var result = new List<Process>();
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
            {
                try
                {
                    if (string.Equals(process.MainModule?.FileName, exePath, StringComparison.OrdinalIgnoreCase))
                        result.Add(process);
                }
                catch (Exception)
                {
                    // Process of another user or exiting: not ours to worry about.
                }
            }
            return result;
        }

        /// <summary>
        /// Installs or updates the app. Returns problems that did not stop the install
        /// (e.g. a shortcut that could not be created); an empty list means everything worked.
        /// </summary>
        public static List<string> Install(string dir, bool desktopShortcut)
        {
            var warnings = new List<string>();
            Directory.CreateDirectory(dir);
            string exe = Path.Combine(dir, ExeName);

            ExtractPayload("payload.SerialDebugger.exe", exe);
            ExtractPayload("payload.SerialDebugger.exe.config", exe + ".config");

            // This setup program doubles as the uninstaller.
            string self = Assembly.GetExecutingAssembly().Location;
            string uninstaller = Path.Combine(dir, UninstallerName);
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(uninstaller), StringComparison.OrdinalIgnoreCase))
                File.Copy(self, uninstaller, true);

            // Register in Settings > Apps before anything optional, so the app can always be uninstalled.
            Register(dir, exe, uninstaller);

            // Shortcuts are nice to have: a failure is reported but never stops the install.
            TryCreateShortcut(StartMenuShortcut, exe, dir, "Start menu", warnings);
            if (desktopShortcut)
                TryCreateShortcut(DesktopShortcut, exe, dir, "desktop", warnings);
            else
                TryDelete(DesktopShortcut);

            return warnings;
        }

        private static void TryCreateShortcut(string shortcutPath, string target, string workingDir, string where, List<string> warnings)
        {
            try
            {
                CreateShortcut(shortcutPath, target, workingDir);
            }
            catch (Exception ex)
            {
                warnings.Add($"The {where} shortcut could not be created: {ex.Message}");
            }
        }

        private static void Register(string dir, string exe, string uninstaller)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                long sizeKb = new[] { exe, exe + ".config", uninstaller }.Where(File.Exists).Sum(f => new FileInfo(f).Length) / 1024;
                key.SetValue("DisplayName", AppName);
                key.SetValue("DisplayVersion", Version);
                key.SetValue("Publisher", AppName);
                key.SetValue("DisplayIcon", exe + ",0");
                key.SetValue("InstallLocation", dir);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                key.SetValue("UninstallString", $"\"{uninstaller}\" /uninstall");
                key.SetValue("QuietUninstallString", $"\"{uninstaller}\" /uninstall /quiet");
                key.SetValue("EstimatedSize", (int)Math.Max(1, sizeKb), RegistryValueKind.DWord);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        /// <summary>Removes only what setup created; other files in the folder are left alone.</summary>
        public static void Uninstall(string dir)
        {
            TryDelete(StartMenuShortcut);
            TryDelete(DesktopShortcut);

            foreach (string name in new[] { ExeName, ExeName + ".config", UninstallerName })
                TryDelete(Path.Combine(dir, name));
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch (Exception)
            {
                // Folder in use: harmless, it is empty or holds the user's own files.
            }

            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            Registry.CurrentUser.DeleteSubKeyTree(SettingsKey, false);
        }

        private static void ExtractPayload(string resourceName, string target)
        {
            using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (source == null)
                    throw new InvalidOperationException("This setup file is damaged (missing " + resourceName + "). Please download it again.");

                // Write next to the target first, then swap, so a failure never leaves a half-written exe.
                string temp = target + ".new";
                using (var file = File.Create(temp))
                    source.CopyTo(file);
                if (File.Exists(target))
                    File.Delete(target);
                File.Move(temp, target);
            }
        }

        private static void CreateShortcut(string shortcutPath, string target, string workingDir)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath));
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            try
            {
                dynamic link = shell.CreateShortcut(shortcutPath);
                link.TargetPath = target;
                link.WorkingDirectory = workingDir;
                link.IconLocation = target + ",0";
                link.Description = "Simple, reliable serial port monitor";
                link.Save();
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception)
            {
                // Best effort.
            }
        }
    }
}
