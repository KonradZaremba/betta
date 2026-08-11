// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Betta;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;
using Xunit;

namespace TestBetta
{
    /// <summary>
    /// Headless coverage for <see cref="ParamVector.GetGhParamType"/> — the
    /// CLR-type → IGH_Param mapping. Constructing Param_* types loads
    /// Grasshopper but needs no running RhinoCore (same as TestParamRegistry
    /// instantiating Param_BettaGoo&lt;T&gt;). Locks the v0.5/v0.6 additions and
    /// the Bitmap/Image "don't explode into properties" regression.
    /// </summary>
    public class TestParamVector
    {
        private static IGH_Param Map(Type t) => ParamVector.GetGhParamType(t);

        [Theory]
        // Primitives (via GetGhParamGenericType)
        [InlineData(typeof(double), typeof(Param_Number))]
        [InlineData(typeof(int), typeof(Param_Integer))]
        [InlineData(typeof(string), typeof(Param_String))]
        [InlineData(typeof(bool), typeof(Param_Boolean))]
        // Baseline geometry (FullName switch)
        [InlineData(typeof(Point3d), typeof(Param_Point))]
        [InlineData(typeof(Circle), typeof(Param_Circle))]
        [InlineData(typeof(Curve), typeof(Param_Curve))]
        // v0.5/v0.6 additions
        [InlineData(typeof(BoundingBox), typeof(Param_Box))]
        [InlineData(typeof(Interval), typeof(Param_Interval))]
        [InlineData(typeof(Transform), typeof(Param_Transform))]
        [InlineData(typeof(System.Drawing.Color), typeof(Param_Colour))]
        [InlineData(typeof(Guid), typeof(Param_Guid))]
        public void GetGhParamType_MapsToExpectedParam(Type clrType, Type expectedParam)
        {
            Assert.IsType(expectedParam, Map(clrType));
        }

        [Theory]
        [InlineData(typeof(System.Drawing.Bitmap))]
        [InlineData(typeof(System.Drawing.Image))]
        public void BitmapAndImage_MapToGenericObject_NotExploded(Type imageType)
        {
            // Regression: without this branch OutputPlanner would fan a bitmap
            // out into Width/Height/Palette/... — it must flow as a single wire.
            Assert.IsType<Param_GenericObject>(Map(imageType));
        }

        [Fact]
        public void Polyline_MapsToCurveParam()
        {
            // Polyline has no dedicated GH param; it surfaces as a curve socket.
            Assert.IsType<Param_Curve>(Map(typeof(Polyline)));
        }

        [Fact]
        public void UnknownReferenceType_FallsBackToGenericObject()
        {
            Assert.IsType<Param_GenericObject>(Map(typeof(TestParamVector)));
        }
    }
}
