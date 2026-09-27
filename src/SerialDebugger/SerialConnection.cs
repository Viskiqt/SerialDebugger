using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace SerialDebugger
{
    /// <summary>
    /// An open COM port using the Win32 serial API directly, read and written on two dedicated background
    /// threads. The UI thread never waits on the port. If the device disappears, <see cref="Failed"/> is
    /// raised instead of an exception, so unplugging a board can never crash or freeze the app.
    /// </summary>
    internal sealed class SerialConnection : IDisposable
    {
        private readonly SafeFileHandle _handle;
        private readonly BlockingCollection<byte[]> _sendQueue = new BlockingCollection<byte[]>();
        private Thread _thread;
        private Thread _writer;
        private volatile bool _stopping;
        private long _bytesReceived;
        private long _bytesSent;

        private SerialConnection(string portName, int baudRate, SafeFileHandle handle)
        {
            PortName = portName;
            BaudRate = baudRate;
            _handle = handle;
        }

        public string PortName { get; }
        public int BaudRate { get; }
        public long BytesReceived => Interlocked.Read(ref _bytesReceived);
        public long BytesSent => Interlocked.Read(ref _bytesSent);

        /// <summary>Raised on the reader thread with text ready for display.</summary>
        public event Action<string> TextReceived;

        /// <summary>Raised on the reader thread when the port stops working (e.g. device unplugged).</summary>
        public event Action<string> Failed;

        /// <summary>Opens the port (8 data bits, no parity, 1 stop bit, no flow control).</summary>
        /// <exception cref="IOException">With a message suitable for showing to the user.</exception>
        public static SerialConnection Open(string portName, int baudRate)
        {
            SafeFileHandle handle = NativeMethods.CreateFile(@"\\.\" + portName,
                NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE, 0, IntPtr.Zero,
                NativeMethods.OPEN_EXISTING, NativeMethods.FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);

            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new IOException(DescribeOpenError(portName, error));
            }

            try
            {
                // Ask the driver for a large receive buffer so fast bursts are never lost.
                NativeMethods.SetupComm(handle, 64 * 1024, 4 * 1024);

                var dcb = new NativeMethods.DCB { DCBlength = (uint)Marshal.SizeOf(typeof(NativeMethods.DCB)) };
                if (!NativeMethods.GetCommState(handle, ref dcb))
                    throw Win32Failure($"Could not read the settings of {portName}");

                dcb.BaudRate = (uint)baudRate;
                dcb.ByteSize = 8;
                dcb.Parity = 0;     // no parity
                dcb.StopBits = 0;   // 1 stop bit
                dcb.Flags = NativeMethods.DCB_FLAGS_BINARY_DTR_RTS;
                if (!NativeMethods.SetCommState(handle, ref dcb))
                    throw Win32Failure($"{portName} does not accept {baudRate:N0} baud");

                // ReadFile returns as soon as any data is available, or after 50 ms with nothing.
                // (Windows runs one read or write at a time on this port, so this also bounds send latency.)
                var timeouts = new NativeMethods.COMMTIMEOUTS
                {
                    ReadIntervalTimeout = NativeMethods.MAXDWORD,
                    ReadTotalTimeoutMultiplier = NativeMethods.MAXDWORD,
                    ReadTotalTimeoutConstant = 50,
                    WriteTotalTimeoutConstant = 1000,
                };
                if (!NativeMethods.SetCommTimeouts(handle, ref timeouts))
                    throw Win32Failure($"Could not configure {portName}");

                NativeMethods.PurgeComm(handle, NativeMethods.PURGE_RXCLEAR | NativeMethods.PURGE_TXCLEAR);
            }
            catch
            {
                handle.Dispose();
                throw;
            }

            return new SerialConnection(portName, baudRate, handle);
        }

        /// <summary>Starts reading and sending. Subscribe to the events first.</summary>
        public void Start()
        {
            _thread = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = "Serial reader " + PortName,
            };
            _writer = new Thread(WriteLoop)
            {
                IsBackground = true,
                Name = "Serial writer " + PortName,
            };
            _thread.Start();
            _writer.Start();
        }

        /// <summary>Queues bytes to send and returns immediately.</summary>
        public void Send(byte[] data)
        {
            if (_stopping || data == null || data.Length == 0)
                return;
            try
            {
                _sendQueue.Add(data);
            }
            catch (InvalidOperationException)
            {
                // Closing: nothing more is sent.
            }
        }

        private void WriteLoop()
        {
            try
            {
                foreach (byte[] data in _sendQueue.GetConsumingEnumerable())
                {
                    int offset = 0;
                    while (offset < data.Length && !_stopping)
                    {
                        byte[] chunk = offset == 0 ? data : Copy(data, offset);
                        if (!NativeMethods.WriteFile(_handle, chunk, (uint)chunk.Length, out uint written, IntPtr.Zero) || written == 0)
                            break;   // device gone or not accepting data; the reader reports lost connections
                        offset += (int)written;
                        Interlocked.Add(ref _bytesSent, written);
                    }
                    if (_stopping)
                        return;
                }
            }
            catch (ObjectDisposedException)
            {
                // Port closed while stopping.
            }
        }

        private static byte[] Copy(byte[] data, int offset)
        {
            var rest = new byte[data.Length - offset];
            Buffer.BlockCopy(data, offset, rest, 0, rest.Length);
            return rest;
        }

        private void ReadLoop()
        {
            var buffer = new byte[64 * 1024];
            var normalizer = new TextNormalizer();

            try
            {
                while (!_stopping)
                {
                    if (!NativeMethods.ReadFile(_handle, buffer, (uint)buffer.Length, out uint read, IntPtr.Zero))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (!_stopping)
                            Failed?.Invoke($"Lost connection to {PortName} ({new Win32Exception(error).Message.TrimEnd('.')}). " +
                                           "The device may have been unplugged or reset.");
                        return;
                    }

                    if (read == 0)
                        continue;   // no data within 100 ms

                    Interlocked.Add(ref _bytesReceived, read);
                    string text = normalizer.Process(buffer, (int)read);
                    if (text.Length > 0)
                        TextReceived?.Invoke(text);
                }
            }
            catch (ObjectDisposedException)
            {
                // Port closed while stopping.
            }
            catch (Exception ex)
            {
                if (!_stopping)
                    Failed?.Invoke($"Lost connection to {PortName}: {ex.Message}");
            }
            finally
            {
                _handle.Dispose();
            }
        }

        /// <summary>Stops reading and closes the port. Waits at most about a second.</summary>
        public void Dispose()
        {
            if (_stopping)
                return;
            _stopping = true;
            _sendQueue.CompleteAdding();

            if (_thread == null)
            {
                _handle.Dispose();
                return;
            }

            try
            {
                NativeMethods.CancelIoEx(_handle, IntPtr.Zero);   // wake the reader and writer immediately
            }
            catch (ObjectDisposedException)
            {
                // Reader already closed the port.
            }

            // Both normally finish within a few ms; never block forever.
            if (Thread.CurrentThread != _thread)
                _thread.Join(1000);
            if (_writer != null && Thread.CurrentThread != _writer)
                _writer.Join(500);
        }

        private static string DescribeOpenError(string portName, int error)
        {
            switch (error)
            {
                case NativeMethods.ERROR_FILE_NOT_FOUND:
                case NativeMethods.ERROR_PATH_NOT_FOUND:
                    return $"{portName} was not found. Check that the device is plugged in, then click Refresh.";
                case NativeMethods.ERROR_ACCESS_DENIED:
                    return $"{portName} is already in use by another program.\n\n" +
                           "Close any other terminal or tool that has it open, then try again.";
                default:
                    return $"Could not open {portName}: {new Win32Exception(error).Message}";
            }
        }

        private static IOException Win32Failure(string what)
        {
            int error = Marshal.GetLastWin32Error();
            return new IOException($"{what}: {new Win32Exception(error).Message}");
        }
    }
}
