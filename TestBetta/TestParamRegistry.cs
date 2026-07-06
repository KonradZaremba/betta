// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Betta.Goo;
using Betta.Services;
using Xunit;

namespace TestBetta
{
    // Domain types isolated from other test fixtures so the static
    // ParamRegistry can be observed deterministically.
    public class RegistryGraphA { }
    public class RegistryGraphB { }

    public class TestParamRegistry
    {
        [Fact]
        public void Register_IsIdempotent()
        {
            ParamRegistry.RegisterOpaque(typeof(RegistryGraphA));
            ParamRegistry.RegisterOpaque(typeof(RegistryGraphA));
            ParamRegistry.RegisterOpaque(typeof(RegistryGraphA));

            Assert.True(ParamRegistry.IsRegistered(typeof(RegistryGraphA)));
            Assert.True(ParamRegistry.TryGetFactory(typeof(RegistryGraphA), out _));
        }

        [Fact]
        public void Factory_ProducesCorrectClosedGenericParam()
        {
            ParamRegistry.RegisterOpaque(typeof(RegistryGraphB));

            Assert.True(ParamRegistry.TryGetFactory(typeof(RegistryGraphB), out var factory));
            var param = factory();
            Assert.NotNull(param);
            Assert.IsType<Param_BettaGoo<RegistryGraphB>>(param);
        }

        [Fact]
        public void ParamBettaGoo_ComponentGuid_IsDeterministic()
        {
            var a = new Param_BettaGoo<RegistryGraphA>().ComponentGuid;
            var b = new Param_BettaGoo<RegistryGraphA>().ComponentGuid;
            Assert.Equal(a, b);
        }

        [Fact]
        public void ParamBettaGoo_ComponentGuid_DiffersByType()
        {
            var a = new Param_BettaGoo<RegistryGraphA>().ComponentGuid;
            var b = new Param_BettaGoo<RegistryGraphB>().ComponentGuid;
            Assert.NotEqual(a, b);
            Assert.NotEqual(Guid.Empty, a);
            Assert.NotEqual(Guid.Empty, b);
        }
    }
}
