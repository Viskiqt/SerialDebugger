using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SerialDebugger
{
    /// <summary>Main window: connecting, disconnecting, and reacting to devices being plugged in or out.</summary>
    internal sealed partial class MainForm
    {
        private readonly Timer _reconnectTimer = new Timer { Interval = 1000 };
        private readonly Timer _deviceChangeTimer = new Timer { Interval = 400 };
        private SerialConnection _connection;
        private string _reconnectPort;     // set while waiting for an unplugged device to come back
        private int _reconnectBaud;
        private string _lostReason;

        private string SelectedPortName => (_portBox.SelectedItem as PortInfo)?.Name;

        private void InitializeBehaviour()
        {
            InitializeTerminal();
            InitializeSend();

            _autoScrollBox.Checked = _settings.AutoScroll;
            _baudBox.Text = _settings.BaudRate.ToString(CultureInfo.InvariantCulture);
            RefreshPorts(_settings.LastPort);
            RestoreWindowBounds();

            _refreshButton.Click += (sender, e) => RefreshPorts(SelectedPortName);
            _connectButton.Click += (sender, e) => ToggleConnection();
            _portBox.DropDown += (sender, e) => FitDropDownWidth();
            _reconnectTimer.Tick += (sender, e) => TryReconnect();
            _deviceChangeTimer.Tick += (sender, e) =>
            {
                _deviceChangeTimer.Stop();
                OnDevicesChanged();
            };
        }

        private void ToggleConnection()
        {
            if (_connection != null || _reconnectPort != null)
            {
                Disconnect();
                return;
            }

            string port = SelectedPortName;
            if (port == null)
            {
                MessageBox.Show(this, "No COM port is selected. Plug in your device, then click Refresh.",
                    "Serial Debugger", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!TryGetBaudRate(out int baud))
            {
                MessageBox.Show(this, "Please enter a valid baud rate, for example 115200.",
                    "Serial Debugger", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _baudBox.Focus();
                return;
            }

            Connect(port, baud, showErrors: true);
        }

        private bool Connect(string port, int baud, bool showErrors)
        {
            SerialConnection connection;
            try
            {
                connection = SerialConnection.Open(port, baud);
            }
            catch (IOException ex)
            {
                if (showErrors)
                {
                    SetState(Theme.Error, ex.Message.Split('\n')[0], ex.Message);
                    MessageBox.Show(this, ex.Message, "Serial Debugger", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return false;
            }

            connection.TextReceived += OnTextReceived;
            connection.Failed += reason => RunOnUiThread(() => OnConnectionLost(connection, reason));

            _connection = connection;
            _connectionBytesSeen = 0;
            _connectionTxSeen = 0;
            _reconnectPort = null;
            _reconnectTimer.Stop();
            _settings.LastPort = port;
            _settings.BaudRate = baud;

            connection.Start();
            UpdateUiState();
            if (showErrors)
                _sendBox.Focus();   // ready to type straight away
            return true;
        }

        private void Disconnect()
        {
            _reconnectPort = null;
            _reconnectTimer.Stop();
            CloseConnection();
            UpdateUiState();
        }

        private void CloseConnection()
        {
            SerialConnection connection = _connection;
            if (connection == null)
                return;

            UpdateByteCounter();   // count the last bytes before letting go of the connection
            _connection = null;
            connection.TextReceived -= OnTextReceived;
            connection.Dispose();
            FlushPending();
        }

        /// <summary>The device stopped responding or was unplugged: wait for it to come back.</summary>
        private void OnConnectionLost(SerialConnection connection, string reason)
        {
            if (connection != _connection)
                return;   // already closed on purpose

            string port = connection.PortName;
            int baud = connection.BaudRate;
            CloseConnection();

            _reconnectPort = port;
            _reconnectBaud = baud;
            _lostReason = reason;
            _reconnectTimer.Start();
            RefreshPorts(port);
        }

        private void TryReconnect()
        {
            if (_reconnectPort == null)
            {
                _reconnectTimer.Stop();
                return;
            }

            // Not back yet, or its driver is still starting: try again on the next tick.
            if (PortInfo.Exists(_reconnectPort) && Connect(_reconnectPort, _reconnectBaud, showErrors: false))
                RefreshPorts(_connection?.PortName);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            // A device was plugged in or removed. Several of these arrive at once, so wait briefly.
            if (m.Msg == NativeMethods.WM_DEVICECHANGE)
            {
                _deviceChangeTimer.Stop();
                _deviceChangeTimer.Start();
            }
        }

        private void OnDevicesChanged()
        {
            if (_connection != null && !PortInfo.Exists(_connection.PortName))
                OnConnectionLost(_connection, $"{_connection.PortName} was removed.");
            else if (_reconnectPort != null)
                TryReconnect();

            RefreshPorts(_connection?.PortName ?? _reconnectPort ?? SelectedPortName);
        }

        private void RefreshPorts(string portToSelect)
        {
            List<PortInfo> ports;
            try
            {
                ports = PortInfo.GetPorts();
            }
            catch (Exception)
            {
                ports = new List<PortInfo>();
            }

            // Keep showing the port we are waiting for, even while it is unplugged.
            if (_reconnectPort != null && !ports.Any(p => SamePort(p.Name, _reconnectPort)))
                ports.Insert(0, new PortInfo(_reconnectPort, "waiting for device…"));

            _portBox.BeginUpdate();
            _portBox.Items.Clear();
            _portBox.Items.AddRange(ports.Cast<object>().ToArray());
            int index = ports.FindIndex(p => SamePort(p.Name, portToSelect));
            _portBox.SelectedIndex = index >= 0 ? index : (ports.Count > 0 ? 0 : -1);
            _portBox.EndUpdate();

            UpdateUiState();
        }

        private void UpdateUiState()
        {
            bool busy = _connection != null || _reconnectPort != null;
            _portBox.Enabled = !busy;
            _baudBox.Enabled = !busy;
            _refreshButton.Enabled = !busy;

            _sendButton.Enabled = _connection != null;
            if (_connection != null)
                Theme.StylePrimary(_sendButton);
            else
                Theme.StyleDisabled(_sendButton);

            if (_connection != null)
            {
                _connectButton.Text = "Disconnect";
                Theme.StyleSecondary(_connectButton);
                SetState(Theme.Success, $"Connected to {_connection.PortName} at {_connection.BaudRate:N0} baud");
            }
            else if (_reconnectPort != null)
            {
                _connectButton.Text = "Stop";
                Theme.StyleSecondary(_connectButton);
                SetState(Theme.Warning, $"Connection to {_reconnectPort} lost — reconnecting automatically when it is back…", _lostReason);
            }
            else
            {
                _connectButton.Text = "Connect";
                bool canConnect = _portBox.Items.Count > 0;
                _connectButton.Enabled = canConnect;
                if (canConnect)
                    Theme.StylePrimary(_connectButton);
                else
                    Theme.StyleDisabled(_connectButton);
                SetState(Theme.Neutral, canConnect ? "Not connected" : "No COM ports found — plug in your device");
                return;
            }

            _connectButton.Enabled = true;
        }

        private void SetState(Color dotColor, string text, string details = null)
        {
            _stateDot.ForeColor = dotColor;
            _stateLabel.Text = text;
            _stateLabel.ToolTipText = details ?? text;
        }

        private bool TryGetBaudRate(out int baud)
        {
            string text = _baudBox.Text.Replace(",", "").Replace(".", "").Replace(" ", "");
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out baud) && baud >= 50 && baud <= 20_000_000;
        }

        private static bool SamePort(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>Makes the drop-down list wide enough for long device names.</summary>
        private void FitDropDownWidth()
        {
            int width = _portBox.Width;
            foreach (object item in _portBox.Items)
                width = Math.Max(width, TextRenderer.MeasureText(item.ToString(), _portBox.Font).Width + SystemInformation.VerticalScrollBarWidth + 12);
            _portBox.DropDownWidth = width;
        }

        /// <summary>Runs an action on the UI thread; ignored while the window is closing.</summary>
        private void RunOnUiThread(Action action)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // Window is closing.
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.L:
                    ClearTerminal();
                    return true;
                case Keys.F5:
                    if (_refreshButton.Enabled)
                        RefreshPorts(SelectedPortName);
                    return true;
                case Keys.Control | Keys.A:
                    if (_terminal.Focused)
                    {
                        _terminal.SelectAll();
                        return true;
                    }
                    if (_sendBox.Focused)
                    {
                        _sendBox.SelectAll();
                        return true;
                    }
                    break;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void RestoreWindowBounds()
        {
            if (_settings.WindowBounds is Rectangle bounds &&
                Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds)) &&
                bounds.Width >= MinimumSize.Width && bounds.Height >= MinimumSize.Height)
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = bounds;
            }

            if (_settings.Maximized)
                WindowState = FormWindowState.Maximized;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _flushTimer.Stop();
            _reconnectTimer.Stop();
            _deviceChangeTimer.Stop();

            _settings.AutoScroll = _autoScrollBox.Checked;
            _settings.LineEnding = _lineEndingBox.SelectedIndex;
            if (TryGetBaudRate(out int baud))
                _settings.BaudRate = baud;
            _settings.LastPort = _connection?.PortName ?? _reconnectPort ?? SelectedPortName ?? _settings.LastPort;
            _settings.Maximized = WindowState == FormWindowState.Maximized;
            _settings.WindowBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _settings.Save();

            _reconnectPort = null;
            CloseConnection();
            base.OnFormClosing(e);
        }
    }
}
