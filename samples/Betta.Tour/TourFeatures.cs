// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Betta.Attributes;
using Betta.Interfaces;
using Betta.Preview;
using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Rhino.Display;
using Rhino.Geometry;

namespace Betta.Tour
{
    // -------------------------------------------------------------------
    // Tour collection 5 of 5 — New Features in Betta 0.4.0.
    //
    // Demonstrates every feature added in this release in one place:
    //   1. Opaque pass-through              ([GrasshopperOpaque] / IBettaValue)
    //   2. List-of-opaque outputs           (Sample returns List<Curve3>)
    //   3. IBettaModule + ctor DI           (IRandomService injected below)
    //   4. Enum input                       (Mode is a CurveMode dropdown)
    //   5. Viewport preview                 (Curve3 implements IBettaPreview)
    //
    // See the prior tour collections for the basics (Tour/1..4).
    // -------------------------------------------------------------------

    /// <summary>
    /// A toy domain object — three points and the polyline through them.
    /// Marked opaque so it flows as a single typed wire, and previewable so
    /// the wrapping component draws it in the viewport.
    /// </summary>
    [GrasshopperOpaque]
    public class Curve3 : IBettaPreview
    {
        public Point3d A { get; set; }
        public Point3d B { get; set; }
        public Point3d C { get; set; }

        public BoundingBox ClippingBox
        {
            get
            {
                var bb = BoundingBox.Empty;
                bb.Union(A); bb.Union(B); bb.Union(C);
                return bb;
            }
        }

        public void DrawWires(IGH_PreviewArgs args)
        {
            args.Display.DrawLine(A, B, args.WireColour_Selected, 2);
            args.Display.DrawLine(B, C, args.WireColour_Selected, 2);
        }

        public void DrawMeshes(IGH_PreviewArgs args)
        {
            args.Display.DrawPoint(A, PointStyle.RoundSimple, 4, args.WireColour_Selected);
            args.Display.DrawPoint(B, PointStyle.RoundSimple, 4, args.WireColour_Selected);
            args.Display.DrawPoint(C, PointStyle.RoundSimple, 4, args.WireColour_Selected);
        }
    }

    /// <summary>
    /// A plain service registered by the module below. Demonstrates that the
    /// Tour assembly can hand any ctor-injected dependency to its collection
    /// classes — no static service locator, no hand-instantiation.
    /// </summary>
    public interface IRandomService { Vector3d Wiggle(double seed); }

    public class RandomService : IRandomService
    {
        public Vector3d Wiggle(double seed) =>
            new Vector3d(System.Math.Sin(seed), System.Math.Cos(seed), 0) * 0.25;
    }

    /// <summary>
    /// IBettaModule implementor — Betta finds this at startup (own + plugin
    /// assemblies) and invokes ConfigureServices before BuildServiceProvider,
    /// so the registrations below are visible to the DI container that
    /// builds collection instances.
    /// </summary>
    public class TourModule : IBettaModule
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IRandomService, RandomService>();
        }
    }

    public enum CurveMode
    {
        Straight = 0,
        Wiggled = 1,
        Star = 2,
    }

    [GrasshopperCollection("Tour", "5 New Features")]
    public class TourFeatures : IBettaCollection
    {
        // Injected by the DI container the module above populated. The class
        // is self-registered as a singleton by AutoRegisterServices (a class
        // is assignable from itself), so this ctor runs once per session.
        private readonly IRandomService _random;
        public TourFeatures(IRandomService random) { _random = random; }

        // (1) Opaque pass-through: the return type is a single typed wire.
        // (4) Enum input: Mode reads as a CurveMode dropdown / integer.
        [GrasshopperMethod("Make Curve3", "Build a three-point curve from a seed")]
        public Curve3 MakeCurve(
            [GrasshopperParameter("Seed", DefaultValue = 1.0)] double seed,
            [GrasshopperParameter("Mode")] CurveMode mode)
        {
            var origin = Point3d.Origin;
            switch (mode)
            {
                case CurveMode.Straight:
                    return new Curve3 { A = origin, B = new Point3d(1, 0, 0), C = new Point3d(2, 0, 0) };
                case CurveMode.Wiggled:
                    var w = _random.Wiggle(seed);
                    return new Curve3 { A = origin, B = new Point3d(1, w.X, 0), C = new Point3d(2, w.Y, 0) };
                case CurveMode.Star:
                default:
                    return new Curve3 { A = origin, B = new Point3d(0.5, 1, 0), C = new Point3d(1, 0, 0) };
            }
        }

        // (1) Opaque input: Curve3 arrives as a single typed wire.
        // Tuple return with named elements → two named outputs.
        [GrasshopperMethod("Deconstruct Curve3", "Pull out the three points")]
        public (Point3d A, Point3d B, Point3d C) Deconstruct(
            [GrasshopperParameter("Curve")] Curve3 curve)
        {
            return (curve.A, curve.B, curve.C);
        }

        // (2) List-of-opaque output: a single list wire of Curve3.
        // (4) Enum input again.
        [GrasshopperMethod("Sample Curves", "Build a list of curves from a list of seeds")]
        public List<Curve3> Sample(
            [GrasshopperParameter("Seeds")] List<double> seeds,
            [GrasshopperParameter("Mode")] CurveMode mode)
        {
            var result = new List<Curve3>();
            if (seeds == null) return result;
            foreach (var s in seeds) result.Add(MakeCurve(s, mode));
            return result;
        }
    }
}
