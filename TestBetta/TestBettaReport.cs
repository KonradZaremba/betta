// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Linq;
using System.Threading.Tasks;
using Betta.Components;
using Xunit;

namespace TestBetta
{
    /// <summary>
    /// The collector behind an injected <see cref="Betta.Interfaces.IBettaReport"/>.
    /// GH-free: BettaComponent turns its messages into runtime messages after the call.
    /// </summary>
    public class TestBettaReport
    {
        [Fact]
        public void Keeps_messages_in_order_with_their_level()
        {
            var report = new BettaReport();
            report.Warning("3 parts left out");
            report.Remark("2 sheets");

            Assert.Equal(new[] { (BettaReportLevel.Warning, "3 parts left out"), (BettaReportLevel.Remark, "2 sheets") },
                         report.Messages);
        }

        [Fact]
        public void Ignores_duplicates_and_empty_messages()
        {
            var report = new BettaReport();
            report.Warning("not flat");
            report.Warning("not flat");
            report.Remark("not flat");
            report.Warning("");
            report.Warning(null);

            Assert.Equal(new[] { (BettaReportLevel.Warning, "not flat"), (BettaReportLevel.Remark, "not flat") },
                         report.Messages);
        }

        [Fact]
        public void Caps_a_runaway_method()
        {
            var report = new BettaReport();
            for (int i = 0; i < 1000; i++) report.Warning("panel " + i);

            Assert.Equal(BettaReport.MaxMessages, report.Messages.Count);
            Assert.Equal("panel 0", report.Messages[0].Text);
        }

        [Fact]
        public void Accepts_messages_from_many_threads()
        {
            var report = new BettaReport();
            Parallel.For(0, 40, i => report.Remark("item " + i));

            Assert.Equal(40, report.Messages.Select(m => m.Text).Distinct().Count());
        }
    }
}
