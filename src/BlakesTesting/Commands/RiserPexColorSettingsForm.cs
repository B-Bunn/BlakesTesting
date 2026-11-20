#nullable enable
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using BlakesTesting.Risers;

namespace BlakesTesting.Commands
{
    /// <summary>The settings window for the riser PEX colouring: what gets painted, and in which colours.</summary>
    internal sealed class RiserPexColorSettingsForm : Form
    {
        private readonly ColorSettings _settings;
        private readonly CheckBox _enabled = new CheckBox();
        private readonly CheckBox _rounded = new CheckBox();
        private readonly CheckBox _otherPex = new CheckBox();
        private readonly CheckBox _otherPipes = new CheckBox();
        private readonly CheckBox _lines = new CheckBox();
        private readonly Button _roundedColor = new Button();
        private readonly Button _notRoundedColor = new Button();
        private readonly Label _hint = new Label();

        public RiserPexColorSettingsForm(ColorSettings current)
        {
            _settings = current.Clone();

            Text = "Riser PEX colors";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(470, 330);

            int y = 14;
            _enabled.Text = "Color the pipes in the active view when a run sorts them";
            _enabled.SetBounds(16, y, 440, 22);
            Controls.Add(_enabled);
            y += 34;

            Controls.Add(new Label { Text = "What to color", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Left = 16, Top = y });
            y += 24;
            _rounded.Text = "PEX that was rounded  (the riser)";
            _rounded.SetBounds(32, y, 400, 22);
            Controls.Add(_rounded);
            _roundedColor.SetBounds(360, y - 2, 96, 26);
            StyleSwatch(_roundedColor, () => _settings.RoundedColor, c => _settings.RoundedColor = c);
            Controls.Add(_roundedColor);
            y += 30;
            _otherPex.Text = "PEX that was not rounded  (branches, mains)";
            _otherPex.SetBounds(32, y, 320, 22);
            Controls.Add(_otherPex);
            _notRoundedColor.SetBounds(360, y - 2, 96, 26);
            StyleSwatch(_notRoundedColor, () => _settings.NotRoundedColor, c => _settings.NotRoundedColor = c);
            Controls.Add(_notRoundedColor);
            y += 30;
            _otherPipes.Text = "Every other pipe too  (not PEX - uses the not-rounded color)";
            _otherPipes.SetBounds(32, y, 420, 22);
            Controls.Add(_otherPipes);
            y += 40;

            Controls.Add(new Label { Text = "How to paint it", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Left = 16, Top = y });
            y += 24;
            _lines.Text = "Color the pipe's lines too  (so it shows in coarse, hidden-line and wireframe views)";
            _lines.SetBounds(32, y, 430, 22);
            Controls.Add(_lines);
            y += 30;
            _hint.Text = "Solid fill on the surface and cut, in the active view only. It is a view override: nothing in the model changes, "
                         + "and Undo takes it back. 'Clear Riser PEX Colors' removes it from a view.";
            _hint.SetBounds(16, y, 440, 56);
            _hint.ForeColor = SystemColors.GrayText;
            Controls.Add(_hint);

            Button reset = new Button { Text = "Defaults", Left = 16, Top = 290, Width = 90, Height = 28 };
            reset.Click += (s, e) => { Pull(new ColorSettings()); };
            Controls.Add(reset);

            Button ok = new Button { Text = "OK", Left = 280, Top = 290, Width = 90, Height = 28, DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = "Cancel", Left = 376, Top = 290, Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            _enabled.CheckedChanged += (s, e) => UpdateEnabled();
            Pull(_settings);
        }

        /// <summary>The settings as the window now has them (valid after OK).</summary>
        public ColorSettings Result()
        {
            ColorSettings s = _settings.Clone();
            s.Enabled = _enabled.Checked;
            s.ColorRounded = _rounded.Checked;
            s.ColorOtherPex = _otherPex.Checked;
            s.ColorOtherPipes = _otherPipes.Checked;
            s.ColorLines = _lines.Checked;
            return s;
        }

        // Puts a settings object into the controls (also the Defaults button).
        private void Pull(ColorSettings s)
        {
            _settings.RoundedColor = s.RoundedColor;
            _settings.NotRoundedColor = s.NotRoundedColor;
            _enabled.Checked = s.Enabled;
            _rounded.Checked = s.ColorRounded;
            _otherPex.Checked = s.ColorOtherPex;
            _otherPipes.Checked = s.ColorOtherPipes;
            _lines.Checked = s.ColorLines;
            ShowSwatch(_roundedColor, s.RoundedColor);
            ShowSwatch(_notRoundedColor, s.NotRoundedColor);
            UpdateEnabled();
        }

        private void UpdateEnabled()
        {
            bool on = _enabled.Checked;
            foreach (Control c in new Control[] { _rounded, _otherPex, _otherPipes, _lines, _roundedColor, _notRoundedColor })
                c.Enabled = on;
        }

        private void StyleSwatch(Button b, Func<RgbColor> get, Action<RgbColor> set)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.Click += (s, e) =>
            {
                RgbColor now = get();
                using (ColorDialog dlg = new ColorDialog { Color = Color.FromArgb(now.R, now.G, now.B), FullOpen = true, AnyColor = true })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    RgbColor picked = new RgbColor(dlg.Color.R, dlg.Color.G, dlg.Color.B);
                    set(picked);
                    ShowSwatch(b, picked);
                }
            };
        }

        private static void ShowSwatch(Button b, RgbColor c)
        {
            b.BackColor = Color.FromArgb(c.R, c.G, c.B);
            b.Text = c.ToHex();
            // Black or white text, whichever reads on the swatch.
            b.ForeColor = (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 140 ? Color.Black : Color.White;
        }
    }

    /// <summary>Revit's main window as the owner of a dialog, so it opens in front of Revit and not behind it.</summary>
    internal sealed class RevitWindow : IWin32Window
    {
        public RevitWindow()
        {
            Handle = Process.GetCurrentProcess().MainWindowHandle;
        }

        public IntPtr Handle { get; }
    }
}
