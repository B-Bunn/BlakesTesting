#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BlakesTesting.Risers;

namespace BlakesTesting.Trench
{
    /// <summary>What a Trench Map drawing put in the view.</summary>
    internal sealed class TrenchDrawResult
    {
        /// <summary>Filled regions drawn for trench pieces.</summary>
        public int Regions { get; set; }

        /// <summary>Pieces too small for Revit to draw as a region, or that Revit refused.</summary>
        public int Skipped { get; set; }

        /// <summary>The first reason Revit gave for refusing a piece, for the report.</summary>
        public string? FirstProblem { get; set; }

        /// <summary>Elements a previous map left in the view that were removed first.</summary>
        public int Cleared { get; set; }

        /// <summary>Filled region types made (one per colour used) the first time a combination is drawn.</summary>
        public int TypesMade { get; set; }

        public List<TrenchLegendRow> Legend { get; set; } = new List<TrenchLegendRow>();

        /// <summary>Whether the transparency could be set on the drawn regions.</summary>
        public bool TransparencyApplied { get; set; }
    }

    /// <summary>
    ///     Draws the trench map into one plan view and clears it again. A trench piece is a filled region (a detail item,
    ///     so it belongs to that view alone) of a filled region type named <c>Trench Map W18 D1</c> - one type per
    ///     width and depth band, in the solid fill and the combination's colour; the legend off to the side of the trench
    ///     is the same filled regions as swatches with text notes of the types named <c>Trench Map Legend...</c>. The
    ///     names are what <see cref="Clear"/> finds them by, so nothing is stamped on the elements and a hand-made filled
    ///     region is never touched. Call <see cref="Draw"/> and <see cref="Clear"/> inside a transaction.
    /// </summary>
    internal static class TrenchDrawing
    {
        internal const string RegionTypePrefix = "Trench Map ";
        internal const string TextTypePrefix = "Trench Map Legend";
        private const string BodyTextType = TextTypePrefix;
        private const string TitleTextType = TextTypePrefix + " Title";

        /// <summary>Body and title text sizes on paper (feet): 3/32" and 1/8".</summary>
        private const double BodyPaperSize = 3.0 / 32.0 / 12.0;
        private const double TitlePaperSize = 1.0 / 8.0 / 12.0;

        /// <summary>A piece shorter or narrower than this (feet) is left out: Revit will not draw a region that small.</summary>
        private const double SmallestPiece = 0.02;

        /// <summary>Null when the map can be drawn in <paramref name="view"/>; otherwise a short reason it cannot.</summary>
        public static string? ViewProblem(View view)
        {
            if (view is ViewPlan plan && !plan.IsTemplate) return null;
            return "Trench Map draws in a plan view (floor plan, ceiling plan or engineering plan) - open one and run it again.";
        }

        /// <summary>Takes the map out of the view: every filled region of a Trench Map type and every text note of a Trench Map Legend type in it. Returns how many.</summary>
        public static int Clear(Document doc, View view)
        {
            List<ElementId> ids = new List<ElementId>();
            foreach (FilledRegion region in new FilteredElementCollector(doc, view.Id).OfClass(typeof(FilledRegion)).Cast<FilledRegion>())
                if (TypeNameStarts(doc, region, RegionTypePrefix)) ids.Add(region.Id);
            foreach (TextNote note in new FilteredElementCollector(doc, view.Id).OfClass(typeof(TextNote)).Cast<TextNote>())
                if (TypeNameStarts(doc, note, TextTypePrefix)) ids.Add(note.Id);

            if (ids.Count > 0) doc.Delete(ids);
            return ids.Count;
        }

        private static bool TypeNameStarts(Document doc, Element e, string prefix)
        {
            Element? type = doc.GetElement(e.GetTypeId());
            return type != null && type.Name.StartsWith(prefix, StringComparison.Ordinal);
        }

        /// <summary>Clears the view, then draws the pieces and the legend. Returns what it did, or throws <see cref="InvalidOperationException"/> with the reason it could not start.</summary>
        public static TrenchDrawResult Draw(Document doc, View view, TrenchMapSettings settings, TrenchPlanResult plan, double noFloorLength)
        {
            TrenchDrawResult result = new TrenchDrawResult { Cleared = Clear(doc, view) };

            ElementId solid = SolidDraftingFill(doc)
                              ?? throw new InvalidOperationException("This project has no solid drafting fill pattern, which the trench colours are drawn in.");
            FilledRegionType baseType = new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>()
                                            .FirstOrDefault(t => !t.Name.StartsWith(RegionTypePrefix, StringComparison.Ordinal))
                                        ?? throw new InvalidOperationException("This project has no filled region type to make the trench colours from.");

            // One type per combination in use, in the combination's colour.
            Dictionary<TrenchClass, ElementId> types = new Dictionary<TrenchClass, ElementId>();
            foreach (TrenchClass c in plan.Pieces.Select(p => p.Class).Distinct())
                types[c] = RegionType(doc, baseType, solid, settings, c, result);

            ElementId? invisible = InvisibleLines(doc);
            OverrideGraphicSettings? see = settings.Transparency > 0 ? new OverrideGraphicSettings().SetSurfaceTransparency(settings.Transparency) : null;
            PlaneZ z = new PlaneZ(view);

            // Shallow first, so a deeper trench is drawn over the shallower one where two meet at a corner.
            foreach (TrenchPiece piece in plan.Pieces.OrderBy(p => p.Class.DepthBand).ThenBy(p => p.Class.Width))
            {
                if (piece.Length < SmallestPiece || piece.Width < SmallestPiece)
                {
                    result.Skipped++;
                    continue;
                }

                try
                {
                    FilledRegion region = CreateRegion(doc, view, types[piece.Class], piece.Corners(), z);
                    Style(view, region, invisible, see, result);
                    result.Regions++;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    result.Skipped++;
                    if (result.FirstProblem == null) result.FirstProblem = ex.Message;
                }
            }

            result.Legend = TrenchLegend.Rows(settings, plan.Pieces, TrenchText.Feet);
            if (result.Regions > 0) DrawLegend(doc, view, settings, plan, result, types, see, z, noFloorLength);
            return result;
        }

        // ------------------------------------------------------------------ regions

        /// <summary>
        ///     The height the curves of a region are drawn at. A plan view's detail plane is its own; Revit takes the
        ///     level's height, and the others are fallbacks should a Revit want the curves on the view's own plane.
        ///     Whichever the first region is made at is kept for the rest.
        /// </summary>
        private sealed class PlaneZ
        {
            private readonly List<double> _candidates = new List<double>();
            private int _chosen = -1;

            public PlaneZ(View view)
            {
                if (view.GenLevel != null) _candidates.Add(view.GenLevel.ProjectElevation);
                _candidates.Add(view.Origin.Z);
                _candidates.Add(0);
            }

            public IEnumerable<double> Order()
            {
                if (_chosen >= 0)
                {
                    yield return _candidates[_chosen];
                    yield break;
                }

                for (int i = 0; i < _candidates.Count; i++) yield return _candidates[i];
            }

            public void Worked(double z)
            {
                if (_chosen < 0) _chosen = _candidates.IndexOf(z);
            }

            public double First => _chosen >= 0 ? _candidates[_chosen] : _candidates[0];
        }

        private static FilledRegion CreateRegion(Document doc, View view, ElementId typeId, (double X, double Y)[] corners, PlaneZ plane)
        {
            Exception? last = null;
            foreach (double z in plane.Order())
            {
                try
                {
                    CurveLoop loop = new CurveLoop();
                    for (int i = 0; i < corners.Length; i++)
                    {
                        (double X, double Y) a = corners[i], b = corners[(i + 1) % corners.Length];
                        loop.Append(Line.CreateBound(new XYZ(a.X, a.Y, z), new XYZ(b.X, b.Y, z)));
                    }

                    FilledRegion region = FilledRegion.Create(doc, typeId, view.Id, new List<CurveLoop> { loop });
                    plane.Worked(z);
                    return region;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    last = ex;
                }
            }

            throw last ?? new InvalidOperationException("Revit would not make the filled region.");
        }

        private static void Style(View view, FilledRegion region, ElementId? invisible, OverrideGraphicSettings? see, TrenchDrawResult result)
        {
            if (invisible != null)
            {
                try
                {
                    region.SetLineStyleId(invisible);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // The outline stays the default one: a thin border, nothing worse.
                }
            }

            if (see == null) return;
            try
            {
                view.SetElementOverrides(region.Id, see);
                result.TransparencyApplied = true;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // This Revit would not make a filled region see-through: it stays solid.
            }
        }

        private static ElementId? SolidDraftingFill(Document doc)
        {
            foreach (FillPatternElement fpe in new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>())
            {
                FillPattern? fp = fpe.GetFillPattern();
                if (fp != null && fp.IsSolidFill && fp.Target == FillPatternTarget.Drafting) return fpe.Id;
            }

            return null;
        }

        private static ElementId? InvisibleLines(Document doc)
        {
            try
            {
                return doc.Settings.Categories.get_Item(BuiltInCategory.OST_InvisibleLines)?.GetGraphicsStyle(GraphicsStyleType.Projection)?.Id;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }
        }

        /// <summary>The filled region type of one combination, made (from <paramref name="baseType"/>) the first time and its colour set every time.</summary>
        private static ElementId RegionType(Document doc, FilledRegionType baseType, ElementId solid, TrenchMapSettings settings,
            TrenchClass c, TrenchDrawResult result)
        {
            string name = RegionTypePrefix + "W" + TrenchText.Width(c.Width).TrimEnd('"') + " D" + c.DepthBand;
            FilledRegionType? type = new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>()
                .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
            if (type == null)
            {
                type = (FilledRegionType)baseType.Duplicate(name);
                result.TypesMade++;
            }

            RgbColor color = TrenchLegend.ColorOf(settings, c);
            type.ForegroundPatternId = solid;
            type.ForegroundPatternColor = new Color(color.R, color.G, color.B);
            type.BackgroundPatternId = ElementId.InvalidElementId;
            return type.Id;
        }

        // ------------------------------------------------------------------ legend

        private static ElementId TextType(Document doc, string name, double paperSize, bool bold)
        {
            TextNoteType? type = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
                .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
            if (type == null)
            {
                ElementId defaultId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
                TextNoteType baseType = (doc.GetElement(defaultId) as TextNoteType)
                                        ?? new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
                                            .FirstOrDefault(t => !t.Name.StartsWith(TextTypePrefix, StringComparison.Ordinal))
                                        ?? throw new InvalidOperationException("This project has no text type to make the legend's from.");
                type = (TextNoteType)baseType.Duplicate(name);
            }

            type.get_Parameter(BuiltInParameter.TEXT_SIZE)?.Set(paperSize);
            type.get_Parameter(BuiltInParameter.TEXT_STYLE_BOLD)?.Set(bold ? 1 : 0);
            return type.Id;
        }

        private static void DrawLegend(Document doc, View view, TrenchMapSettings settings, TrenchPlanResult plan, TrenchDrawResult result,
            Dictionary<TrenchClass, ElementId> types, OverrideGraphicSettings? see, PlaneZ plane, double noFloorLength)
        {
            ElementId bodyType = TextType(doc, BodyTextType, BodyPaperSize, bold: false);
            ElementId titleType = TextType(doc, TitleTextType, TitlePaperSize, bold: true);

            // Sizes are on paper; the view's scale makes them the model feet they take up on the plan.
            double scale = Math.Max(1, view.Scale);
            double body = BodyPaperSize * scale, title = TitlePaperSize * scale;

            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            foreach (TrenchPiece piece in plan.Pieces)
                foreach ((double X, double Y) c in piece.Corners())
                {
                    maxX = Math.Max(maxX, c.X);
                    maxY = Math.Max(maxY, c.Y);
                }

            double z = plane.First;
            double x0 = maxX + 8 * body;
            double y = maxY;
            double swatchW = 5 * body, swatchH = 1.5 * body, rowH = 2.6 * body;

            (string heading, string subtitle) = TrenchLegend.Heading(settings);
            Note(doc, view, titleType, x0, y, z, heading);
            y -= title * 1.7;
            Note(doc, view, bodyType, x0, y, z, subtitle);
            y -= body * 2.6;

            foreach (TrenchLegendRow row in result.Legend)
            {
                double top = y + 0.15 * body;
                (double X, double Y)[] swatch =
                {
                    (x0, top), (x0 + swatchW, top), (x0 + swatchW, top - swatchH), (x0, top - swatchH),
                };
                try
                {
                    FilledRegion region = CreateRegion(doc, view, types[row.Class], swatch, plane);
                    if (see != null)
                    {
                        try
                        {
                            view.SetElementOverrides(region.Id, see);
                        }
                        catch (Autodesk.Revit.Exceptions.ApplicationException)
                        {
                            // Solid, as the trench is.
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    if (result.FirstProblem == null) result.FirstProblem = ex.Message;
                }

                Note(doc, view, bodyType, x0 + swatchW + body, y, z, row.Text);
                y -= rowH;
            }

            string total = "Total trench: " + TrenchText.Feet(plan.TrenchLength);
            if (noFloorLength > 0.01) total += "\nNot shown (no floor over it): " + TrenchText.Feet(noFloorLength);
            Note(doc, view, bodyType, x0, y - body * 0.4, z, total);
        }

        private static void Note(Document doc, View view, ElementId typeId, double x, double y, double z, string text)
        {
            TextNoteOptions options = new TextNoteOptions(typeId)
            {
                HorizontalAlignment = HorizontalTextAlignment.Left,
                VerticalAlignment = VerticalTextAlignment.Top,
            };
            TextNote.Create(doc, view.Id, new XYZ(x, y, z), text, options);
        }
    }
}
