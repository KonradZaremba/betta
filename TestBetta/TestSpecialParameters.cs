// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Betta.Attributes;
using Xunit;

namespace TestBetta
{
    /// <summary>
    /// Headless coverage for the "this parameter is not a normal input pin"
    /// detectors. These gate whether a service-method parameter becomes a wired
    /// input or is synthesized/skipped — a regression silently mis-builds every
    /// affected component. All detectors are pure reflection (GH-free); the
    /// async/streaming return-type predicates live on BettaComponent as private
    /// statics and are reached by reflection.
    /// </summary>
    public class TestSpecialParameters
    {
        // Fixture: one method whose parameters carry each special attribute, plus
        // a couple of plain ones. We only ever reflect its ParameterInfo — it is
        // never invoked.
        private interface ISpecialSamples
        {
            void Sample(
                double plain,
                [GrasshopperMenuState] string mode,
                [GrasshopperSecret("service.key")] string apiKey,
                [GrasshopperTrigger] bool run,
                CancellationToken token,
                IProgress<double> progress);
        }

        private static ParameterInfo Param(string name) =>
            typeof(ISpecialSamples).GetMethod(nameof(ISpecialSamples.Sample))
                .GetParameters().Single(p => p.Name == name);

        // --- Type-keyed synthetic detectors (ParamInjector) -----------------

        [Fact]
        public void CancellationToken_IsSynthetic()
        {
            Assert.True(Betta.ParamInjector.IsSyntheticParameter(typeof(CancellationToken)));
        }

        [Fact]
        public void Progress_IsSyntheticAndProgress()
        {
            Assert.True(Betta.ParamInjector.IsProgressType(typeof(IProgress<double>)));
            Assert.True(Betta.ParamInjector.IsSyntheticParameter(typeof(IProgress<double>)));
        }

        [Theory]
        [InlineData(typeof(double))]
        [InlineData(typeof(string))]
        [InlineData(typeof(int[]))]
        public void PlainTypes_AreNotSynthetic(Type t)
        {
            Assert.False(Betta.ParamInjector.IsSyntheticParameter(t));
            Assert.False(Betta.ParamInjector.IsProgressType(t));
        }

        // --- Attribute-keyed detectors (ParamInjector) ----------------------

        [Fact]
        public void MenuState_DetectedOnlyOnAttributedParam()
        {
            Assert.True(Betta.ParamInjector.IsMenuStateParameter(Param("mode")));
            Assert.False(Betta.ParamInjector.IsMenuStateParameter(Param("plain")));
        }

        [Fact]
        public void Secret_DetectedOnlyOnAttributedParam()
        {
            Assert.True(Betta.ParamInjector.IsSecretParameter(Param("apiKey")));
            Assert.False(Betta.ParamInjector.IsSecretParameter(Param("plain")));
        }

        [Fact]
        public void Trigger_DetectedOnlyOnAttributedParam()
        {
            Assert.True(Betta.ParamInjector.IsTriggerParameter(Param("run")));
            Assert.False(Betta.ParamInjector.IsTriggerParameter(Param("plain")));
        }

        [Fact]
        public void Detectors_DoNotCrossContaminate()
        {
            // The secret param must not read as menu-state or trigger, etc.
            var apiKey = Param("apiKey");
            Assert.True(Betta.ParamInjector.IsSecretParameter(apiKey));
            Assert.False(Betta.ParamInjector.IsMenuStateParameter(apiKey));
            Assert.False(Betta.ParamInjector.IsTriggerParameter(apiKey));
        }

        // --- Return-type predicates (BettaComponent private statics) ---------

        private static bool InvokeReturnPredicate(string method, Type arg)
        {
            var m = typeof(Betta.Components.BettaComponent).GetMethod(method,
                BindingFlags.NonPublic | BindingFlags.Static);
            return (bool)m.Invoke(null, new object[] { arg });
        }

        [Fact]
        public void IsObservableType_TrueForIObservable_FalseOtherwise()
        {
            Assert.True(InvokeReturnPredicate("IsObservableType", typeof(IObservable<double>)));
            Assert.False(InvokeReturnPredicate("IsObservableType", typeof(Task<double>)));
            Assert.False(InvokeReturnPredicate("IsObservableType", typeof(double)));
        }

        [Fact]
        public void IsTaskType_TrueForTaskAndValueTask_FalseOtherwise()
        {
            Assert.True(InvokeReturnPredicate("IsTaskType", typeof(Task<double>)));
            Assert.True(InvokeReturnPredicate("IsTaskType", typeof(ValueTask<int>)));
            Assert.False(InvokeReturnPredicate("IsTaskType", typeof(IObservable<double>)));
            Assert.False(InvokeReturnPredicate("IsTaskType", typeof(double)));
        }

        // --- IProgress<T> -> Action<object> bridge (ParamInjector) -----------

        [Fact]
        public void BuildProgressInstance_ForwardsReportsToSink()
        {
            object received = null;
            Action<object> sink = v => received = v;

            var m = typeof(Betta.ParamInjector).GetMethod("BuildProgressInstance",
                BindingFlags.NonPublic | BindingFlags.Static);
            var progress = (IProgress<int>)m.Invoke(null, new object[] { typeof(IProgress<int>), sink });

            progress.Report(7);

            Assert.Equal(7, received);
        }
    }
}
