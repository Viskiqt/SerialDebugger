using System.Drawing;
using System.Windows.Forms;

namespace SerialDebugger
{
    /// <summary>Light, Windows 11 style colours and fonts.</summary>
    internal static class Theme
    {
        public static readonly Color WindowBack = Color.FromArgb(243, 243, 243);
        public static readonly Color Surface = Color.White;
        public static readonly Color Border = Color.FromArgb(222, 222, 222);
        public static readonly Color ControlBorder = Color.FromArgb(204, 204, 204);
        public static readonly Color Text = Color.FromArgb(27, 27, 27);
        public static readonly Color TextSecondary = Color.FromArgb(96, 96, 96);
        public static readonly Color HoverBack = Color.FromArgb(246, 246, 246);
        public static readonly Color PressedBack = Color.FromArgb(235, 235, 235);
        public static readonly Color DisabledBack = Color.FromArgb(229, 229, 229);
        public static readonly Color DisabledText = Color.FromArgb(160, 160, 160);

        public static readonly Color Accent = Color.FromArgb(0, 95, 184);
        public static readonly Color AccentHover = Color.FromArgb(25, 110, 191);
        public static readonly Color AccentPressed = Color.FromArgb(0, 78, 152);

        public static readonly Color Success = Color.FromArgb(15, 123, 15);
        public static readonly Color Warning = Color.FromArgb(157, 93, 0);
        public static readonly Color Error = Color.FromArgb(196, 43, 28);
        public static readonly Color Neutral = Color.FromArgb(138, 138, 138);

        public static readonly Font UiFont = new Font("Segoe UI", 9F);
        public static readonly Font UiFontSemibold = CreateFont(9F, FontStyle.Regular, "Segoe UI Semibold", "Segoe UI");
        public static readonly Font TerminalFont = CreateFont(10F, FontStyle.Regular, "Cascadia Mono", "Consolas", "Courier New");
        public static readonly Font IconFont = CreateFont(10F, FontStyle.Regular, "Segoe Fluent Icons", "Segoe MDL2 Assets");

        /// <summary>Glyphs from the Segoe Fluent Icons / MDL2 icon fonts.</summary>
        public const string RefreshGlyph = "";

        /// <summary>Filled blue button for the main action.</summary>
        public static void StylePrimary(Button button)
        {
            Prepare(button);
            button.Font = UiFontSemibold;
            button.BackColor = Accent;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderColor = Accent;
            button.FlatAppearance.MouseOverBackColor = AccentHover;
            button.FlatAppearance.MouseDownBackColor = AccentPressed;
        }

        /// <summary>White button with a thin border.</summary>
        public static void StyleSecondary(Button button)
        {
            Prepare(button);
            button.BackColor = Surface;
            button.ForeColor = Text;
            button.FlatAppearance.BorderColor = ControlBorder;
            button.FlatAppearance.MouseOverBackColor = HoverBack;
            button.FlatAppearance.MouseDownBackColor = PressedBack;
        }

        /// <summary>Greyed-out look (flat buttons keep their colours when disabled otherwise).</summary>
        public static void StyleDisabled(Button button)
        {
            Prepare(button);
            button.BackColor = DisabledBack;
            button.ForeColor = DisabledText;
            button.FlatAppearance.BorderColor = DisabledBack;
        }

        private static void Prepare(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
        }

        private static Font CreateFont(float size, FontStyle style, params string[] families)
        {
            foreach (string family in families)
            {
                var font = new Font(family, size, style);
                if (font.Name == family)
                    return font;   // installed
                font.Dispose();
            }
            return new Font(FontFamily.GenericSansSerif, size, style);
        }
    }
}
