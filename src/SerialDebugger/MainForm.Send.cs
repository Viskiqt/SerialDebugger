using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using static SerialDebugger.NativeMethods;

namespace SerialDebugger
{
    /// <summary>Main window: typing and sending text to the device.</summary>
    internal sealed partial class MainForm
    {
        private const int MaxHistory = 50;

        private static readonly object[] LineEndingNames = { "No line ending", "LF  (\\n)", "CR  (\\r)", "CR+LF  (\\r\\n)" };
        private static readonly string[] LineEndings = { "", "\n", "\r", "\r\n" };

        private readonly List<string> _history = new List<string>();
        private int _historyIndex;   // == _history.Count when not browsing
        private long _totalTxBytes;
        private long _connectionTxSeen;

        private void InitializeSend()
        {
            _lineEndingBox.SelectedIndex = Math.Max(0, Math.Min(LineEndings.Length - 1, _settings.LineEnding));

            _sendBox.HandleCreated += (sender, e) =>
                SendMessage(_sendBox.Handle, EM_SETCUEBANNER, (IntPtr)1, "Type a message and press Enter to send");
            _sendBox.KeyDown += SendBox_KeyDown;
            _sendButton.Click += (sender, e) => SendText();
        }

        private void SendBox_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Enter:
                    SendText();
                    e.SuppressKeyPress = true;   // no "ding"
                    break;
                case Keys.Up:
                    ShowHistory(-1);
                    e.SuppressKeyPress = true;
                    break;
                case Keys.Down:
                    ShowHistory(+1);
                    e.SuppressKeyPress = true;
                    break;
            }
        }

        private void SendText()
        {
            if (_connection == null)
                return;

            string text = _sendBox.Text;
            string ending = LineEndings[Math.Max(0, _lineEndingBox.SelectedIndex)];
            if (text.Length == 0 && ending.Length == 0)
                return;

            _connection.Send(Encoding.UTF8.GetBytes(text + ending));

            if (text.Length > 0 && (_history.Count == 0 || _history[_history.Count - 1] != text))
            {
                _history.Add(text);
                if (_history.Count > MaxHistory)
                    _history.RemoveAt(0);
            }
            _historyIndex = _history.Count;
            _sendBox.Clear();
            _sendBox.Focus();
        }

        /// <summary>Up/Down arrows step through previously sent messages, like a command prompt.</summary>
        private void ShowHistory(int step)
        {
            if (_history.Count == 0)
                return;

            _historyIndex = Math.Max(0, Math.Min(_history.Count, _historyIndex + step));
            _sendBox.Text = _historyIndex < _history.Count ? _history[_historyIndex] : "";
            _sendBox.SelectionStart = _sendBox.TextLength;
        }
    }
}
