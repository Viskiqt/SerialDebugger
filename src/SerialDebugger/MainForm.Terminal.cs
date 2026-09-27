using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using static SerialDebugger.NativeMethods;

namespace SerialDebugger
{
    /// <summary>Main window: the output area.</summary>
    internal sealed partial class MainForm
    {
        // Keep the newest ~1 million characters (roughly 15-20k lines) so the window stays fast forever.
        private const int MaxTerminalChars = 1_000_000;
        private const int TrimToChars = 750_000;

        private readonly object _pendingLock = new object();
        private readonly StringBuilder _pending = new StringBuilder();
        private readonly Timer _flushTimer = new Timer { Interval = 40 };
        private readonly Timer _copiedTimer = new Timer { Interval = 1200 };
        private readonly ContextMenuStrip _terminalMenu = new ContextMenuStrip();
        private long _totalBytes;
        private long _connectionBytesSeen;
        private long _shownBytes = -1;
        private long _shownTxBytes = -1;

        private void InitializeTerminal()
        {
            _terminal.HandleCreated += (sender, e) => SetTerminalMargins();
            _terminal.DpiChangedAfterParent += (sender, e) => SetTerminalMargins();

            var copyItem = new ToolStripMenuItem("Copy", null, (sender, e) => CopyText()) { ShortcutKeyDisplayString = "Ctrl+C" };
            var selectAllItem = new ToolStripMenuItem("Select all", null, (sender, e) => _terminal.SelectAll()) { ShortcutKeyDisplayString = "Ctrl+A" };
            var clearItem = new ToolStripMenuItem("Clear", null, (sender, e) => ClearTerminal()) { ShortcutKeyDisplayString = "Ctrl+L" };
            _terminalMenu.Items.AddRange(new ToolStripItem[] { copyItem, selectAllItem, new ToolStripSeparator(), clearItem });
            _terminalMenu.Opening += (sender, e) => copyItem.Enabled = _terminal.SelectionLength > 0;
            _terminal.ContextMenuStrip = _terminalMenu;

            _copyButton.Click += (sender, e) => CopyText();
            _clearButton.Click += (sender, e) => ClearTerminal();
            _autoScrollBox.CheckedChanged += (sender, e) =>
            {
                if (_autoScrollBox.Checked)
                    ScrollToBottom();
            };

            _copiedTimer.Tick += (sender, e) =>
            {
                _copiedTimer.Stop();
                _copyButton.Text = "Copy";
            };

            _flushTimer.Tick += (sender, e) =>
            {
                FlushPending();
                UpdateByteCounter();
            };
            _flushTimer.Start();
        }

        /// <summary>Called on the serial reader thread. Only queues the text; the UI timer displays it.</summary>
        private void OnTextReceived(string text)
        {
            lock (_pendingLock)
            {
                _pending.Append(text);

                // If the window can't show text right now (e.g. the user is dragging a selection),
                // keep only the newest output, cut at a line break.
                if (_pending.Length > MaxTerminalChars)
                {
                    string all = _pending.ToString();
                    int lineEnd = all.IndexOf('\n', all.Length - TrimToChars);
                    _pending.Clear();
                    _pending.Append(lineEnd >= 0 ? all.Substring(lineEnd + 1) : all.Substring(all.Length - TrimToChars));
                }
            }
        }

        private void FlushPending()
        {
            // While the user is dragging a selection with the mouse, wait: text arriving
            // mid-drag would disturb the selection.
            if (!_terminal.IsHandleCreated || _terminal.Capture)
                return;

            string text;
            lock (_pendingLock)
            {
                if (_pending.Length == 0)
                    return;
                text = _pending.ToString();
                _pending.Clear();
            }

            AppendToTerminal(text);
        }

        /// <summary>
        /// Adds text at the end without disturbing the user: their selection is kept, and when
        /// auto-scroll is off the view stays exactly where they scrolled to.
        /// </summary>
        private void AppendToTerminal(string text)
        {
            IntPtr hwnd = _terminal.Handle;
            bool autoScroll = _autoScrollBox.Checked;
            int selStart = _terminal.SelectionStart;
            int selLength = _terminal.SelectionLength;
            int firstVisibleLine = (int)SendMessage(hwnd, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);

            SendMessage(hwnd, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            try
            {
                int length = _terminal.TextLength;
                int removedChars = 0;
                int removedLines = 0;

                // Drop the oldest lines when the output gets too long.
                if (length + text.Length > MaxTerminalChars)
                {
                    int mustRemove = Math.Min(length, length + text.Length - TrimToChars);
                    string current = _terminal.Text;
                    int lineEnd = current.IndexOf('\n', Math.Max(0, mustRemove - 1));
                    removedChars = lineEnd >= 0 ? lineEnd + 1 : length;
                    removedLines = _terminal.GetLineFromCharIndex(removedChars);

                    SetSelection(hwnd, 0, removedChars);
                    SendMessage(hwnd, EM_REPLACESEL, IntPtr.Zero, "");
                    length -= removedChars;
                }

                SetSelection(hwnd, length, length);
                SendMessage(hwnd, EM_REPLACESEL, IntPtr.Zero, text);

                // Put the user's selection (or cursor) back where it was.
                int newStart = selStart - removedChars;
                int newEnd = selStart + selLength - removedChars;
                if (selLength > 0 && newEnd > 0)
                    SetSelection(hwnd, Math.Max(0, newStart), newEnd);
                else if (!autoScroll)
                    SetSelection(hwnd, Math.Max(0, newStart), Math.Max(0, newStart));

                if (autoScroll)
                {
                    SendMessage(hwnd, WM_VSCROLL, (IntPtr)SB_BOTTOM, IntPtr.Zero);
                }
                else
                {
                    int target = Math.Max(0, firstVisibleLine - removedLines);
                    int now = (int)SendMessage(hwnd, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
                    if (now != target)
                        SendMessage(hwnd, EM_LINESCROLL, IntPtr.Zero, (IntPtr)(target - now));
                }
            }
            finally
            {
                SendMessage(hwnd, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero, RDW_ERASE | RDW_FRAME | RDW_INVALIDATE);
            }
        }

        private void ScrollToBottom()
        {
            if (_terminal.IsHandleCreated)
                SendMessage(_terminal.Handle, WM_VSCROLL, (IntPtr)SB_BOTTOM, IntPtr.Zero);
        }

        private void ClearTerminal()
        {
            lock (_pendingLock)
                _pending.Clear();
            _terminal.Clear();
            _totalBytes = 0;
            _totalTxBytes = 0;
            UpdateByteCounter();
        }

        /// <summary>Copies the selection, or all output when nothing is selected.</summary>
        private void CopyText()
        {
            string text = _terminal.SelectionLength > 0 ? _terminal.SelectedText : _terminal.Text;
            if (text.Length == 0)
                return;

            try
            {
                Clipboard.SetDataObject(text, true, 5, 100);   // retries if another app holds the clipboard
                _copyButton.Text = "Copied ✓";
                _copiedTimer.Stop();
                _copiedTimer.Start();
            }
            catch (ExternalException)
            {
                MessageBox.Show(this, "The clipboard is busy. Please try again.", "Serial Debugger",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>Adds bytes received/sent by the current connection to the running totals and shows them.</summary>
        private void UpdateByteCounter()
        {
            if (_connection != null)
            {
                long seen = _connection.BytesReceived;
                _totalBytes += seen - _connectionBytesSeen;
                _connectionBytesSeen = seen;

                long sent = _connection.BytesSent;
                _totalTxBytes += sent - _connectionTxSeen;
                _connectionTxSeen = sent;
            }

            if (_totalBytes != _shownBytes || _totalTxBytes != _shownTxBytes)
            {
                _shownBytes = _totalBytes;
                _shownTxBytes = _totalTxBytes;
                _bytesLabel.Text = $"RX  {_totalBytes:N0} bytes     TX  {_totalTxBytes:N0} bytes";
            }
        }

        private void SetTerminalMargins()
        {
            int left = _terminal.LogicalToDeviceUnits(10);
            int right = _terminal.LogicalToDeviceUnits(6);
            SendMessage(_terminal.Handle, EM_SETMARGINS, (IntPtr)(EC_LEFTMARGIN | EC_RIGHTMARGIN), (IntPtr)(left | (right << 16)));
        }

        private static void SetSelection(IntPtr hwnd, int start, int end) =>
            SendMessage(hwnd, EM_SETSEL, (IntPtr)start, (IntPtr)end);
    }
}
