// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace TestBetta
{
    /// <summary>
    /// Headless coverage for <c>ParamInjector.ChangeTypeStrong</c> — the input
    /// coercion the runtime applies before invoking a service method. Enum
    /// coercion lives in <see cref="TestEnumInputs"/>; this file covers the
    /// primitive, collection (List/array/variadic) and geometry-upcast branches.
    /// Reached via reflection (same pattern as TestEnumInputs) so we don't widen
    /// the production API. GH-free: no Grasshopper types, no RhinoCore.
    /// </summary>
    public class TestInputCoercion
    {
        private static object Coerce(object value, Type target)
        {
            var m = typeof(Betta.ParamInjector).GetMethod("ChangeTypeStrong",
                BindingFlags.NonPublic | BindingFlags.Static);
            return m.Invoke(null, new[] { value, target });
        }

        // --- Primitives -----------------------------------------------------

        [Fact]
        public void Int_WidensToDouble()
        {
            var result = Coerce(1, typeof(double));
            Assert.IsType<double>(result);
            Assert.Equal(1.0, (double)result);
        }

        [Fact]
        public void NumericString_ParsesToDouble()
        {
            // Integer-valued to stay culture-independent (a comma-decimal locale
            // would reject "3.5" and fall back to 0.0).
            var result = Coerce("42", typeof(double));
            Assert.Equal(42.0, (double)result);
        }

        [Fact]
        public void NumericString_ParsesToInt()
        {
            var result = Coerce("42", typeof(int));
            Assert.Equal(42, (int)result);
        }

        [Fact]
        public void Double_NarrowsToInt()
        {
            var result = Coerce(5.0, typeof(int));
            Assert.IsType<int>(result);
            Assert.Equal(5, (int)result);
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("false", false)]
        public void String_ParsesToBool(string input, bool expected)
        {
            Assert.Equal(expected, (bool)Coerce(input, typeof(bool)));
        }

        [Fact]
        public void AssignableValue_PassesThroughUnchanged()
        {
            var result = Coerce("already a string", typeof(string));
            Assert.Equal("already a string", result);
        }

        // --- Null handling --------------------------------------------------

        [Fact]
        public void Null_ToValueType_YieldsDefault()
        {
            var result = Coerce(null, typeof(int));
            Assert.Equal(0, (int)result);
        }

        [Fact]
        public void Null_ToReferenceType_YieldsNull()
        {
            Assert.Null(Coerce(null, typeof(string)));
        }

        // --- Collections: List<T> / T[] / params T[] ------------------------

        [Fact]
        public void ListOfObject_CoercesToTypedList()
        {
            var source = new List<object> { 1, 2, 3 };
            var result = Coerce(source, typeof(List<int>));

            var typed = Assert.IsType<List<int>>(result);
            Assert.Equal(new[] { 1, 2, 3 }, typed);
        }

        [Fact]
        public void ListOfStrings_CoercesElementwiseToTypedList()
        {
            var source = new List<object> { "1", "2", "3" };
            var result = Coerce(source, typeof(List<int>));

            Assert.Equal(new[] { 1, 2, 3 }, Assert.IsType<List<int>>(result));
        }

        [Fact]
        public void ListOfObject_CoercesToTypedArray()
        {
            // The T[] branch also backs `params T[]`: GH wires a single
            // list-access input and we build the typed array element by element.
            var source = new List<object> { "10", "20" };
            var result = Coerce(source, typeof(int[]));

            var arr = Assert.IsType<int[]>(result);
            Assert.Equal(new[] { 10, 20 }, arr);
        }

        [Fact]
        public void Scalar_ToArray_WrapsAsSingleElement()
        {
            var result = Coerce(7, typeof(int[]));
            Assert.Equal(new[] { 7 }, Assert.IsType<int[]>(result));
        }

        // NOTE: the geometry-upcast branch of ChangeTypeStrong
        // (Line/Arc/Circle/Polyline -> Curve, Surface -> Brep) is NOT covered
        // here. Those conversions call ToNurbsCurve()/ToBrep(), which invoke the
        // native Rhino kernel — unavailable without a live RhinoCore. Verifying
        // them headlessly throws inside the SUT, so they belong with the
        // deferred GH-bound surface (the in-process Rhino fixture), not this
        // pure-reflection file.
    }
}
