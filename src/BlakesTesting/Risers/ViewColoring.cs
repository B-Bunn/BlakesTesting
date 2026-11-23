#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace BlakesTesting.Risers
{
    /// <summary>
    ///     Paints pipes in ONE view with per-element graphic overrides (Override Graphics in View > By Element), so it is
    ///     a view setting only: nothing about the model, the pipes or their parameters changes, and Undo takes it back.
    ///     The fill is the project's solid fill pattern, on the surface and the cut; with ColorLines the projection and
    ///     cut lines take the colour too, which is what shows in a coarse-detail, hidden-line or wireframe view where a
    ///     surface fill does not. Call everything in here inside a transaction.
    /// </summary>
    internal static class ViewColoring
    {
        /// <summary>Null when the view can take overrides; otherwise a short reason it cannot.</summary>
        public static string? Problem(View view)
        {
            try
            {
                if (view is ViewSheet || view is ViewSchedule || !view.AreGraphicsOverridesAllowed())
                    return "this view can't take graphic overrides (a sheet, schedule or legend?)";
                return null;
            }
            catch
            {
                return "this view can't take graphic overrides";
            }
        }

        /// <summary>The project's solid fill pattern, or null when it has none.</summary>
        public static ElementId? SolidFill(Document doc)
        {
            foreach (FillPatternElement fpe in new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>())
            {
                FillPattern? fp = fpe.GetFillPattern();
                if (fp != null && fp.IsSolidFill) return fpe.Id;
            }

            return null;
        }

        /// <summary>
        ///     Colours each pipe by whether it was rounded, as the settings say (a pipe the settings give no colour is left
        ///     exactly as it is). Returns how many were coloured; <paramref name="failed"/> counts the ones Revit would not
        ///     override in this view (hidden by a filter or a category, say).
        /// </summary>
        public static int Apply(Document doc, View view, IEnumerable<KeyValuePair<Element, bool>> pipes, ColorSettings settings, out int failed)
        {
            failed = 0;
            if (!settings.Enabled || Problem(view) != null) return 0;

            ElementId? solid = SolidFill(doc);
            Dictionary<RgbColor, OverrideGraphicSettings> made = new Dictionary<RgbColor, OverrideGraphicSettings>();
            int colored = 0;
            foreach (KeyValuePair<Element, bool> pair in pipes)
            {
                RgbColor? choice = settings.Choose(pair.Value, RevitPipeNetwork.IsPex(doc, pair.Key));
                if (choice == null) continue;

                if (!made.TryGetValue(choice.Value, out OverrideGraphicSettings? ogs))
                    made[choice.Value] = ogs = Make(choice.Value, solid, settings.ColorLines);
                try
                {
                    view.SetElementOverrides(pair.Key.Id, ogs);
                    colored++;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    failed++;
                }
            }

            return colored;
        }

        /// <summary>
        ///     Takes the colours back off: every one of these pipes whose override is one of the two colours in the
        ///     settings goes back to no override at all. A pipe coloured something else is not touched - but note that
        ///     a pipe that IS cleared loses any other override it had (halftone, transparency).
        /// </summary>
        public static int Clear(View view, IEnumerable<Element> pipes, ColorSettings settings)
        {
            if (Problem(view) != null) return 0;
            int cleared = 0;
            foreach (Element e in pipes)
            {
                try
                {
                    OverrideGraphicSettings now = view.GetElementOverrides(e.Id);
                    if (!IsOurs(now, settings)) continue;
                    view.SetElementOverrides(e.Id, new OverrideGraphicSettings());
                    cleared++;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                }
            }

            return cleared;
        }

        /// <summary>
        ///     Paints these elements ONE colour in the view (a solid surface fill and, with <paramref name="lines"/>, the
        ///     lines), for a command whose colour is not one of the two riser colours. Returns how many were painted;
        ///     <paramref name="failed"/> counts the ones Revit would not override here.
        /// </summary>
        public static int Paint(Document doc, View view, IEnumerable<ElementId> ids, RgbColor color, bool lines, out int failed)
        {
            failed = 0;
            if (Problem(view) != null) return 0;

            OverrideGraphicSettings ogs = Make(color, SolidFill(doc), lines);
            int painted = 0;
            foreach (ElementId id in ids)
            {
                try
                {
                    view.SetElementOverrides(id, ogs);
                    painted++;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    failed++;
                }
            }

            return painted;
        }

        /// <summary>Takes <see cref="Paint"/>'s colour back off: every element whose override is that colour goes back to none.</summary>
        public static int ClearColor(View view, IEnumerable<Element> elements, RgbColor color)
        {
            if (Problem(view) != null) return 0;
            int cleared = 0;
            foreach (Element e in elements)
            {
                try
                {
                    OverrideGraphicSettings now = view.GetElementOverrides(e.Id);
                    Color? surface = now.SurfaceForegroundPatternColor;
                    Color? line = now.ProjectionLineColor;
                    bool ours = (surface != null && surface.IsValid && new RgbColor(surface.Red, surface.Green, surface.Blue).Equals(color))
                                || (line != null && line.IsValid && new RgbColor(line.Red, line.Green, line.Blue).Equals(color));
                    if (!ours) continue;
                    view.SetElementOverrides(e.Id, new OverrideGraphicSettings());
                    cleared++;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                }
            }

            return cleared;
        }

        private static OverrideGraphicSettings Make(RgbColor c, ElementId? solid, bool lines)
        {
            Color color = new Color(c.R, c.G, c.B);
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            if (solid != null)
            {
                ogs.SetSurfaceForegroundPatternId(solid);
                ogs.SetSurfaceForegroundPatternColor(color);
                ogs.SetSurfaceForegroundPatternVisible(true);
                ogs.SetCutForegroundPatternId(solid);
                ogs.SetCutForegroundPatternColor(color);
                ogs.SetCutForegroundPatternVisible(true);
            }

            if (lines)
            {
                ogs.SetProjectionLineColor(color);
                ogs.SetCutLineColor(color);
            }

            return ogs;
        }

        private static bool IsOurs(OverrideGraphicSettings ogs, ColorSettings s) =>
            Matches(ogs.SurfaceForegroundPatternColor, s) || Matches(ogs.ProjectionLineColor, s);

        private static bool Matches(Color? c, ColorSettings s)
        {
            if (c == null || !c.IsValid) return false;
            RgbColor have = new RgbColor(c.Red, c.Green, c.Blue);
            return have.Equals(s.RoundedColor) || have.Equals(s.NotRoundedColor);
        }
    }
}
