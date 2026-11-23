#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace BlakesTesting.Flow
{
    /// <summary>
    ///     A plain single-stroke capital font, as lines. Revit's API cannot make text in a 3D view (a text note is a
    ///     view-specific annotation and Model Text can only be made inside a family), so Flow Arrow draws the label of a
    ///     3D arrow as model lines with this. Capitals, digits and a few marks; lower case is drawn as capitals.
    ///     No Revit types in here, so it is tested without Revit.
    /// </summary>
    public static class StrokeFont
    {
        /// <summary>The height of a capital in glyph units; every glyph is drawn on this grid.</summary>
        public const double CapHeight = 6.0;

        private const double LetterGap = 1.5;
        private const double SpaceWidth = 3.0;

        // Each glyph: its width in grid units, then its strokes as polylines "x,y x,y ...;x,y x,y". Origin bottom left, y up.
        private static readonly Dictionary<char, (double Width, string Strokes)> Glyphs = new Dictionary<char, (double, string)>
        {
            ['A'] = (4, "0,0 2,6 4,0;0.8,2 3.2,2"),
            ['B'] = (4, "0,0 0,6 3,6 4,5 4,4 3,3 0,3;3,3 4,2 4,1 3,0 0,0"),
            ['C'] = (4, "4,5 3,6 1,6 0,5 0,1 1,0 3,0 4,1"),
            ['D'] = (4, "0,0 0,6 2.5,6 4,4.5 4,1.5 2.5,0 0,0"),
            ['E'] = (4, "4,6 0,6 0,0 4,0;0,3 3,3"),
            ['F'] = (4, "4,6 0,6 0,0;0,3 3,3"),
            ['G'] = (4, "4,5 3,6 1,6 0,5 0,1 1,0 3,0 4,1 4,3 2,3"),
            ['H'] = (4, "0,0 0,6;4,0 4,6;0,3 4,3"),
            ['I'] = (2, "0,6 2,6;1,6 1,0;0,0 2,0"),
            ['J'] = (4, "4,6 4,1 3,0 1,0 0,1"),
            ['K'] = (4, "0,0 0,6;4,6 0,2;1.6,3.6 4,0"),
            ['L'] = (4, "0,6 0,0 4,0"),
            ['M'] = (4, "0,0 0,6 2,3 4,6 4,0"),
            ['N'] = (4, "0,0 0,6 4,0 4,6"),
            ['O'] = (4, "1,0 0,1 0,5 1,6 3,6 4,5 4,1 3,0 1,0"),
            ['P'] = (4, "0,0 0,6 3,6 4,5 4,4 3,3 0,3"),
            ['Q'] = (4, "1,0 0,1 0,5 1,6 3,6 4,5 4,1 3,0 1,0;2.5,2 4,0"),
            ['R'] = (4, "0,0 0,6 3,6 4,5 4,4 3,3 0,3;2,3 4,0"),
            ['S'] = (4, "4,5 3,6 1,6 0,5 0,4 1,3 3,3 4,2 4,1 3,0 1,0 0,1"),
            ['T'] = (4, "0,6 4,6;2,6 2,0"),
            ['U'] = (4, "0,6 0,1 1,0 3,0 4,1 4,6"),
            ['V'] = (4, "0,6 2,0 4,6"),
            ['W'] = (6, "0,6 1.5,0 3,4 4.5,0 6,6"),
            ['X'] = (4, "0,0 4,6;0,6 4,0"),
            ['Y'] = (4, "0,6 2,3 4,6;2,3 2,0"),
            ['Z'] = (4, "0,6 4,6 0,0 4,0"),
            ['0'] = (4, "1,0 0,1 0,5 1,6 3,6 4,5 4,1 3,0 1,0;0.8,0.8 3.2,5.2"),
            ['1'] = (4, "0.5,4.5 2,6 2,0;0.5,0 3.5,0"),
            ['2'] = (4, "0,5 1,6 3,6 4,5 4,4 0,0 4,0"),
            ['3'] = (4, "0,5 1,6 3,6 4,5 4,4 3,3 1.5,3;3,3 4,2 4,1 3,0 1,0 0,1"),
            ['4'] = (4, "3,0 3,6 0,2 4,2"),
            ['5'] = (4, "4,6 0,6 0,3 3,3 4,2 4,1 3,0 1,0 0,1"),
            ['6'] = (4, "4,5 3,6 1,6 0,5 0,1 1,0 3,0 4,1 4,2 3,3 0,3"),
            ['7'] = (4, "0,6 4,6 1.5,0"),
            ['8'] = (4, "1,3 0,4 0,5 1,6 3,6 4,5 4,4 3,3 1,3 0,2 0,1 1,0 3,0 4,1 4,2 3,3"),
            ['9'] = (4, "0,1 1,0 3,0 4,1 4,5 3,6 1,6 0,5 0,4 1,3 4,3"),
            ['-'] = (3, "0.5,3 2.5,3"),
            ['.'] = (1, "0.5,0 0.5,0.6"),
            [','] = (1, "0.5,0.6 0.2,-0.8"),
            [':'] = (1, "0.5,1 0.5,1.6;0.5,4 0.5,4.6"),
            ['/'] = (3, "0,0 3,6"),
            ['+'] = (4, "0,3 4,3;2,1 2,5"),
            ['('] = (2, "1.5,6.5 0.5,5 0.5,1 1.5,-0.5"),
            [')'] = (2, "0.5,6.5 1.5,5 1.5,1 0.5,-0.5"),
            ['"'] = (3, "0.8,6 0.8,4.5;2.2,6 2.2,4.5"),
            ['?'] = (4, "0,5 1,6 3,6 4,5 4,4 2,3 2,2;2,0.4 2,0"),
        };

        private static readonly Dictionary<char, List<List<(double X, double Y)>>> Parsed = ParseAll();

        private static Dictionary<char, List<List<(double X, double Y)>>> ParseAll()
        {
            Dictionary<char, List<List<(double X, double Y)>>> all = new Dictionary<char, List<List<(double X, double Y)>>>();
            foreach (KeyValuePair<char, (double Width, string Strokes)> g in Glyphs)
            {
                List<List<(double X, double Y)>> strokes = new List<List<(double X, double Y)>>();
                foreach (string stroke in g.Value.Strokes.Split(';'))
                {
                    List<(double X, double Y)> pts = new List<(double X, double Y)>();
                    foreach (string p in stroke.Split(' '))
                    {
                        string[] xy = p.Split(',');
                        pts.Add((double.Parse(xy[0], CultureInfo.InvariantCulture), double.Parse(xy[1], CultureInfo.InvariantCulture)));
                    }

                    strokes.Add(pts);
                }

                all[g.Key] = strokes;
            }

            return all;
        }

        private static char Key(char c)
        {
            char u = char.ToUpperInvariant(c);
            if (u == ' ' || Glyphs.ContainsKey(u)) return u;
            return '?';
        }

        /// <summary>The width of the text, in glyph units.</summary>
        public static double WidthUnits(string text)
        {
            double w = 0;
            bool first = true;
            foreach (char c in text)
            {
                char k = Key(c);
                if (!first) w += LetterGap;
                w += k == ' ' ? SpaceWidth : Glyphs[k].Width;
                first = false;
            }

            return w;
        }

        /// <summary>The text's width when its capitals are <paramref name="height"/> tall.</summary>
        public static double Width(string text, double height) => WidthUnits(text) * height / CapHeight;

        /// <summary>
        ///     The text as strokes: capitals <paramref name="height"/> tall, centred on <paramref name="center"/>, turned
        ///     <paramref name="angle"/> radians counter-clockwise.
        /// </summary>
        public static List<Seg> Layout(string text, double height, V2 center, double angle)
        {
            List<Seg> result = new List<Seg>();
            if (text.Length == 0 || height <= 0) return result;

            double k = height / CapHeight;
            double total = WidthUnits(text);
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            V2 Place(double gx, double gy)
            {
                double lx = (gx - total / 2) * k, ly = (gy - CapHeight / 2) * k;
                return new V2(center.X + lx * cos - ly * sin, center.Y + lx * sin + ly * cos);
            }

            double pen = 0;
            bool first = true;
            foreach (char c in text)
            {
                char key = Key(c);
                if (!first) pen += LetterGap;
                first = false;
                if (key == ' ')
                {
                    pen += SpaceWidth;
                    continue;
                }

                foreach (List<(double X, double Y)> stroke in Parsed[key])
                    for (int i = 0; i + 1 < stroke.Count; i++)
                        result.Add(new Seg(Place(pen + stroke[i].X, stroke[i].Y), Place(pen + stroke[i + 1].X, stroke[i + 1].Y)));
                pen += Glyphs[key].Width;
            }

            return result;
        }
    }
}
