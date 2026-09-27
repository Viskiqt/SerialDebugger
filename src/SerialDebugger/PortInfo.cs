using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace SerialDebugger
{
    /// <summary>A COM port plus its Device Manager name, e.g. "COM3" / "JLink CDC UART Port".</summary>
    internal sealed class PortInfo
    {
        public PortInfo(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public string Name { get; }
        public string Description { get; }

        public override string ToString() =>
            string.IsNullOrEmpty(Description) ? Name : Name + "  —  " + Description;

        /// <summary>Lists the COM ports that exist right now, sorted COM1, COM2, ... COM10.</summary>
        public static List<PortInfo> GetPorts()
        {
            var descriptions = GetDeviceDescriptions();
            var names = GetActivePortNames();
            foreach (var port in descriptions.Keys)
                names.Add(port);

            return names
                .Select(n => new PortInfo(n.ToUpperInvariant(), descriptions.TryGetValue(n, out var d) ? d : null))
                .OrderBy(p => PortNumber(p.Name))
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Fast check (registry only) used to notice unplugged / re-plugged devices.</summary>
        public static bool Exists(string portName) =>
            GetActivePortNames().Contains(portName) || GetDeviceDescriptions().ContainsKey(portName);

        /// <summary>Every active port is listed under SERIALCOMM (this is what Windows' own tools use).</summary>
        private static HashSet<string> GetActivePortNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM"))
                {
                    if (key != null)
                    {
                        foreach (var valueName in key.GetValueNames())
                        {
                            if (key.GetValue(valueName) is string port && port.Trim('\0', ' ').Length > 0)
                                names.Add(port.Trim('\0', ' '));
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Registry not readable: callers fall back to the device list.
            }
            return names;
        }

        private static int PortNumber(string name)
        {
            int i = name.Length;
            while (i > 0 && char.IsDigit(name[i - 1])) i--;
            return int.TryParse(name.Substring(i), out int n) ? n : int.MaxValue;
        }

        /// <summary>Maps "COM3" to "JLink CDC UART Port" using the Ports device class.</summary>
        private static Dictionary<string, string> GetDeviceDescriptions()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Guid classGuid = NativeMethods.GUID_DEVCLASS_PORTS;
            IntPtr set = NativeMethods.SetupDiGetClassDevs(ref classGuid, IntPtr.Zero, IntPtr.Zero, NativeMethods.DIGCF_PRESENT);
            if (set == NativeMethods.INVALID_HANDLE_VALUE)
                return result;

            try
            {
                var info = new NativeMethods.SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.SP_DEVINFO_DATA)) };
                for (uint i = 0; NativeMethods.SetupDiEnumDeviceInfo(set, i, ref info); i++)
                {
                    string portName = ReadPortName(set, ref info);
                    if (string.IsNullOrEmpty(portName) || !portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string friendly = ReadFriendlyName(set, ref info) ?? "";
                    // "JLink CDC UART Port (COM3)" -> "JLink CDC UART Port"
                    string suffix = " (" + portName + ")";
                    if (friendly.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        friendly = friendly.Substring(0, friendly.Length - suffix.Length);

                    result[portName] = friendly.Trim();
                }
            }
            catch (Exception)
            {
                // Descriptions are optional; the port names still come from the registry.
            }
            finally
            {
                NativeMethods.SetupDiDestroyDeviceInfoList(set);
            }

            return result;
        }

        private static string ReadFriendlyName(IntPtr set, ref NativeMethods.SP_DEVINFO_DATA info)
        {
            var buffer = new byte[1024];
            if (!NativeMethods.SetupDiGetDeviceRegistryProperty(set, ref info, NativeMethods.SPDRP_FRIENDLYNAME,
                    out _, buffer, buffer.Length, out int size) || size <= 2)
                return null;

            return Encoding.Unicode.GetString(buffer, 0, size).TrimEnd('\0');
        }

        private static string ReadPortName(IntPtr set, ref NativeMethods.SP_DEVINFO_DATA info)
        {
            IntPtr hKey = NativeMethods.SetupDiOpenDevRegKey(set, ref info, NativeMethods.DICS_FLAG_GLOBAL, 0,
                NativeMethods.DIREG_DEV, NativeMethods.KEY_READ);
            if (hKey == NativeMethods.INVALID_HANDLE_VALUE || hKey == IntPtr.Zero)
                return null;

            using (var key = RegistryKey.FromHandle(new SafeRegistryHandle(hKey, true)))
            {
                return (key.GetValue("PortName") as string)?.Trim('\0', ' ');
            }
        }
    }
}
