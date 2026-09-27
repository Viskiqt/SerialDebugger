using System;
using System.Drawing;
using System.Globalization;
using Microsoft.Win32;

namespace SerialDebugger
{
    /// <summary>Remembers the user's choices between runs (HKCU\Software\Serial Debugger).</summary>
    internal sealed class AppSettings
    {
        private const string KeyPath = @"Software\Serial Debugger";

        public string LastPort { get; set; }
        public int BaudRate { get; set; } = 115200;
        public bool AutoScroll { get; set; } = true;
        public int LineEnding { get; set; } = 3;   // index into the "line ending" list: CR+LF
        public Rectangle? WindowBounds { get; set; }
        public bool Maximized { get; set; }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    if (key == null)
                        return settings;

                    settings.LastPort = key.GetValue("LastPort") as string;
                    if (key.GetValue("BaudRate") is int baud && baud > 0)
                        settings.BaudRate = baud;
                    if (key.GetValue("AutoScroll") is int autoScroll)
                        settings.AutoScroll = autoScroll != 0;
                    if (key.GetValue("LineEnding") is int lineEnding)
                        settings.LineEnding = lineEnding;
                    if (key.GetValue("Maximized") is int maximized)
                        settings.Maximized = maximized != 0;
                    settings.WindowBounds = ParseRectangle(key.GetValue("WindowBounds") as string);
                }
            }
            catch (Exception)
            {
                // Unreadable settings are ignored; defaults are used.
            }
            return settings;
        }

        public void Save()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    key.SetValue("LastPort", LastPort ?? "");
                    key.SetValue("BaudRate", BaudRate, RegistryValueKind.DWord);
                    key.SetValue("AutoScroll", AutoScroll ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("LineEnding", LineEnding, RegistryValueKind.DWord);
                    key.SetValue("Maximized", Maximized ? 1 : 0, RegistryValueKind.DWord);
                    if (WindowBounds is Rectangle r)
                        key.SetValue("WindowBounds", string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}", r.X, r.Y, r.Width, r.Height));
                }
            }
            catch (Exception)
            {
                // Failing to save settings must never stop the app from closing.
            }
        }

        private static Rectangle? ParseRectangle(string text)
        {
            var parts = (text ?? "").Split(',');
            if (parts.Length != 4)
                return null;

            var values = new int[4];
            for (int i = 0; i < 4; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                    return null;
            }
            return new Rectangle(values[0], values[1], values[2], values[3]);
        }
    }
}
