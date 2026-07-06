// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using Xunit;

namespace TestBetta
{
    public enum SampleKind
    {
        None = 0,
        Walls = 1,
        Doors = 2
    }

    public class TestEnumInputs
    {
        // Private helper that mirrors ParamInjector.ChangeTypeStrong. We reach
        // it via reflection so we don't need to expose internal-only API.
        private static object ChangeTypeStrong(object value, System.Type target)
        {
            var t = typeof(Betta.ParamInjector);
            var m = t.GetMethod("ChangeTypeStrong",
                BindingFlags.NonPublic | BindingFlags.Static);
            return m.Invoke(null, new[] { value, target });
        }

        [Fact]
        public void Int_ConvertsToEnum()
        {
            var result = ChangeTypeStrong(1, typeof(SampleKind));
            Assert.IsType<SampleKind>(result);
            Assert.Equal(SampleKind.Walls, (SampleKind)result);
        }

        [Fact]
        public void Short_ConvertsToEnum()
        {
            var result = ChangeTypeStrong((short)2, typeof(SampleKind));
            Assert.Equal(SampleKind.Doors, (SampleKind)result);
        }

        [Fact]
        public void String_ConvertsToEnum_CaseInsensitive()
        {
            var result = ChangeTypeStrong("walls", typeof(SampleKind));
            Assert.Equal(SampleKind.Walls, (SampleKind)result);
        }

        [Fact]
        public void EnumValue_PassesThroughUnchanged()
        {
            var result = ChangeTypeStrong(SampleKind.Doors, typeof(SampleKind));
            Assert.Equal(SampleKind.Doors, (SampleKind)result);
        }

        [Fact]
        public void InvalidString_FallsBackToDefault()
        {
            // Unparseable string should not throw out of ChangeTypeStrong — it
            // returns the default (0) value of the enum so SolveInstance can
            // continue rather than crashing the whole component.
            var result = ChangeTypeStrong("not-an-enum-member", typeof(SampleKind));
            Assert.Equal(SampleKind.None, (SampleKind)result);
        }
    }
}
