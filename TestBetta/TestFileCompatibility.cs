// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using Betta.Attributes;
using Betta.Components;
using Betta.Services;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Xunit;

namespace TestBetta
{
    // Two versions of one component: v2 gained an input and an output. Not an
    // IBettaCollection, so discovery scans of this assembly don't pick it up.
    public class FileCompatSamples
    {
        public double RemapV1(double value, double sourceMax) => value / sourceMax;

        public (double Result, double Normalized) RemapV2(double number, double sourceMax, bool clamp) => (number / sourceMax, number);

        public double Resize(double value, [GrasshopperMenuState] FitMode mode) => value;
    }

    public enum FitMode { Stretch, Fit, Fill }

    /// <summary>
    /// What a saved .gh file meets when it opens: Grasshopper looks the stored GUID up among the
    /// proxies, creates the object and calls Read on its chunk, then saves whatever ComponentGuid
    /// the object reports (GH_Document.DeserializeObjects / GH_ComponentServer.EmitObject in
    /// Grasshopper 8.26). These tests run the Betta half of that headlessly through GH_IO.
    /// </summary>
    [Trait("Requires", "Rhino")]
    public class TestFileCompatibility
    {
        private static ComponentDescriptor Descriptor(string method)
        {
            var m = typeof(FileCompatSamples).GetMethod(method);
            return new ComponentDescriptor
            {
                ServiceType = typeof(FileCompatSamples),
                Method = m,
                Name = method, NickName = method, Description = method,
                Category = "Test", SubCategory = "Files",
                Guid = ComponentDescriptor.GenerateGuidFromMethod(typeof(FileCompatSamples), m),
            };
        }

        private static GH_Archive SaveAndReopen(IGH_DocumentObject obj)
        {
            var saved = new GH_Archive();
            saved.CreateNewRoot(true);
            Assert.True(saved.AppendObject(obj, "Component"));
            var reopened = new GH_Archive();
            Assert.True(reopened.Deserialize_Xml(saved.Serialize_Xml()));
            return reopened;
        }

        // GH_Archive.Messages stays null until something is logged.
        private static string[] Problems(GH_Archive archive) =>
            (archive.Messages ?? new()).Where(m => m.Type != GH_Message_Type.info).Select(m => m.Message).ToArray();

        [Fact]
        public void A_file_saved_before_the_method_gained_params_loads_cleanly_by_position()
        {
            var v1 = (BettaComponent)new BettaComponentProxy(Descriptor("RemapV1")).CreateInstance();
            var archive = SaveAndReopen(v1);

            var v2 = (BettaComponent)new BettaComponentProxy(Descriptor("RemapV2")).CreateInstance();
            Assert.True(archive.ExtractObject(v2, "Component"));

            // Without BettaComponent.Read setting the new params aside, GH logs
            // "Input/Output parameter chunk is missing. Archive is corrupt." for each.
            Assert.Empty(Problems(archive));
            Assert.Equal(3, v2.Params.Input.Count);
            Assert.Equal(2, v2.Params.Output.Count);
            // Wires hang off these instance ids, so the saved params come back in place.
            Assert.Equal(v1.Params.Input[0].InstanceGuid, v2.Params.Input[0].InstanceGuid);
            Assert.Equal(v1.Params.Input[1].InstanceGuid, v2.Params.Input[1].InstanceGuid);
            Assert.Equal(v1.Params.Output[0].InstanceGuid, v2.Params.Output[0].InstanceGuid);
            Assert.DoesNotContain(v2.Params.Input[2].InstanceGuid, v1.Params.Input.Select(p => p.InstanceGuid));
        }

        [Fact]
        public void An_alias_proxy_creates_the_component_under_its_current_guid()
        {
            var descriptor = Descriptor("RemapV1");
            var legacy = ComponentDescriptor.LegacyGuidFromMethod(typeof(FileCompatSamples), descriptor.Method);
            var alias = new BettaComponentProxy(descriptor, legacy);

            // GH finds the proxy by the GUID stored in the old file...
            Assert.Equal(legacy, alias.Guid);
            Assert.Equal(GH_Exposure.hidden, alias.Exposure);
            // ...and saves the object under the GUID it reports, so the file migrates.
            var component = (BettaComponent)alias.CreateInstance();
            Assert.Equal(descriptor.Guid, component.ComponentGuid);

            var archive = SaveAndReopen(component);
            var reopened = (BettaComponent)new BettaComponentProxy(descriptor).CreateInstance();
            Assert.True(archive.ExtractObject(reopened, "Component"));
            Assert.Equal(component.InstanceGuid, reopened.InstanceGuid);
            Assert.Empty(Problems(archive));
        }

        [Fact]
        public void An_enum_menu_choice_comes_back_without_resolving_the_saved_type_name()
        {
            var saved = (BettaComponent)new BettaComponentProxy(Descriptor("Resize")).CreateInstance();
            var chunk = new GH_LooseChunk("Component");
            Assert.True(saved.Write(chunk));
            // The file stores the enum's assembly-qualified name. A plugin lives in its own load
            // context, where Type.GetType can't find it by name (and the sibling resolver would
            // load a second copy of the plugin); an unknown assembly name stands in for that here.
            chunk.RemoveItem("betta_ms_count");
            chunk.SetInt32("betta_ms_count", 1);
            chunk.SetString("betta_ms_key_0", "mode");
            chunk.SetString("betta_ms_kind_0", "enum");
            chunk.SetString("betta_ms_etype_0", "TestBetta.FitMode, Some.Plugin, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
            chunk.SetInt64("betta_ms_val_0", (long)FitMode.Fill);

            var reopened = (BettaComponent)new BettaComponentProxy(Descriptor("Resize")).CreateInstance();
            Assert.True(reopened.Read(chunk));

            var resaved = new GH_LooseChunk("Component");
            Assert.True(reopened.Write(resaved));
            Assert.Equal(1, resaved.GetInt32("betta_ms_count"));
            Assert.Equal("enum", resaved.GetString("betta_ms_kind_0"));
            Assert.Equal((long)FitMode.Fill, resaved.GetInt64("betta_ms_val_0"));
        }
    }
}
