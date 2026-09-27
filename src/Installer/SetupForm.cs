using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SerialDebuggerSetup
{
    /// <summary>The setup window.</summary>
    internal sealed class SetupForm : Form
    {
        private static readonly Color Accent = Color.FromArgb(0, 95, 184);
        private static readonly Color Muted = Color.FromArgb(96, 96, 96);
        private static readonly Color FooterBack = Color.FromArgb(243, 243, 243);

        private readonly TextBox _dirBox = new TextBox();
        private readonly Button _changeButton = new Button();
        private readonly CheckBox _desktopBox = new CheckBox();
        private readonly CheckBox _launchBox = new CheckBox();
        private readonly Button _installButton = new Button();
        private readonly Button _cancelButton = new Button();
        private readonly Label _message = new Label();
        private readonly TableLayoutPanel _body = new TableLayoutPanel();
        private bool _installed;

        public SetupForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = InstallerCore.AppName + " Setup";
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 330);
            Icon = Program.LoadIcon(32);

            var existing = InstallerCore.GetExistingInstall();
            _dirBox.Text = existing.Dir ?? InstallerCore.DefaultInstallDir;

            Controls.Add(BuildBody(existing.Version));
            Controls.Add(BuildFooter(existing.Version != null));
            Controls.Add(BuildHeader());
            ResumeLayout(false);
            PerformLayout();

            _dirBox.TabStop = false;          // don't open with the path highlighted
            ActiveControl = _installButton;
        }

        private Control BuildHeader()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Color.White, Padding = new Padding(24, 18, 24, 0) };
            var picture = new PictureBox
            {
                Image = Program.LoadIcon(48)?.ToBitmap(),
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(48, 48),
                Location = new Point(24, 18),
            };
            var title = new Label
            {
                Text = InstallerCore.AppName,
                Font = new Font("Segoe UI Semibold", 15F),
                AutoSize = true,
                Location = new Point(86, 16),
            };
            var subtitle = new Label
            {
                Text = $"Version {InstallerCore.Version}  ·  Simple, reliable serial port monitor",
                ForeColor = Muted,
                AutoSize = true,
                Location = new Point(88, 48),
            };
            var line = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.FromArgb(229, 229, 229) };
            header.Controls.AddRange(new Control[] { picture, title, subtitle, line });
            return header;
        }

        private Control BuildBody(string existingVersion)
        {
            _body.Dock = DockStyle.Fill;
            _body.Padding = new Padding(24, 16, 24, 8);
            _body.ColumnCount = 2;
            _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var dirLabel = new Label { Text = "Install to", AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            _dirBox.ReadOnly = true;
            _dirBox.BackColor = Color.White;
            _dirBox.Dock = DockStyle.Fill;
            _dirBox.Margin = new Padding(0, 0, 8, 0);
            _changeButton.Text = "Change…";
            _changeButton.AutoSize = true;
            _changeButton.Click += (sender, e) => ChooseFolder();

            _desktopBox.Text = "Create a desktop shortcut";
            _desktopBox.Checked = true;
            _desktopBox.AutoSize = true;
            _desktopBox.Margin = new Padding(0, 14, 0, 0);
            _launchBox.Text = "Open Serial Debugger when setup finishes";
            _launchBox.Checked = true;
            _launchBox.AutoSize = true;
            _launchBox.Margin = new Padding(0, 6, 0, 0);

            _message.AutoSize = true;
            _message.ForeColor = Muted;
            _message.MaximumSize = new Size(500, 0);
            _message.Margin = new Padding(0, 14, 0, 0);
            _message.Text = existingVersion != null
                ? $"Version {existingVersion} is already installed. It will be updated; your settings are kept."
                : "Installs for your Windows account only. No administrator rights needed.";

            _body.Controls.Add(dirLabel, 0, 0);
            _body.SetColumnSpan(dirLabel, 2);
            _body.Controls.Add(_dirBox, 0, 1);
            _body.Controls.Add(_changeButton, 1, 1);
            _body.Controls.Add(_desktopBox, 0, 2);
            _body.Controls.Add(_launchBox, 0, 3);
            _body.Controls.Add(_message, 0, 4);
            _body.SetColumnSpan(_message, 2);
            return _body;
        }

        private Control BuildFooter(bool isUpdate)
        {
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 58,
                Padding = new Padding(16, 13, 16, 0),
                BackColor = FooterBack,
            };

            _installButton.Text = isUpdate ? "Update" : "Install";
            _installButton.Size = new Size(104, 32);
            _installButton.FlatStyle = FlatStyle.Flat;
            _installButton.BackColor = Accent;
            _installButton.ForeColor = Color.White;
            _installButton.FlatAppearance.BorderColor = Accent;
            _installButton.Font = new Font("Segoe UI Semibold", 9F);
            _installButton.Click += (sender, e) => OnInstallClicked();

            _cancelButton.Text = "Cancel";
            _cancelButton.Size = new Size(104, 32);
            _cancelButton.Margin = new Padding(0, 3, 8, 0);
            _cancelButton.Click += (sender, e) => Close();

            footer.Controls.Add(_installButton);
            footer.Controls.Add(_cancelButton);
            AcceptButton = _installButton;
            CancelButton = _cancelButton;
            return footer;
        }

        private void ChooseFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Choose where to install Serial Debugger", SelectedPath = _dirBox.Text })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                string chosen = dialog.SelectedPath;
                // Install into a "Serial Debugger" subfolder unless that is already the chosen folder.
                if (!string.Equals(Path.GetFileName(chosen.TrimEnd('\\')), InstallerCore.AppName, StringComparison.OrdinalIgnoreCase))
                    chosen = Path.Combine(chosen, InstallerCore.AppName);
                _dirBox.Text = chosen;
            }
        }

        private void OnInstallClicked()
        {
            if (_installed)
            {
                if (_launchBox.Checked)
                    Process.Start(new ProcessStartInfo(Path.Combine(_dirBox.Text, InstallerCore.ExeName)) { UseShellExecute = true, WorkingDirectory = _dirBox.Text });
                Close();
                return;
            }

            string dir = _dirBox.Text;
            if (!Program.EnsureAppClosed(this, dir))
                return;

            Cursor = Cursors.WaitCursor;
            _installButton.Enabled = _cancelButton.Enabled = _changeButton.Enabled = false;
            List<string> warnings;
            try
            {
                warnings = InstallerCore.Install(dir, _desktopBox.Checked);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                _installButton.Enabled = _cancelButton.Enabled = _changeButton.Enabled = true;
                MessageBox.Show(this, "Setup could not finish:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Cursor = Cursors.Default;
            _installed = true;
            _dirBox.Enabled = _changeButton.Enabled = _desktopBox.Enabled = false;
            if (warnings.Count == 0)
            {
                _message.ForeColor = Color.FromArgb(15, 123, 15);
                _message.Text = "✓  Serial Debugger is installed. You can find it in the Start menu.";
            }
            else
            {
                _message.ForeColor = Color.FromArgb(157, 93, 0);
                _message.Text = "Serial Debugger is installed, with a problem:\n" + string.Join("\n", warnings);
            }
            _installButton.Text = "Finish";
            _installButton.Enabled = true;
            _cancelButton.Visible = false;
        }
    }
}
