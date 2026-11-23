#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using BlakesTesting.Flow;
using BlakesTesting.Risers;

namespace BlakesTesting.Commands
{
    /// <summary>Which command opened the window, so it shows the options that command uses.</summary>
    internal enum FlowArrowFormMode
    {
        /// <summary>Flow Arrow: two clicks per arrow.</summary>
        Single,

        /// <summary>Flow Run: arrows repeated along a stretch of pipe.</summary>
        Run,

        /// <summary>Place on Selected Pipes: one arrow on each, by slope.</summary>
        Selected,
    }

    /// <summary>The window the placing commands open first: what the arrow says, and how it looks. OK starts placing.</summary>
    internal sealed class FlowArrowForm : Form
    {
        private static readonly string[] Presets = { "FLOW", "SAN", "STORM", "WASTE", "VENT", "DOMESTIC WATER" };
        private static readonly double[] Lengths = { 0.25, 0.375, 0.5, 0.75, 1.0 };
        private static readonly string[] LengthNames = { "1/4\"", "3/8\"", "1/2\"", "3/4\"", "1\"" };
        private static readonly double[] TextSizes = { 3.0 / 32, 1.0 / 8, 5.0 / 32, 3.0 / 16, 1.0 / 4 };
        private static readonly string[] TextSizeNames = { "3/32\"", "1/8\"", "5/32\"", "3/16\"", "1/4\"" };
        private static readonly double[] Spacings = { 3, 5, 8, 10, 15, 20, 30, 50 };

        private readonly FlowArrowSettings _settings;
        private readonly ComboBox _text = new ComboBox();
        private readonly ComboBox _style = new ComboBox();
        private readonly ComboBox _length = new ComboBox();
        private readonly ComboBox _size = new ComboBox();
        private readonly ComboBox _position = new ComboBox();
        private readonly ComboBox _reading = new ComboBox();
        private readonly ComboBox _spacing = new ComboBox();
        private readonly ComboBox _runDirection = new ComboBox();
        private readonly CheckBox _skip = new CheckBox();
        private readonly Button _colorButton = new Button();
        private RgbColor? _color;

        private FlowArrowForm(FlowArrowSettings current, FlowArrowFormMode mode)
        {
            _settings = current.Clone();

            Text = mode == FlowArrowFormMode.Run ? "Flow Run" : mode == FlowArrowFormMode.Selected ? "Flow Arrow on Selected Pipes" : "Flow Arrow";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;

            int y = 16;
            Row("Text on the arrow", _text, ref y);
            _text.DropDownStyle = ComboBoxStyle.DropDown;
            _text.MaxLength = 40;
            foreach (string r in _settings.Recent) _text.Items.Add(r);
            foreach (string p in Presets)
                if (!_settings.Recent.Any(r => string.Equals(r, p, StringComparison.OrdinalIgnoreCase))) _text.Items.Add(p);
            _text.Text = _settings.Text;

            Row("Arrow", _style, ref y);
            _style.DropDownStyle = ComboBoxStyle.DropDownList;
            _style.Items.AddRange(new object[] { "Open arrow", "Three chevrons" });
            _style.SelectedIndex = _settings.Style == FlowArrowStyle.Chevrons ? 1 : 0;

            Row("Arrow length (on paper)", _length, ref y);
            _length.DropDownStyle = ComboBoxStyle.DropDownList;
            _length.Items.AddRange(LengthNames.Cast<object>().ToArray());
            _length.SelectedIndex = Nearest(Lengths, _settings.ArrowLengthInches);

            Row("Text height (on paper)", _size, ref y);
            _size.DropDownStyle = ComboBoxStyle.DropDownList;
            _size.Items.AddRange(TextSizeNames.Cast<object>().ToArray());
            _size.SelectedIndex = Nearest(TextSizes, _settings.TextSizeInches);

            Row("Position", _position, ref y);
            _position.DropDownStyle = ComboBoxStyle.DropDownList;
            _position.Items.AddRange(new object[] { "Beside the pipe", "On the pipe", "Text in the pipe (breaks it)" });
            _position.SelectedIndex = (int)_settings.Position;

            Row("Text reads", _reading, ref y);
            _reading.DropDownStyle = ComboBoxStyle.DropDownList;
            _reading.Items.AddRange(new object[] { "Along the pipe, upright", "Always level" });
            _reading.SelectedIndex = (int)_settings.TextReading;

            Controls.Add(new Label { Text = "Color", AutoSize = true, Left = 16, Top = y + 4 });
            _colorButton.SetBounds(200, y, 96, 24);
            _colorButton.FlatStyle = FlatStyle.Flat;
            _colorButton.Click += (s, e) => PickColor();
            Controls.Add(_colorButton);
            Button none = new Button { Text = "Default", Left = 300, Top = y, Width = 60, Height = 24 };
            none.Click += (s, e) => SetColor(null);
            Controls.Add(none);
            RgbColor[] quick =
            {
                new RgbColor(0, 0, 0), new RgbColor(220, 0, 0), new RgbColor(0, 0, 255),
                new RgbColor(0, 140, 0), new RgbColor(230, 0, 230), new RgbColor(255, 130, 0),
            };
            for (int i = 0; i < quick.Length; i++)
            {
                RgbColor q = quick[i];
                Button b = new Button { Left = 366 + i * 20, Top = y + 2, Width = 18, Height = 20, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(q.R, q.G, q.B) };
                b.FlatAppearance.BorderColor = SystemColors.ControlDark;
                b.Click += (s, e) => SetColor(q);
                Controls.Add(b);
            }

            y += 36;
            SetColor(_settings.Color);

            string hintText;
            if (mode == FlowArrowFormMode.Run)
            {
                Row("Arrows about every", _spacing, ref y);
                _spacing.DropDownStyle = ComboBoxStyle.DropDownList;
                _spacing.Items.AddRange(Spacings.Select(v => (object)(v.ToString("0") + "'-0\"")).ToArray());
                _spacing.SelectedIndex = Nearest(Spacings, _settings.RunSpacingFeet);

                Row("Point", _runDirection, ref y);
                _runDirection.DropDownStyle = ComboBoxStyle.DropDownList;
                _runDirection.Items.AddRange(new object[] { "Toward the second click", "Away from the second click", "Downhill (follow the slope)" });
                _runDirection.SelectedIndex = (int)_settings.RunDirection;

                hintText = "Click a point on a pipe where the run starts (the arrows go on the side you click), then a point on the same pipe where it ends. "
                           + "The text goes on one arrow, near the middle. Esc finishes.";
            }
            else if (mode == FlowArrowFormMode.Selected)
            {
                _skip.Text = "Leave pipes that already have an arrow alone";
                _skip.SetBounds(200, y, 284, 24);
                _skip.Checked = _settings.SkipMarked;
                Controls.Add(_skip);
                y += 36;
                hintText = "Select the pipes first (or you will be asked to pick them). One arrow goes on the middle of each, pointing downhill by its slope. "
                           + "Pipes too short for the arrow and text, or pointing at the viewer, are left out and counted.";
            }
            else
            {
                hintText = "Click a point on a pipe (the arrow goes on the side you click), then click toward the way it flows. Click across the pipe to choose the side there "
                           + "instead. Click the same spot to follow the pipe's slope. Esc finishes.";
            }

            Label hint = new Label { Text = hintText + " Hold Shift when you start the command to skip this window.", ForeColor = SystemColors.GrayText };
            hint.SetBounds(16, y + 6, 468, 78);
            Controls.Add(hint);
            y += 92;

            string okText = mode == FlowArrowFormMode.Run ? "Start run" : mode == FlowArrowFormMode.Selected ? "Place on pipes" : "Place arrows";
            Button ok = new Button { Text = okText, Left = 290, Top = y, Width = 110, Height = 28, DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = "Cancel", Left = 406, Top = y, Width = 78, Height = 28, DialogResult = DialogResult.Cancel };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            ClientSize = new Size(500, y + 44);
        }

        /// <summary>Shows the window. True (and the new settings) when the person pressed the OK button.</summary>
        public static bool Edit(FlowArrowSettings current, FlowArrowFormMode mode, out FlowArrowSettings result)
        {
            result = current;
            using (FlowArrowForm form = new FlowArrowForm(current, mode))
            {
                if (form.ShowDialog(new RevitWindow()) != DialogResult.OK) return false;
                result = form.Result();
                return true;
            }
        }

        private FlowArrowSettings Result()
        {
            FlowArrowSettings s = _settings.Clone();
            s.Text = _text.Text;
            s.Style = _style.SelectedIndex == 1 ? FlowArrowStyle.Chevrons : FlowArrowStyle.Arrow;
            s.ArrowLengthInches = Lengths[Math.Max(0, _length.SelectedIndex)];
            s.TextSizeInches = TextSizes[Math.Max(0, _size.SelectedIndex)];
            s.Position = (FlowArrowPosition)Math.Max(0, _position.SelectedIndex);
            s.TextReading = (FlowArrowTextReading)Math.Max(0, _reading.SelectedIndex);
            s.Color = _color;
            if (_spacing.Items.Count > 0) s.RunSpacingFeet = Spacings[Math.Max(0, _spacing.SelectedIndex)];
            if (_runDirection.Items.Count > 0) s.RunDirection = (FlowArrowRunDirection)Math.Max(0, _runDirection.SelectedIndex);
            if (_skip.Parent != null) s.SkipMarked = _skip.Checked;
            return s.Normalize();
        }

        private void PickColor()
        {
            RgbColor start = _color ?? new RgbColor(220, 0, 0);
            using (ColorDialog dlg = new ColorDialog { Color = Color.FromArgb(start.R, start.G, start.B), FullOpen = true, AnyColor = true })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) SetColor(new RgbColor(dlg.Color.R, dlg.Color.G, dlg.Color.B));
            }
        }

        private void SetColor(RgbColor? color)
        {
            _color = color;
            if (color.HasValue)
            {
                RgbColor c = color.Value;
                _colorButton.BackColor = Color.FromArgb(c.R, c.G, c.B);
                _colorButton.Text = c.ToHex();
                _colorButton.ForeColor = (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 140 ? Color.Black : Color.White;
            }
            else
            {
                _colorButton.BackColor = SystemColors.Control;
                _colorButton.Text = "Line style's";
                _colorButton.ForeColor = SystemColors.ControlText;
            }
        }

        private void Row(string label, ComboBox box, ref int y)
        {
            Controls.Add(new Label { Text = label, AutoSize = true, Left = 16, Top = y + 4 });
            box.SetBounds(200, y, 284, 24);
            Controls.Add(box);
            y += 36;
        }

        private static int Nearest(double[] values, double v)
        {
            int best = 0;
            for (int i = 1; i < values.Length; i++)
                if (Math.Abs(values[i] - v) < Math.Abs(values[best] - v)) best = i;
            return best;
        }
    }

    /// <summary>A one-line window asking for the text of the arrows, for Edit Flow Arrow Text.</summary>
    internal sealed class FlowArrowTextForm : Form
    {
        private static readonly string[] Presets = { "FLOW", "SAN", "STORM", "WASTE", "VENT", "DOMESTIC WATER" };
        private readonly ComboBox _text = new ComboBox();

        private FlowArrowTextForm(FlowArrowSettings settings, string start, int count)
        {
            Text = "Edit Flow Arrow Text";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(440, 150);

            Controls.Add(new Label { Text = "New text for " + count + " arrow(s). Leave it empty to take the text off.", AutoSize = false, Left = 16, Top = 14, Width = 408, Height = 20 });
            _text.SetBounds(16, 40, 408, 24);
            _text.DropDownStyle = ComboBoxStyle.DropDown;
            _text.MaxLength = 40;
            foreach (string r in settings.Recent) _text.Items.Add(r);
            foreach (string p in Presets)
                if (!settings.Recent.Any(r => string.Equals(r, p, StringComparison.OrdinalIgnoreCase))) _text.Items.Add(p);
            _text.Text = start;
            Controls.Add(_text);
            Label hint = new Label { Text = "In a 3D view the text is drawn from lines (capitals only).", ForeColor = SystemColors.GrayText, Left = 16, Top = 72, Width = 408, Height = 20 };
            Controls.Add(hint);

            Button ok = new Button { Text = "Change text", Left = 238, Top = 106, Width = 110, Height = 28, DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = "Cancel", Left = 354, Top = 106, Width = 70, Height = 28, DialogResult = DialogResult.Cancel };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        /// <summary>Asks for the text. False when cancelled.</summary>
        public static bool Ask(FlowArrowSettings settings, int count, out string text)
        {
            text = settings.Text;
            using (FlowArrowTextForm form = new FlowArrowTextForm(settings, settings.Text, count))
            {
                if (form.ShowDialog(new RevitWindow()) != DialogResult.OK) return false;
                text = FlowArrowSettings.CleanText(form._text.Text);
                return true;
            }
        }
    }
}
