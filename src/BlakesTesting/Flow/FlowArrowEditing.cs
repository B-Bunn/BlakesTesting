#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace BlakesTesting.Flow
{
    /// <summary>
    ///     Changes arrows already placed: turns them round, or changes their text. Revit will not edit a group's members
    ///     one by one (it warns "A group has been changed outside group edit mode"), so each group is taken apart, changed
    ///     and grouped again under the same name; that also gives a copied arrow a type of its own. Call it inside a transaction.
    /// </summary>
    internal static class FlowArrowEditing
    {
        // ------------------------------------------------------------------ flip

        /// <summary>
        ///     Points each arrow the other way, by turning its lines half a turn about their own middle. The text stays where
        ///     it is. Returns how many were flipped; <paramref name="skipped"/> counts arrows it could not tell the lines of
        ///     (placed in a 3D view before pieces were told apart: their text is lines too), and
        ///     <paramref name="ungrouped"/> the ones Revit would not group again.
        /// </summary>
        public static int Flip(Document doc, IList<Autodesk.Revit.DB.Group> groups, out int skipped, out int ungrouped)
        {
            skipped = 0;
            ungrouped = 0;
            int done = 0;
            foreach (Autodesk.Revit.DB.Group g in groups)
            {
                List<ElementId> arrowIds = g.GetMemberIds()
                    .Where(id => doc.GetElement(id) is CurveElement ce && IsArrowLine(ce))
                    .ToList();
                if (arrowIds.Count == 0)
                {
                    skipped++;
                    continue;
                }

                string name = g.GroupType.Name;
                string? note = FlowArrowDrawing.CommentOf(g);
                ElementId oldType = g.GroupType.Id;
                List<ElementId> members = g.UngroupMembers().ToList();

                List<CurveElement> lines = arrowIds.Select(id => (CurveElement)doc.GetElement(id)).ToList();
                XYZ min = new XYZ(double.MaxValue, double.MaxValue, double.MaxValue), max = new XYZ(double.MinValue, double.MinValue, double.MinValue);
                foreach (CurveElement l in lines)
                    for (int e = 0; e < 2; e++)
                    {
                        XYZ p = l.GeometryCurve.GetEndPoint(e);
                        min = new XYZ(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                        max = new XYZ(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
                    }

                XYZ centre = new XYZ((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);
                foreach (CurveElement l in lines)
                {
                    XYZ a = l.GeometryCurve.GetEndPoint(0), b = l.GeometryCurve.GetEndPoint(1);
                    l.SetGeometryCurve(Line.CreateBound(Through(centre, a), Through(centre, b)), true);
                }

                FlowArrowDrawing.DeleteTypeIfUnused(doc, oldType);
                if (!FlowArrowDrawing.Regroup(doc, members, name, note)) ungrouped++;
                done++;
            }

            return done;
        }

        private static XYZ Through(XYZ centre, XYZ p) => new XYZ(2 * centre.X - p.X, 2 * centre.Y - p.Y, 2 * centre.Z - p.Z);

        private static bool IsArrowLine(CurveElement ce)
        {
            ArrowRole role = FlowArrowDrawing.RoleOf(ce);
            return role == ArrowRole.Line || (role == ArrowRole.Old && ce is DetailCurve);
        }

        // ------------------------------------------------------------------ edit text

        /// <summary>
        ///     Gives each arrow this text (empty = no text). A 2D arrow's text note is edited, or removed. A 3D arrow's
        ///     strokes are replaced in the plane they were drawn in, at the same place and angle. An arrow that has no text
        ///     yet is skipped (nothing says where its text would go), and so is a 3D arrow placed before pieces were told apart.
        /// </summary>
        public static int SetText(Document doc, View view, IList<Autodesk.Revit.DB.Group> groups, string text, FlowArrowSettings settings, out int skipped, out int ungrouped)
        {
            skipped = 0;
            ungrouped = 0;
            int done = 0;
            text = FlowArrowSettings.CleanText(text);

            foreach (Autodesk.Revit.DB.Group g in groups)
            {
                List<Element> members = g.GetMemberIds().Select(id => doc.GetElement(id)).Where(e => e != null).ToList();
                TextNote? note = members.OfType<TextNote>().FirstOrDefault();
                List<CurveElement> strokes = members.OfType<CurveElement>().Where(c => FlowArrowDrawing.RoleOf(c) == ArrowRole.Text).ToList();
                List<ModelCurve> modelLines = members.OfType<ModelCurve>().Where(c => FlowArrowDrawing.RoleOf(c) == ArrowRole.Line).ToList();

                bool twoD = note != null;
                bool threeD = note == null && strokes.Count > 0 && modelLines.Count > 0;
                if (!twoD && !threeD)
                {
                    skipped++;
                    continue;
                }

                ElementId noteId = note?.Id ?? ElementId.InvalidElementId;
                List<ElementId> strokeIds = strokes.Select(c => c.Id).ToList();
                ElementId shaftId = threeD ? modelLines.OrderByDescending(c => c.GeometryCurve.Length).First().Id : ElementId.InvalidElementId;
                string name = g.GroupType.Name;
                string? comment = FlowArrowDrawing.CommentOf(g);
                ElementId oldType = g.GroupType.Id;
                List<ElementId> ids = g.UngroupMembers().ToList();

                if (twoD)
                {
                    if (text.Length == 0)
                    {
                        doc.Delete(noteId);
                        ids.Remove(noteId);
                    }
                    else if (doc.GetElement(noteId) is TextNote n)
                    {
                        n.Text = text;
                    }
                }
                else
                {
                    ReplaceStrokes(doc, view, (ModelCurve)doc.GetElement(shaftId), strokeIds, ids, text, settings);
                }

                FlowArrowDrawing.DeleteTypeIfUnused(doc, oldType);
                if (!FlowArrowDrawing.Regroup(doc, ids, name, comment)) ungrouped++;
                done++;
            }

            return done;
        }

        private static void ReplaceStrokes(Document doc, View view, ModelCurve shaft, List<ElementId> strokeIds, List<ElementId> ids, string text, FlowArrowSettings settings)
        {
            SketchPlane sketch = shaft.SketchPlane;
            Plane plane = sketch.GetPlane();
            XYZ origin = plane.Origin, x = plane.XVec, y = plane.YVec;

            // Where the old text was, read off its strokes: their middle, and how tall they stand.
            List<XYZ> points = new List<XYZ>();
            GraphicsStyle? style = null;
            foreach (ElementId id in strokeIds)
                if (doc.GetElement(id) is CurveElement c)
                {
                    points.Add(c.GeometryCurve.GetEndPoint(0));
                    points.Add(c.GeometryCurve.GetEndPoint(1));
                    style ??= c.LineStyle as GraphicsStyle;
                }

            double angle = 0;
            if (settings.TextReading == FlowArrowTextReading.WithPipe)
            {
                XYZ d = shaft.GeometryCurve.GetEndPoint(1) - shaft.GeometryCurve.GetEndPoint(0);
                angle = Math.Atan2(d.DotProduct(y), d.DotProduct(x));
                if (angle > Math.PI / 2 + 1e-9) angle -= Math.PI;
                else if (angle <= -Math.PI / 2 + 1e-9) angle += Math.PI;
            }

            double scale = view.Scale > 0 ? view.Scale : 96;
            double height = FlowArrowLayout.PaperToFeet(settings.TextSizeInches, scale);
            double minX = points.Min(p => (p - origin).DotProduct(x)), maxX = points.Max(p => (p - origin).DotProduct(x));
            double minY = points.Min(p => (p - origin).DotProduct(y)), maxY = points.Max(p => (p - origin).DotProduct(y));
            V2 centre = new V2((minX + maxX) / 2, (minY + maxY) / 2);

            // How tall the old text stood, across its own direction, when that is a sensible size.
            XYZ up = x.Multiply(-Math.Sin(angle)) + y.Multiply(Math.Cos(angle));
            double measured = points.Max(p => (p - origin).DotProduct(up)) - points.Min(p => (p - origin).DotProduct(up));
            if (measured >= 0.6 * height && measured <= 1.6 * height) height = measured;

            doc.Delete(strokeIds);
            foreach (ElementId id in strokeIds) ids.Remove(id);
            if (text.Length == 0) return;

            foreach (Seg s in StrokeFont.Layout(text, height, centre, angle))
            {
                if (s.Length < 0.0026) continue;
                XYZ from = origin + x.Multiply(s.A.X) + y.Multiply(s.A.Y);
                XYZ to = origin + x.Multiply(s.B.X) + y.Multiply(s.B.Y);
                (double X, double Y, double Z) snapped = AxisSnap.SnapEnd((from.X, from.Y, from.Z), (to.X, to.Y, to.Z));
                ModelCurve curve = doc.Create.NewModelCurve(Line.CreateBound(from, new XYZ(snapped.X, snapped.Y, snapped.Z)), sketch);
                if (style != null) curve.LineStyle = style;
                FlowArrowDrawing.SetComment(curve, FlowArrowDrawing.MarkText);
                ids.Add(curve.Id);
            }
        }
    }
}
