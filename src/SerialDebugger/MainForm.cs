using System;
using System.Drawing;
using System.Windows.Forms;

namespace SerialDebugger
{
    /// <summary>Main window: layout. Behaviour is in MainForm.Terminal.cs and MainForm.Connection.cs.</summary>
    internal sealed partial class MainForm : Form
    {
        private static readonly int[] CommonBaudRates =
            { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600, 1000000, 2000000 };

        private readonly ComboBox _portBox = new ComboBox();
        private readonly Button _refreshButton = new Button();
        private readonly ComboBox _baudBox = new ComboBox();
        private readonly Button _connectButton = new Button();
        private readonly TextBox _terminal = new TextBox();
        private readonly TextBox _sendBox = new TextBox();
        private readonly ComboBox _lineEndingBox = new ComboBox();
        private readonly Button _sendButton = new Button();
        private readonly CheckBox _autoScrollBox = new CheckBox();
        private readonly Button _copyButton = new Button();
        private readonly Button _clearButton = new Button();
        private readonly StatusStrip _statusStrip = new StatusStrip();
        private readonly ToolStripStatusLabel _stateDot = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel _stateLabel = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel _bytesLabel = new ToolStripStatusLabel();
        private readonly ToolTip _toolTip = new ToolTip();
        private readonly AppSettings _settings = AppSettings.Load();

        public MainForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            Text = "Serial Debugger";
            Icon = LoadAppIcon();
            Font = Theme.UiFont;
            BackColor = Theme.WindowBack;
            ForeColor = Theme.Text;
            ClientSize = new Size(980, 640);
            MinimumSize = new Size(720, 420);
            StartPosition = FormStartPosition.CenterScreen;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(14, 12, 14, 10),
                BackColor = Theme.WindowBack,
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(BuildToolbar(), 0, 0);
            root.Controls.Add(BuildTerminal(), 0, 1);
            root.Controls.Add(BuildSendBar(), 0, 2);
            root.Controls.Add(BuildActionBar(), 0, 3);

            BuildStatusBar();
            Controls.Add(root);
            Controls.Add(_statusStrip);

            ResumeLayout(false);
            PerformLayout();

            InitializeBehaviour();
            ActiveControl = _sendBox;   // ready to type as soon as the window opens
        }

        private Control BuildToolbar()
        {
            var bar = NewRow(8, new Padding(0, 0, 0, 10));

            _portBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _portBox.Width = 340;
            _portBox.MaxDropDownItems = 14;
            _portBox.Margin = new Padding(8, 0, 0, 0);

            _refreshButton.Text = Theme.RefreshGlyph;
            _refreshButton.Font = Theme.IconFont;
            _refreshButton.Size = new Size(34, 28);
            _refreshButton.Margin = new Padding(6, 0, 0, 0);
            Theme.StyleSecondary(_refreshButton);
            _toolTip.SetToolTip(_refreshButton, "Refresh the list of COM ports (F5)");

            _baudBox.DropDownStyle = ComboBoxStyle.DropDown;
            _baudBox.Width = 110;
            _baudBox.Margin = new Padding(8, 0, 0, 0);
            foreach (int rate in CommonBaudRates)
                _baudBox.Items.Add(rate.ToString());
            _toolTip.SetToolTip(_baudBox, "Must match the speed your device uses (STM32 projects usually use 115200)");

            _connectButton.Text = "Connect";
            _connectButton.Size = new Size(116, 30);
            _connectButton.Margin = new Padding(16, 0, 0, 0);
            Theme.StylePrimary(_connectButton);

            bar.Controls.Add(NewLabel("Port"), 0, 0);
            bar.Controls.Add(_portBox, 1, 0);
            bar.Controls.Add(_refreshButton, 2, 0);
            bar.Controls.Add(NewLabel("Baud rate", new Padding(22, 0, 0, 0)), 3, 0);
            bar.Controls.Add(_baudBox, 4, 0);
            bar.Controls.Add(_connectButton, 5, 0);
            return bar;
        }

        private Control BuildTerminal()
        {
            // 1 px border around a white "card" that holds the text box.
            var border = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1),
                Margin = new Padding(0),
                BackColor = Theme.Border,
            };
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 6, 0, 6),
                BackColor = Theme.Surface,
            };

            _terminal.Dock = DockStyle.Fill;
            _terminal.Multiline = true;
            _terminal.ReadOnly = true;
            _terminal.WordWrap = true;
            _terminal.ScrollBars = ScrollBars.Vertical;
            _terminal.BorderStyle = BorderStyle.None;
            _terminal.BackColor = Theme.Surface;
            _terminal.ForeColor = Theme.Text;
            _terminal.Font = Theme.TerminalFont;
            _terminal.MaxLength = 0;          // no 32 KB limit
            _terminal.HideSelection = false;  // keep the selection visible when clicking Copy

            card.Controls.Add(_terminal);
            border.Controls.Add(card);
            return border;
        }

        private Control BuildSendBar()
        {
            var bar = NewRow(3, new Padding(0, 10, 0, 0));

            _sendBox.Font = Theme.TerminalFont;
            _sendBox.BorderStyle = BorderStyle.FixedSingle;
            _sendBox.Margin = new Padding(0);
            _toolTip.SetToolTip(_sendBox, "Press Enter to send. Up / Down arrows bring back earlier messages.");

            _lineEndingBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _lineEndingBox.Width = 130;
            _lineEndingBox.Margin = new Padding(8, 0, 0, 0);
            _lineEndingBox.Items.AddRange(LineEndingNames);
            _toolTip.SetToolTip(_lineEndingBox, "What is added after your text when it is sent (most devices expect a line ending)");

            _sendButton.Text = "Send";
            _sendButton.Size = new Size(92, 30);
            _sendButton.Margin = new Padding(8, 0, 0, 0);
            Theme.StylePrimary(_sendButton);

            // Column 0 (the text box) takes the spare width.
            bar.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 100F);
            bar.ColumnStyles[bar.ColumnCount - 1] = new ColumnStyle(SizeType.AutoSize);

            bar.Controls.Add(_sendBox, 0, 0);
            bar.Controls.Add(_lineEndingBox, 1, 0);
            bar.Controls.Add(_sendButton, 2, 0);
            _sendBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;   // stretch
            return bar;
        }

        private Control BuildActionBar()
        {
            var bar = NewRow(4, new Padding(0, 10, 0, 0));

            _autoScrollBox.Text = "Auto-scroll";
            _autoScrollBox.AutoSize = true;
            _autoScrollBox.Checked = true;
            _autoScrollBox.Cursor = Cursors.Hand;
            _toolTip.SetToolTip(_autoScrollBox, "Keep the newest output in view. Untick to scroll through the output yourself.");

            _copyButton.Text = "Copy";
            _copyButton.Size = new Size(92, 30);
            Theme.StyleSecondary(_copyButton);
            _toolTip.SetToolTip(_copyButton, "Copy the selected text, or everything if nothing is selected (Ctrl+C)");

            _clearButton.Text = "Clear";
            _clearButton.Size = new Size(92, 30);
            _clearButton.Margin = new Padding(8, 0, 0, 0);
            Theme.StyleSecondary(_clearButton);
            _toolTip.SetToolTip(_clearButton, "Clear the output (Ctrl+L)");

            // Column 1 is the stretching gap between the checkbox and the buttons.
            bar.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 100F);
            bar.ColumnStyles[bar.ColumnCount - 1] = new ColumnStyle(SizeType.AutoSize);

            bar.Controls.Add(_autoScrollBox, 0, 0);
            bar.Controls.Add(_copyButton, 2, 0);
            bar.Controls.Add(_clearButton, 3, 0);
            return bar;
        }

        private void BuildStatusBar()
        {
            _statusStrip.SizingGrip = false;
            _statusStrip.Font = Theme.UiFont;
            _statusStrip.BackColor = Theme.Surface;
            _statusStrip.Padding = new Padding(10, 3, 14, 3);
            _statusStrip.ShowItemToolTips = true;
            _statusStrip.Renderer = new FlatStatusRenderer();

            _stateDot.Text = "●";   // ●
            _stateDot.Margin = new Padding(0, 3, 0, 2);

            _stateLabel.Spring = true;
            _stateLabel.TextAlign = ContentAlignment.MiddleLeft;
            _stateLabel.ForeColor = Theme.Text;

            _bytesLabel.ForeColor = Theme.TextSecondary;

            _statusStrip.Items.AddRange(new ToolStripItem[] { _stateDot, _stateLabel, _bytesLabel });
        }

        /// <summary>One-row table whose last column absorbs the spare width.</summary>
        private static TableLayoutPanel NewRow(int columns, Padding margin)
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = columns,
                RowCount = 1,
                Margin = margin,
                Padding = new Padding(0),
                BackColor = Theme.WindowBack,
            };
            for (int i = 0; i < columns; i++)
                row.ColumnStyles.Add(i == columns - 1 ? new ColumnStyle(SizeType.Percent, 100F) : new ColumnStyle(SizeType.AutoSize));
            row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            row.ControlAdded += (sender, e) => e.Control.Anchor = AnchorStyles.Left;   // centre vertically
            return row;
        }

        private static Label NewLabel(string text, Padding? margin = null) => new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = Theme.TextSecondary,
            Margin = margin ?? new Padding(0),
        };

        private static Icon LoadAppIcon()
        {
            try
            {
                using (var stream = typeof(MainForm).Assembly.GetManifestResourceStream("SerialDebugger.App.ico"))
                    return stream == null ? null : new Icon(stream);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Flat white status bar with a thin line on top.</summary>
        private sealed class FlatStatusRenderer : ToolStripProfessionalRenderer
        {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (var back = new SolidBrush(Theme.Surface))
                    e.Graphics.FillRectangle(back, e.AffectedBounds);
                using (var line = new Pen(Theme.Border))
                    e.Graphics.DrawLine(line, 0, 0, e.ToolStrip.Width, 0);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                // No border.
            }
        }
    }
}
