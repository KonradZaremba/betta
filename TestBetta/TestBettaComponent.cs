// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Rhino.Geometry;
using Rhino.Test;
using SampleGHTests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace TestBetta
{
    [CollectionDefinition("Primitives.gh collection")]
    public class PrimitivesCollection : ICollectionFixture<PrimitivesFixture>
    {
        // This class has no code, and is never created. Its purpose is simply
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }

    public class PrimitivesFixture : Rhino.Test.GHFileFixture
    {
        public PrimitivesFixture() : base("Primitives.gh") { }
    }
    
    [Collection("Primitives.gh collection")]
    public class TestBettaComponent
    {
        PrimitivesFixture fixture { get; set; }

        public TestBettaComponent(PrimitivesFixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact]
        public void TestFileIsOpen()
        {
            fixture.Doc.SelectAll();
            var selected = fixture.Doc.SelectedCount;
            Assert.Equal(fixture.Doc.DisplayName, "Primitives");
            Assert.True(fixture.GH.DocIO.IsDocument);    
        }

        [Fact]
        public void TestLineAgain()
        {
            foreach (var obj in (fixture.Doc.Objects))
                if (obj is Grasshopper.Kernel.IGH_Param param)
                    if (param.NickName == "TestLineOutput") {
                        param.CollectData();
                        param.ComputeData();

                        Assert.Equal(1, param.VolatileData.DataCount);
                        var data = param.VolatileData.AllData(true).GetEnumerator();
                        data.Reset();
                        data.MoveNext();
                        var theLine = data.Current;
                        Assert.True(theLine.CastTo(out Line line));
                        Assert.Equal(Math.Sqrt(1.0 * 1.0 + -5.0 * -5.0 + 3.0 * 3.0), line.Length);
                        return;
                    }
            Assert.True(false, "Did not find oputput");
        }
    }
}



