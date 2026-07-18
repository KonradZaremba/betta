// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Betta.Attributes;
using Betta.Interfaces;
using Betta.Services;
using Xunit;

namespace TestBetta
{
    /// <summary>
    /// Custom validators exercised through [GrasshopperValidation]. Each needs a
    /// public parameterless ctor — the runtime builds one per solve.
    /// </summary>
    public class EvenNumberValidator : IBettaValidator
    {
        public string Validate(object value) =>
            value is int i && i % 2 != 0 ? $"{i} is not even" : null;
    }

    public class AlwaysPassesValidator : IBettaValidator
    {
        public string Validate(object value) => null;
    }

    public class ThrowingValidator : IBettaValidator
    {
        public string Validate(object value) => throw new InvalidOperationException("boom");
    }

    /// <summary>
    /// Methods whose parameters carry the validation attributes. ParamValidator
    /// reads ParameterInfo, so these are never invoked — only reflected over.
    /// </summary>
    public class ValidationSubjects
    {
        public void Ranged([GrasshopperRange(1, 10)] double value) { }

        public void NotEmptyText([GrasshopperNotEmpty] string text) { }

        public void NotEmptyList([GrasshopperNotEmpty] List<int> items) { }

        public void Custom([GrasshopperValidation(typeof(EvenNumberValidator))] int number) { }

        public void CustomPassing([GrasshopperValidation(typeof(AlwaysPassesValidator))] int number) { }

        public void CustomThrows([GrasshopperValidation(typeof(ThrowingValidator))] int number) { }

        public void Unvalidated(double value) { }

        // Both parameters fail; Validate must report the first one only.
        public void TwoBadParams(
            [GrasshopperRange(1, 10)] double first,
            [GrasshopperNotEmpty] string second) { }

        // A range on a non-numeric parameter must not throw.
        public void RangeOnString([GrasshopperRange(1, 10)] string text) { }
    }

    /// <summary>
    /// ParamValidator is GH-free (it lives in Services/ and only reads
    /// ParameterInfo), so these run headlessly with no Rhino present.
    /// </summary>
    public class TestParamValidator
    {
        private static IList<ParameterInfo> ParamsOf(string method) =>
            typeof(ValidationSubjects).GetMethod(method).GetParameters().ToList();

        private static string Run(string method, params object[] args) =>
            ParamValidator.Validate(ParamsOf(method), args);

        [Theory]
        [InlineData(1.0)]   // inclusive lower bound
        [InlineData(5.0)]
        [InlineData(10.0)]  // inclusive upper bound
        public void Range_InsideBounds_Passes(double value)
        {
            Assert.Null(Run(nameof(ValidationSubjects.Ranged), value));
        }

        [Theory]
        [InlineData(0.99)]
        [InlineData(-5.0)]
        [InlineData(10.01)]
        public void Range_OutsideBounds_ReportsParamNameAndBounds(double value)
        {
            var msg = Run(nameof(ValidationSubjects.Ranged), value);

            Assert.NotNull(msg);
            Assert.Contains("value", msg);
            Assert.Contains("[1, 10]", msg);
        }

        [Fact]
        public void Range_NullValue_IsSkipped()
        {
            // A null arrives when nothing is wired; the range check only applies
            // to actual values, so this is not the validator's failure to report.
            Assert.Null(Run(nameof(ValidationSubjects.Ranged), new object[] { null }));
        }

        [Fact]
        public void Range_OnNonNumericValue_DoesNotThrow()
        {
            // Convert.ToDouble("abc") throws inside the validator; it must be
            // swallowed and left to the method body rather than failing the solve.
            var ex = Record.Exception(() => Run(nameof(ValidationSubjects.RangeOnString), "abc"));
            Assert.Null(ex);
        }

        [Fact]
        public void NotEmpty_NullValue_Fails()
        {
            var msg = Run(nameof(ValidationSubjects.NotEmptyText), new object[] { null });

            Assert.NotNull(msg);
            Assert.Contains("no value was wired", msg);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void NotEmpty_BlankString_Fails(string text)
        {
            var msg = Run(nameof(ValidationSubjects.NotEmptyText), text);

            Assert.NotNull(msg);
            Assert.Contains("empty", msg);
        }

        [Fact]
        public void NotEmpty_NonBlankString_Passes()
        {
            Assert.Null(Run(nameof(ValidationSubjects.NotEmptyText), "betta"));
        }

        [Fact]
        public void NotEmpty_EmptyList_Fails()
        {
            var msg = Run(nameof(ValidationSubjects.NotEmptyList), new List<int>());

            Assert.NotNull(msg);
            Assert.Contains("list is empty", msg);
        }

        [Fact]
        public void NotEmpty_PopulatedList_Passes()
        {
            Assert.Null(Run(nameof(ValidationSubjects.NotEmptyList), new List<int> { 1 }));
        }

        [Fact]
        public void Custom_FailingValidator_PrefixesParamName()
        {
            var msg = Run(nameof(ValidationSubjects.Custom), 3);

            Assert.NotNull(msg);
            Assert.Contains("number", msg);
            Assert.Contains("not even", msg);
        }

        [Fact]
        public void Custom_PassingValidator_ReturnsNull()
        {
            Assert.Null(Run(nameof(ValidationSubjects.Custom), 4));
            Assert.Null(Run(nameof(ValidationSubjects.CustomPassing), 7));
        }

        [Fact]
        public void Custom_ThrowingValidator_IsReportedNotPropagated()
        {
            // A broken validator must degrade to a warning message, never take
            // down the solve.
            var msg = Run(nameof(ValidationSubjects.CustomThrows), 1);

            Assert.NotNull(msg);
            Assert.Contains(nameof(ThrowingValidator), msg);
            Assert.Contains("threw", msg);
        }

        [Fact]
        public void Validate_ReportsFirstFailureOnly()
        {
            // first (range) and second (not-empty) both fail — the range wins
            // because Validate short-circuits on the first offending parameter.
            var msg = Run(nameof(ValidationSubjects.TwoBadParams), 99.0, "");

            Assert.NotNull(msg);
            Assert.Contains("first", msg);
            Assert.DoesNotContain("second", msg);
        }

        [Fact]
        public void Validate_UnattributedParam_Passes()
        {
            Assert.Null(Run(nameof(ValidationSubjects.Unvalidated), 12345.0));
        }

        [Fact]
        public void Validate_NullInputs_ReturnNull()
        {
            Assert.Null(ParamValidator.Validate(null, new object[] { 1 }));
            Assert.Null(ParamValidator.Validate(ParamsOf(nameof(ValidationSubjects.Ranged)), null));
        }

        [Fact]
        public void Validate_FewerArgsThanParams_DoesNotThrow()
        {
            // Guards the i < parameters.Count && i < args.Length bound.
            var ex = Record.Exception(() =>
                ParamValidator.Validate(ParamsOf(nameof(ValidationSubjects.TwoBadParams)), new object[] { 5.0 }));

            Assert.Null(ex);
        }
    }
}
