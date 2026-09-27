using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SerialDebugger
{
    /// <summary>Win32 declarations used by the app.</summary>
    internal static class NativeMethods
    {
        // ---------------------------------------------------------------- serial port (kernel32)

        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        public const uint PURGE_TXCLEAR = 0x0004;
        public const uint PURGE_RXCLEAR = 0x0008;
        public const uint MAXDWORD = 0xFFFFFFFF;

        public const int ERROR_FILE_NOT_FOUND = 2;
        public const int ERROR_PATH_NOT_FOUND = 3;
        public const int ERROR_ACCESS_DENIED = 5;

        [StructLayout(LayoutKind.Sequential)]
        public struct DCB
        {
            public uint DCBlength;
            public uint BaudRate;
            public uint Flags;          // bit fields: fBinary, fDtrControl, fRtsControl, ...
            public ushort wReserved;
            public ushort XonLim;
            public ushort XoffLim;
            public byte ByteSize;
            public byte Parity;
            public byte StopBits;
            public sbyte XonChar;
            public sbyte XoffChar;
            public sbyte ErrorChar;
            public sbyte EofChar;
            public sbyte EvtChar;
            public ushort wReserved1;
        }

        // fBinary = 1, fDtrControl = DTR_CONTROL_ENABLE, fRtsControl = RTS_CONTROL_ENABLE,
        // no hardware/software flow control, NUL bytes kept, no abort on error.
        public const uint DCB_FLAGS_BINARY_DTR_RTS = 0x0001 | (1u << 4) | (1u << 12);

        [StructLayout(LayoutKind.Sequential)]
        public struct COMMTIMEOUTS
        {
            public uint ReadIntervalTimeout;
            public uint ReadTotalTimeoutMultiplier;
            public uint ReadTotalTimeoutConstant;
            public uint WriteTotalTimeoutMultiplier;
            public uint WriteTotalTimeoutConstant;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
            IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetCommState(SafeFileHandle hFile, ref DCB dcb);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetCommState(SafeFileHandle hFile, ref DCB dcb);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetCommTimeouts(SafeFileHandle hFile, ref COMMTIMEOUTS timeouts);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetupComm(SafeFileHandle hFile, uint inQueue, uint outQueue);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool PurgeComm(SafeFileHandle hFile, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle hFile, byte[] buffer, uint bytesToRead,
            out uint bytesRead, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(SafeFileHandle hFile, byte[] buffer, uint bytesToWrite,
            out uint bytesWritten, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelIoEx(SafeFileHandle hFile, IntPtr overlapped);

        // ---------------------------------------------------------------- port enumeration (setupapi)

        public static readonly Guid GUID_DEVCLASS_PORTS = new Guid("4d36e978-e325-11ce-bfc1-08002be10318");
        public const int DIGCF_PRESENT = 0x02;
        public const int SPDRP_FRIENDLYNAME = 0x0C;
        public const int DICS_FLAG_GLOBAL = 1;
        public const int DIREG_DEV = 1;
        public const int KEY_READ = 0x20019;
        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
            int property, out int propertyRegDataType, byte[] propertyBuffer, int propertyBufferSize, out int requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern IntPtr SetupDiOpenDevRegKey(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
            int scope, int hwProfile, int keyType, int samDesired);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        // ---------------------------------------------------------------- window / edit control (user32)

        public const int WM_SETREDRAW = 0x000B;
        public const int WM_VSCROLL = 0x0115;
        public const int WM_DEVICECHANGE = 0x0219;
        public const int SB_BOTTOM = 7;
        public const int EM_SETSEL = 0x00B1;
        public const int EM_LINESCROLL = 0x00B6;
        public const int EM_REPLACESEL = 0x00C2;
        public const int EM_GETFIRSTVISIBLELINE = 0x00CE;
        public const int EM_SETMARGINS = 0x00D3;
        public const int EM_SETCUEBANNER = 0x1501;
        public const int EC_LEFTMARGIN = 0x0001;
        public const int EC_RIGHTMARGIN = 0x0002;

        public const uint RDW_INVALIDATE = 0x0001;
        public const uint RDW_ERASE = 0x0004;
        public const uint RDW_FRAME = 0x0400;

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll")]
        public static extern bool RedrawWindow(IntPtr hWnd, IntPtr rcUpdate, IntPtr hrgnUpdate, uint flags);
    }
}
