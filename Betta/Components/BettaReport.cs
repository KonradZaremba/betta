// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Betta.Interfaces;

namespace Betta.Components
{
    internal enum BettaReportLevel { Remark, Warning }

    /// <summary>
    /// The <see cref="IBettaReport"/> handed to a service method. GH-free so it is
    /// testable headlessly: it only collects, and <see cref="BettaComponent"/> posts the
    /// messages as runtime messages when it outputs the call's result. Thread-safe,
    /// because async and streaming methods report from worker threads.
    /// </summary>
    internal sealed class BettaReport : IBettaReport
    {
        /// <summary>Further messages are dropped, so a method that warns per item can't flood the canvas.</summary>
        public const int MaxMessages = 50;

        private readonly object _lock = new();
        private readonly List<(BettaReportLevel Level, string Text)> _messages = new();

        public void Warning(string message) => Add(BettaReportLevel.Warning, message);

        public void Remark(string message) => Add(BettaReportLevel.Remark, message);

        /// <summary>A snapshot, in the order the method reported them.</summary>
        public IReadOnlyList<(BettaReportLevel Level, string Text)> Messages
        {
            get { lock (_lock) return _messages.ToArray(); }
        }

        private void Add(BettaReportLevel level, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            lock (_lock)
            {
                if (_messages.Count >= MaxMessages || _messages.Contains((level, text))) return;
                _messages.Add((level, text));
            }
        }
    }
}
