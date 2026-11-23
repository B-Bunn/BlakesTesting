#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using BlakesTesting.Risers;

namespace BlakesTesting.Flow
{
    /// <summary>
    ///     The view's plane as Flow Arrow sees it: an origin and the view's right and up directions. A plan, section,
    ///     elevation or detail view has a real plane and the arrow is drawn in it as view-specific detail lines and a text
    ///     note. A 3D view has none, so the arrow is drawn as model lines in a plane through the first click that faces the
    ///     camera (<see cref="IsModel"/>).
    /// </summary>
    internal sealed class FlowArrowFrame
    {
        private FlowArrowFrame(View view, bool isModel, XYZ origin, XYZ right, XYZ up, double scale)
        {
            View = view;
            IsModel = isModel;
            Origin = origin;
            Right = right;
            Up = up;
            Scale = scale;
        }

        public View View { get; }
        public bool IsModel { get; }
        public XYZ Origin { get; }
        public XYZ Right { get; }
        public XYZ Up { get; }

        /// <summary>The view's scale denominator (96 for 1/8" = 1'-0"). A 3D view has one too.</summary>
        public double Scale { get; }

        /// <summary>What is wrong with drawing in this view, or null.</summary>
        public static string? ViewProblem(View view)
        {
            if (view.IsTemplate) return "This is a view template. Open a plan, section, elevation or 3D view and run Flow Arrow again.";
            if (view is View3D || view is ViewPlan || view is ViewSection) return null;
            return "Flow Arrow works in a plan, section, elevation, detail or 3D view. Open one and run it again.";
        }

        /// <summary>The frame for an arrow whose first click is at <paramref name="click"/>.</summary>
        public static FlowArrowFrame Create(View view, XYZ click)
        {
            bool model = view is View3D;
            double scale = view.Scale > 0 ? view.Scale : 96;
            return new FlowArrowFrame(view, model, model ? click : view.Origin, view.RightDirection, view.UpDirection, scale);
        }

        public FlowArrowFrame WithOrigin(XYZ origin) => new FlowArrowFrame(View, IsModel, origin, Right, Up, Scale);

        public V2 To2D(XYZ p)
        {
            XYZ d = p - Origin;
            return new V2(d.DotProduct(Right), d.DotProduct(Up));
        }

        public XYZ To3D(V2 v) => Origin + Right.Multiply(v.X) + Up.Multiply(v.Y);
    }

    /// <summary>What a drawn arrow left in the model.</summary>
    internal sealed class FlowArrowDrawn
    {
        public List<ElementId> Ids { get; } = new List<ElementId>();
        public bool Grouped { get; set; }
        public string? GroupName { get; set; }
        public int Skipped { get; set; }
    }

    /// <summary>Draws a <see cref="FlowArrowShape"/> in Revit, and finds the arrows again. Call it inside a transaction.</summary>
    internal static class FlowArrowDrawing
    {
        /// <summary>The start of every group type this makes: Clear Flow Arrows finds them by it.</summary>
        public const string GroupPrefix = "Flow Arrow";

        /// <summary>Written into every element's Comments, so a stray piece (an arrow that could not be grouped) is found too.</summary>
        public const string Mark = "Flow Arrow";

        private const string TextTypePrefix = "Flow Arrow Text";

        /// <summary>Line.CreateBound refuses anything shorter than this (about 1/32").</summary>
        private const double ShortestCurve = 0.0026;

        public static FlowArrowDrawn Draw(Document doc, FlowArrowFrame frame, FlowArrowShape shape, FlowArrowSettings settings)
        {
            FlowArrowDrawn drawn = new FlowArrowDrawn();
            GraphicsStyle? lineStyle = settings.Color.HasValue
                ? ColoredLineStyle(doc, settings.LineStyle, settings.Color.Value)
                : FindLineStyle(doc, settings.LineStyle);

            if (frame.IsModel) DrawModel(doc, frame, shape, lineStyle, drawn);
            else DrawAnnotation(doc, frame, shape, settings, lineStyle, drawn);

            if (drawn.Ids.Count == 0)
                throw new InvalidOperationException("Revit drew nothing: the arrow came out smaller than it will draw. Make the arrow longer in the Flow Arrow window, or use a larger view scale.");

            foreach (ElementId id in drawn.Ids) Mark1(doc.GetElement(id));
            Group(doc, drawn);
            return drawn;
        }

        // ------------------------------------------------------------------ 2D: detail lines and a text note

        private static void DrawAnnotation(Document doc, FlowArrowFrame frame, FlowArrowShape shape, FlowArrowSettings settings, GraphicsStyle? lineStyle, FlowArrowDrawn drawn)
        {
            View view = frame.View;

            // Revit wants the curve IN the view's plane. A plan's plane is at the level, the view's origin or (for a
            // view whose origin is elsewhere) zero: try each until the first line is accepted, then keep it.
            List<XYZ> origins = new List<XYZ>();
            if (view is ViewPlan && view.GenLevel != null)
                origins.Add(new XYZ(frame.Origin.X, frame.Origin.Y, view.GenLevel.ProjectElevation));
            origins.Add(frame.Origin);
            if (view is ViewPlan) origins.Add(new XYZ(frame.Origin.X, frame.Origin.Y, 0));

            Seg[] lines = shape.Lines.Where(s => s.Length >= ShortestCurve).ToArray();
            drawn.Skipped = shape.Lines.Count - lines.Length;

            FlowArrowFrame f = frame;
            int next = 0;
            if (lines.Length > 0)
            {
                Exception? last = null;
                bool placedFirst = false;
                foreach (XYZ origin in origins)
                {
                    f = frame.WithOrigin(origin);
                    try
                    {
                        drawn.Ids.Add(MakeDetail(doc, view, f, lines[0], lineStyle));
                        placedFirst = true;
                        next = 1;
                        break;
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                    {
                        last = ex;
                    }
                }

                if (!placedFirst) throw new InvalidOperationException("Revit would not draw a line in this view" + (last != null ? " (" + last.Message + ")" : "") + ".");
            }

            for (int i = next; i < lines.Length; i++)
                drawn.Ids.Add(MakeDetail(doc, view, f, lines[i], lineStyle));

            if (shape.Label.Length > 0)
            {
                ElementId type = TextType(doc, settings.TextSizeInches, settings.Color);
                TextNoteOptions options = new TextNoteOptions(type)
                {
                    HorizontalAlignment = HorizontalTextAlignment.Center,
                    VerticalAlignment = VerticalTextAlignment.Middle,
                    Rotation = shape.LabelAngle,
                };
                TextNote note = TextNote.Create(doc, view.Id, f.To3D(shape.LabelCenter), shape.Label, options);
                drawn.Ids.Add(note.Id);
            }
        }

        private static ElementId MakeDetail(Document doc, View view, FlowArrowFrame f, Seg s, GraphicsStyle? style)
        {
            DetailCurve curve = doc.Create.NewDetailCurve(view, Line.CreateBound(f.To3D(s.A), f.To3D(s.B)));
            if (style != null) curve.LineStyle = style;
            return curve.Id;
        }

        private static ElementId TextType(Document doc, double inches, RgbColor? color)
        {
            string name = TextTypePrefix + " " + inches.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
                          + "in" + (color.HasValue ? " " + color.Value.ToHex() : "");
            TextNoteType? type = FindTextType(doc, name);
            if (type != null) return type.Id;

            ElementId defaultId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            TextNoteType baseType = (doc.GetElement(defaultId) as TextNoteType)
                                    ?? new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault()
                                    ?? throw new InvalidOperationException("This project has no text type to make the arrow's text from.");
            type = (TextNoteType)baseType.Duplicate(name);
            type.get_Parameter(BuiltInParameter.TEXT_SIZE)?.Set(inches / 12.0);
            type.get_Parameter(BuiltInParameter.TEXT_BACKGROUND)?.Set(1); // transparent: a pipe shows through
            type.get_Parameter(BuiltInParameter.TEXT_BOX_VISIBILITY)?.Set(0); // no border
            if (color.HasValue) type.get_Parameter(BuiltInParameter.TEXT_COLOR)?.Set(ColorInt(color.Value));
            return type.Id;
        }

        private static TextNoteType? FindTextType(Document doc, string name) =>
            new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
                .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

        /// <summary>Revit stores a color as one integer, red lowest.</summary>
        private static int ColorInt(RgbColor c) => c.R | (c.G << 8) | (c.B << 16);

        private static readonly Regex ColorSuffix = new Regex(" #[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

        // ------------------------------------------------------------------ 3D: model lines, the text as strokes

        private static void DrawModel(Document doc, FlowArrowFrame frame, FlowArrowShape shape, GraphicsStyle? lineStyle, FlowArrowDrawn drawn)
        {
            SketchPlane plane = SketchPlane.Create(doc, Plane.CreateByOriginAndBasis(frame.Origin, frame.Right, frame.Up));
            List<Seg> all = new List<Seg>(shape.Lines);
            if (shape.Label.Length > 0) all.AddRange(StrokeFont.Layout(shape.Label, shape.LabelHeight, shape.LabelCenter, shape.LabelAngle));

            foreach (Seg s in all)
            {
                if (s.Length < ShortestCurve)
                {
                    drawn.Skipped++;
                    continue;
                }

                // A line a hair off an axis (a vertical pipe's arrow in a camera that is not exactly upright) makes Revit warn
                // "Line is slightly off axis": put it exactly on the axis.
                XYZ from = frame.To3D(s.A), to = frame.To3D(s.B);
                (double X, double Y, double Z) snapped = AxisSnap.SnapEnd((from.X, from.Y, from.Z), (to.X, to.Y, to.Z));
                ModelCurve curve = doc.Create.NewModelCurve(Line.CreateBound(from, new XYZ(snapped.X, snapped.Y, snapped.Z)), plane);
                if (lineStyle != null) curve.LineStyle = lineStyle;
                drawn.Ids.Add(curve.Id);
            }
        }

        // ------------------------------------------------------------------ grouping, marking, finding

        private static void Mark1(Element? e)
        {
            e?.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(Mark);
        }

        private static void Group(Document doc, FlowArrowDrawn drawn)
        {
            if (drawn.Ids.Count < 2) return;
            try
            {
                Autodesk.Revit.DB.Group group = doc.Create.NewGroup(drawn.Ids);
                string name = UniqueGroupName(doc);
                group.GroupType.Name = name;
                drawn.Grouped = true;
                drawn.GroupName = name;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Left as loose lines and text: still found by Clear Flow Arrows through the Comments mark.
            }
        }

        private static string UniqueGroupName(Document doc)
        {
            HashSet<string> taken = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(GroupType)).Select(t => t.Name), StringComparer.Ordinal);
            int n = taken.Count(t => t.StartsWith(GroupPrefix, StringComparison.Ordinal)) + 1;
            while (taken.Contains(GroupPrefix + " " + n)) n++;
            return GroupPrefix + " " + n;
        }

        private static GraphicsStyle? FindLineStyle(Document doc, string name)
        {
            try
            {
                Category? lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                if (lines == null) return null;
                foreach (Category sub in lines.SubCategories)
                    if (string.Equals(sub.Name, name, StringComparison.OrdinalIgnoreCase))
                        return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // The style is only a courtesy: the arrow draws in the default line style without it.
            }

            return null;
        }

        /// <summary>
        ///     A line style named "Flow Arrow #RRGGBB" in that color, made the first time it is asked for, with the base
        ///     style's line weight and pattern. A line style, not a view override, so it holds in every view (a 3D arrow is
        ///     a model element) and for the text-free parts alike.
        /// </summary>
        private static GraphicsStyle? ColoredLineStyle(Document doc, string baseName, RgbColor color)
        {
            try
            {
                Categories cats = doc.Settings.Categories;
                Category? lines = cats.get_Item(BuiltInCategory.OST_Lines);
                if (lines == null) return FindLineStyle(doc, baseName);

                string name = GroupPrefix + " " + color.ToHex();
                Category? sub = null;
                Category? basis = null;
                foreach (Category c in lines.SubCategories)
                {
                    if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) sub = c;
                    if (string.Equals(c.Name, baseName, StringComparison.OrdinalIgnoreCase)) basis = c;
                }

                if (sub == null)
                {
                    sub = cats.NewSubcategory(lines, name);
                    sub.LineColor = new Color(color.R, color.G, color.B);
                    sub.SetLineWeight(basis?.GetLineWeight(GraphicsStyleType.Projection) ?? 3, GraphicsStyleType.Projection);
                    ElementId? pattern = basis?.GetLinePatternId(GraphicsStyleType.Projection);
                    if (pattern != null && pattern != ElementId.InvalidElementId) sub.SetLinePatternId(pattern, GraphicsStyleType.Projection);
                }

                return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return FindLineStyle(doc, baseName);
            }
        }

        /// <summary>
        ///     Puts <paramref name="color"/> (null = the line style's own) on every line and text in the arrows. A group's
        ///     members are not editable one by one, so each group is taken apart, recolored and grouped again under the same
        ///     name. Returns how many arrows were recolored and how many ended up ungrouped because Revit refused to group
        ///     them again (still found by Clear Flow Arrows through their Comments).
        /// </summary>
        public static int Recolor(Document doc, IList<Autodesk.Revit.DB.Group> groups, IList<ElementId> loose, RgbColor? color, string lineStyleName, out int ungrouped)
        {
            ungrouped = 0;
            GraphicsStyle? style = color.HasValue
                ? ColoredLineStyle(doc, lineStyleName, color.Value)
                : FindLineStyle(doc, lineStyleName) ?? doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines)?.GetGraphicsStyle(GraphicsStyleType.Projection);

            int done = 0;
            foreach (Autodesk.Revit.DB.Group g in groups)
            {
                string name = g.GroupType.Name;
                ElementId oldType = g.GroupType.Id;
                List<ElementId> members = g.UngroupMembers().ToList();
                ApplyColor(doc, members, color, style);

                try
                {
                    if (doc.GetElement(oldType) is GroupType t && t.Groups.Size == 0) doc.Delete(oldType);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // An old type left over is only clutter.
                }

                try
                {
                    Autodesk.Revit.DB.Group again = doc.Create.NewGroup(members);
                    again.GroupType.Name = IsGroupNameFree(doc, name) ? name : UniqueGroupName(doc);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    ungrouped++;
                }

                done++;
            }

            if (loose.Count > 0)
            {
                ApplyColor(doc, loose, color, style);
                done += 1;
            }

            return done;
        }

        private static bool IsGroupNameFree(Document doc, string name) =>
            !new FilteredElementCollector(doc).OfClass(typeof(GroupType)).Any(t => string.Equals(t.Name, name, StringComparison.Ordinal));

        private static void ApplyColor(Document doc, IEnumerable<ElementId> ids, RgbColor? color, GraphicsStyle? style)
        {
            foreach (ElementId id in ids)
            {
                Element? e = doc.GetElement(id);
                if (e is CurveElement curve)
                {
                    if (style != null) curve.LineStyle = style;
                }
                else if (e is TextNote note)
                {
                    note.ChangeTypeId(TextTypeFor(doc, note.TextNoteType, color));
                }
            }
        }

        /// <summary>The note's own type, in the color: the same size, found or made under the same name plus the color.</summary>
        private static ElementId TextTypeFor(Document doc, TextNoteType current, RgbColor? color)
        {
            string baseName = ColorSuffix.Replace(current.Name, "");
            string name = color.HasValue ? baseName + " " + color.Value.ToHex() : baseName;
            TextNoteType? found = FindTextType(doc, name);
            if (found != null) return found.Id;

            TextNoteType made = (TextNoteType)current.Duplicate(name);
            made.get_Parameter(BuiltInParameter.TEXT_COLOR)?.Set(color.HasValue ? ColorInt(color.Value) : 0);
            return made.Id;
        }

        /// <summary>
        ///     Keeps only what the person has selected, when they have selected some arrows. True when it did.
        /// </summary>
        public static bool Restrict(ICollection<ElementId> selected, ref List<Autodesk.Revit.DB.Group> groups, ref List<ElementId> loose)
        {
            if (selected.Count == 0) return false;
            HashSet<ElementId> set = new HashSet<ElementId>(selected);
            List<Autodesk.Revit.DB.Group> g = groups.FindAll(x => set.Contains(x.Id));
            List<ElementId> l = loose.FindAll(set.Contains);
            if (g.Count + l.Count == 0) return false;
            groups = g;
            loose = l;
            return true;
        }

        /// <summary>The groups of arrows that show in the view, and the loose pieces of any that could not be grouped.</summary>
        public static (List<Autodesk.Revit.DB.Group> Groups, List<ElementId> Loose) Find(Document doc, View view)
        {
            List<Autodesk.Revit.DB.Group> groups = new FilteredElementCollector(doc, view.Id).OfClass(typeof(Autodesk.Revit.DB.Group))
                .Cast<Autodesk.Revit.DB.Group>()
                .Where(g => g.GroupType != null && g.GroupType.Name.StartsWith(GroupPrefix, StringComparison.Ordinal))
                .ToList();

            List<ElementId> loose = new List<ElementId>();
            foreach (Type t in new[] { typeof(CurveElement), typeof(TextNote) })
                foreach (Element e in new FilteredElementCollector(doc, view.Id).OfClass(t))
                {
                    if (e.GroupId != ElementId.InvalidElementId) continue;
                    if (e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() == Mark) loose.Add(e.Id);
                }

            return (groups, loose);
        }
    }
}
