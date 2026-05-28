// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Betta.Attributes;
using Betta.Interfaces;
using Rhino.Geometry;

namespace Betta.Tour
{
    /// <summary>
    /// Tour collection 3 of 3 — <b>Advanced</b>. Rhino geometry types, a
    /// geometry-in/multi-out component, a custom embedded icon, and an async
    /// <c>Task&lt;T&gt;</c> result. See also <see cref="ITourBasics"/> and
    /// <see cref="ITourIntermediate"/>.
    /// </summary>
    [GrasshopperCollection("Tour", "3 Advanced")]
    public interface ITourAdvanced : IBettaCollection
    {
        // Rhino types map to native GH params automatically:
        // Point3d→Point, Circle→Circle, Line→Line, and so on.
        [GrasshopperMethod("Midpoint", "Point halfway between two points")]
        Point3d Midpoint(
            [GrasshopperParameter("A", "A", "First point")] Point3d a,
            [GrasshopperParameter("B", "B", "Second point")] Point3d b);

        [GrasshopperMethod("Circle", "Circle on the world-XY plane at a center")]
        Circle CircleAt(
            [GrasshopperParameter("Center", "C", "Center point")] Point3d center,
            [GrasshopperParameter("Radius", "R", "Radius", DefaultValue = 1.0)] double radius);

        [GrasshopperMethod("Divide Circle", "Equally spaced points around a circle")]
        List<Point3d> DivideCircle(
            [GrasshopperParameter("Center", "C", "Center point")] Point3d center,
            [GrasshopperParameter("Radius", "R", "Radius", DefaultValue = 1.0)] double radius,
            [GrasshopperParameter("Count", "N", "Number of points", DefaultValue = 12)] int count);

        // Geometry input → multiple outputs: measure a line.
        [GrasshopperMethod("Line Info", "Midpoint and length of a line")]
        (Point3d Mid, double Length) LineInfo(
            [GrasshopperParameter("Line", "L", "The line to measure")] Line line);

        // The works: a custom embedded icon (ships its own PNG) and an async
        // Task<T> result — the solver kicks the task off, caches by input hash,
        // and re-solves when it completes (no blocking the GH solver thread).
        [GrasshopperMethod("Slow Square", "x² after a short delay (async demo)",
            IconResource = "tour_async.png")]
        Task<double> SlowSquareAsync(
            [GrasshopperParameter("Value", "X", "Number to square", DefaultValue = 4.0)] double x);
    }

    public class TourAdvanced : ITourAdvanced
    {
        public Point3d Midpoint(Point3d a, Point3d b) =>
            new Point3d((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0, (a.Z + b.Z) / 2.0);

        public Circle CircleAt(Point3d center, double radius) =>
            new Circle(new Plane(center, Vector3d.ZAxis), radius);

        public List<Point3d> DivideCircle(Point3d center, double radius, int count)
        {
            var points = new List<Point3d>();
            if (count <= 0 || radius <= 0) return points;
            for (int i = 0; i < count; i++)
            {
                double angle = 2.0 * Math.PI * i / count;
                points.Add(new Point3d(
                    center.X + radius * Math.Cos(angle),
                    center.Y + radius * Math.Sin(angle),
                    center.Z));
            }
            return points;
        }

        public (Point3d Mid, double Length) LineInfo(Line line) =>
            (line.PointAt(0.5), line.Length);

        public async Task<double> SlowSquareAsync(double x)
        {
            await Task.Delay(750);
            return x * x;
        }
    }
}
