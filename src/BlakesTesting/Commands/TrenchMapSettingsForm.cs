#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using BlakesTesting.Trench;

namespace BlakesTesting.Commands
{
    /// <summary>
    ///     The Trench Map settings window: what the colour's depth is measured to, where the floor is, how wide a trench
    ///     is dug at each depth, and the depths the colours change at. Lengths are typed as <c>4'-6"</c>, <c>54"</c> or
    ///     feet (<c>4.5</c>). <see cref="Result"/> is valid after OK; the window refuses OK while a box holds something
    ///     it cannot read.
    /// </summary>
    internal sealed class TrenchMapSettingsForm : Form
    {
        private readonly TrenchMapSettings _start;
        private readonly ComboBox _colorBy = new ComboBox();
        private readonly Label _colorHint = new Label();
        private readonly RadioButton _level = new RadioButton();
        private readonly ComboBox _levelName = new ComboBox();
        private readonly RadioButton _floors = new RadioButton();
        private readonly TextBox _param = new TextBox();
        private readonly TextBox _value = new TextBox();
        private readonly TextBox _offset = new TextBox();
        private readonly TextBox _below = new TextBox();
        private readonly DataGridView _bands = new DataGridView();
        private readonly TextBox _deeper = new TextBox();
        private readonly TextBox _marks = new TextBox();
        private readonly NumericUpDown _transparency = new NumericUpDown();
        private readonly CheckBox _footage = new CheckBox();
        private TrenchMapSettings _result;

        public TrenchMapSettingsForm(TrenchMapSettings current, IEnumerable<string> levels, string? banner)
        {
            _start = current.Clone();
            _result = current.Clone();

            Text = "Trench Map settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(580, 700);

            int y = 12;
            if (!string.IsNullOrEmpty(banner))
            {
                Controls.Add(new Label { Text = banner + " It is saved for the next run.", ForeColor = Color.Firebrick, Left = 16, Top = y, Width = 548, Height = 36 });
                y += 44;
            }

            Header("Color the depth by", ref y);
            _colorBy.DropDownStyle = ComboBoxStyle.DropDownList;
            _colorBy.Items.Add("Spoils depth - top of the trench to the bottom of the trench");
            _colorBy.Items.Add("Bottom of pipe - the floor to the bottom of the pipe");
            _colorBy.SetBounds(32, y, 520, 24);
            _colorBy.SelectedIndexChanged += (s, e) => ShowColorHint();
            Controls.Add(_colorBy);
            y += 28;
            _colorHint.SetBounds(32, y, 520, 34);
            _colorHint.ForeColor = SystemColors.GrayText;
            Controls.Add(_colorHint);
            y += 42;

            Header("The floor the trench is dug from", ref y);
            _level.Text = "Level:";
            _level.SetBounds(32, y, 120, 22);
            _levelName.DropDownStyle = ComboBoxStyle.DropDown;
            foreach (string l in levels) _levelName.Items.Add(l);
            _levelName.SetBounds(156, y, 396, 24);
            Controls.Add(_level);
            Controls.Add(_levelName);
            y += 32;
            _floors.Text = "Floors where:";
            _floors.SetBounds(32, y, 120, 22);
            _param.SetBounds(156, y, 170, 24);
            Controls.Add(_floors);
            Controls.Add(_param);
            Controls.Add(new Label { Text = "=", Left = 332, Top = y + 3, Width = 14 });
            _value.SetBounds(350, y, 202, 24);
            Controls.Add(_value);
            y += 34;
            _level.CheckedChanged += (s, e) => UpdateSource();

            AddRow("Floor offset (top of the floor down to the top of the trench):", _offset, ref y);
            AddRow("Dig below the bottom of the pipe (bedding):", _below, ref y);
            y += 6;

            Header("How wide the trench is dug", ref y);
            Controls.Add(new Label
            {
                Text = "A trench shallower than the depth in a row is that wide; the deeper width is for the rest. Depth is the spoils depth.",
                Left = 32, Top = y, Width = 520, Height = 32, ForeColor = SystemColors.GrayText,
            });
            y += 34;
            _bands.SetBounds(32, y, 520, 118);
            _bands.AllowUserToAddRows = true;
            _bands.AllowUserToDeleteRows = true;
            _bands.RowHeadersVisible = false;
            _bands.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _bands.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Shallower than", SortMode = DataGridViewColumnSortMode.NotSortable });
            _bands.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Is this wide", SortMode = DataGridViewColumnSortMode.NotSortable });
            Controls.Add(_bands);
            y += 126;
            AddRow("Deeper than the last row is this wide:", _deeper, ref y);
            y += 6;

            Header("The colors", ref y);
            Controls.Add(new Label
            {
                Text = "Each width and depth band is its own color. The colors change at these depths (comma between), e.g. 4'-0\", 8'-0\", 12'-0\". "
                       + "Blank is one band.",
                Left = 32, Top = y, Width = 520, Height = 32, ForeColor = SystemColors.GrayText,
            });
            y += 34;
            _marks.SetBounds(32, y, 520, 24);
            Controls.Add(_marks);
            y += 34;
            Controls.Add(new Label { Text = "See-through (0 solid, 90 faint) so pipes show:", Left = 32, Top = y + 3, Width = 320 });
            _transparency.SetBounds(360, y, 70, 24);
            _transparency.Minimum = 0;
            _transparency.Maximum = TrenchMapSettings.MaxTransparency;
            Controls.Add(_transparency);
            y += 32;
            _footage.Text = "Put each color's total length in the legend";
            _footage.SetBounds(32, y, 420, 22);
            Controls.Add(_footage);

            Button reset = new Button { Text = "Defaults", Left = 16, Top = 660, Width = 90, Height = 28 };
            reset.Click += (s, e) => Pull(new TrenchMapSettings { FloorOffset = _start.FloorOffset, LevelName = _start.LevelName });
            Controls.Add(reset);
            Button ok = new Button { Text = "OK", Left = 380, Top = 660, Width = 90, Height = 28 };
            ok.Click += (s, e) => Accept();
            Button cancel = new Button { Text = "Cancel", Left = 476, Top = 660, Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            Pull(current);
        }

        /// <summary>The settings as the window now has them (valid after OK).</summary>
        public TrenchMapSettings Result() => _result;

        private void Header(string text, ref int y)
        {
            Controls.Add(new Label { Text = text, Font = new Font(Font, FontStyle.Bold), AutoSize = true, Left = 16, Top = y });
            y += 24;
        }

        private void AddRow(string label, TextBox box, ref int y)
        {
            Controls.Add(new Label { Text = label, Left = 32, Top = y + 3, Width = 360 });
            box.SetBounds(400, y, 152, 24);
            Controls.Add(box);
            y += 30;
        }

        // Puts a settings object into the controls (also the Defaults button).
        private void Pull(TrenchMapSettings s)
        {
            _colorBy.SelectedIndex = s.ColorBy == TrenchColorBy.BottomOfPipe ? 1 : 0;
            _level.Checked = s.Source == TrenchFloorSource.Level;
            _floors.Checked = s.Source == TrenchFloorSource.Floors;
            _levelName.Text = s.LevelName;
            _param.Text = s.FloorParameter;
            _value.Text = s.FloorValue;
            _offset.Text = s.FloorOffset.HasValue ? TrenchText.Feet(s.FloorOffset.Value) : "";
            _below.Text = TrenchText.Feet(s.BelowPipe);
            _bands.Rows.Clear();
            foreach (TrenchWidthBand b in s.WidthBands) _bands.Rows.Add(TrenchText.Feet(b.ShallowerThan), TrenchText.Feet(b.Width));
            _deeper.Text = TrenchText.Feet(s.DeeperWidth);
            _marks.Text = TrenchText.FeetList(s.DepthMarks);
            _transparency.Value = Math.Min(Math.Max(s.Transparency, 0), TrenchMapSettings.MaxTransparency);
            _footage.Checked = s.LegendFootage;
            UpdateSource();
            ShowColorHint();
        }

        private void UpdateSource()
        {
            bool byLevel = _level.Checked;
            _levelName.Enabled = byLevel;
            _param.Enabled = !byLevel;
            _value.Enabled = !byLevel;
        }

        private void ShowColorHint()
        {
            _colorHint.Text = _colorBy.SelectedIndex == 1
                ? "Depth = the floor down to the bottom of the pipe (the offset and the bedding are left out). The width still follows the spoils depth."
                : "Depth = the floor less the offset, down to the bottom of the pipe less any bedding - the depth Calculate Spoils digs.";
        }

        private void Accept()
        {
            string? problem = TryRead(out TrenchMapSettings? read);
            if (problem != null || read == null)
            {
                MessageBox.Show(this, problem ?? "The settings could not be read.", "Trench Map settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _result = read;
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Reads the controls into settings, or says which box holds something unreadable.</summary>
        private string? TryRead(out TrenchMapSettings? read)
        {
            read = null;
            TrenchMapSettings s = new TrenchMapSettings
            {
                ColorBy = _colorBy.SelectedIndex == 1 ? TrenchColorBy.BottomOfPipe : TrenchColorBy.SpoilsDepth,
                Source = _floors.Checked ? TrenchFloorSource.Floors : TrenchFloorSource.Level,
                LevelName = _levelName.Text,
                FloorParameter = _param.Text,
                FloorValue = _value.Text,
                Transparency = (int)_transparency.Value,
                LegendFootage = _footage.Checked,
            };

            if (_offset.Text.Trim().Length == 0) s.FloorOffset = null;
            else if (TrenchText.TryParseFeet(_offset.Text, out double offset)) s.FloorOffset = offset;
            else return "The floor offset is not a length (type it like 6\" or 0'-6\"; a plain number is feet).";

            if (!TrenchText.TryParseFeet(_below.Text, out double below)) return "The dig below the pipe is not a length (0 for none).";
            s.BelowPipe = below;

            s.WidthBands = new List<TrenchWidthBand>();
            foreach (DataGridViewRow row in _bands.Rows)
            {
                if (row.IsNewRow) continue;
                string depthText = Convert.ToString(row.Cells[0].Value) ?? "";
                string widthText = Convert.ToString(row.Cells[1].Value) ?? "";
                if (depthText.Trim().Length == 0 && widthText.Trim().Length == 0) continue;
                if (!TrenchText.TryParseFeet(depthText, out double depth) || depth <= 0)
                    return "A width row's depth is not a length over zero: \"" + depthText + "\".";
                if (!TrenchText.TryParseFeet(widthText, out double width) || width <= 0)
                    return "A width row's width is not a length over zero: \"" + widthText + "\".";
                s.WidthBands.Add(new TrenchWidthBand { ShallowerThan = depth, Width = width });
            }

            if (!TrenchText.TryParseFeet(_deeper.Text, out double deeper) || deeper <= 0) return "The deeper width is not a length over zero.";
            s.DeeperWidth = deeper;

            if (!TrenchText.TryParseFeetList(_marks.Text, out List<double> marks)) return "The depths the colors change at must be lengths with commas between them.";
            if (marks.Any(m => m <= 0)) return "The depths the colors change at must be over zero.";
            s.DepthMarks = marks;

            // Rows on purpose: none in the grid is one width at every depth, which Normalize keeps for an empty list.
            read = s.Normalize();
            return null;
        }

        /// <summary>
        ///     Shows the window over Revit. True with the settings saved when OK was pressed. <paramref name="banner"/> says
        ///     what the run was missing, when the window opened because of that.
        /// </summary>
        public static bool Edit(TrenchMapSettings current, IEnumerable<string> levels, string? banner, out TrenchMapSettings result, out string? saveProblem)
        {
            result = current;
            saveProblem = null;
            using (TrenchMapSettingsForm form = new TrenchMapSettingsForm(current, levels, banner))
            {
                if (form.ShowDialog(new RevitWindow()) != DialogResult.OK) return false;
                result = form.Result();
            }

            if (!result.Save()) saveProblem = "The settings could not be saved to " + TrenchMapSettings.DefaultPath + " - they are used for this run only.";
            return true;
        }
    }
}
