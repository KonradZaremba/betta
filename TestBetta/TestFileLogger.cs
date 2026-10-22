// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using Betta.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TestBetta
{
    /// <summary>
    /// Betta.log gets a Debug line for every solve, so it has to stay bounded: a log with no limit
    /// reached hundreds of MB in a few months of use. Real files in a temp folder.
    /// </summary>
    public class TestFileLogger : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "betta-log-" + Guid.NewGuid().ToString("N"));

        public TestFileLogger() => Directory.CreateDirectory(_dir);

        private string LogPath => Path.Combine(_dir, "Betta.log");

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        }

        [Fact]
        public void A_full_log_moves_aside_when_a_session_starts()
        {
            File.WriteAllText(LogPath, new string('x', 2000));

            using var provider = new FileLoggerProvider(LogPath, maxBytes: 1000);

            Assert.Equal(new string('x', 2000), File.ReadAllText(LogPath + ".old"));
            Assert.StartsWith("--- Betta session started", File.ReadAllText(LogPath));
        }

        [Fact]
        public void A_log_under_the_limit_keeps_its_history()
        {
            File.WriteAllText(LogPath, "earlier session" + Environment.NewLine);

            using var provider = new FileLoggerProvider(LogPath, maxBytes: 1000);

            Assert.False(File.Exists(LogPath + ".old"));
            Assert.StartsWith("earlier session", File.ReadAllText(LogPath));
        }

        [Fact]
        public void A_long_session_rolls_the_log_while_it_runs()
        {
            using var provider = new FileLoggerProvider(LogPath, maxBytes: 10_000);
            var logger = provider.CreateLogger("Betta.Components.BettaComponent");

            // About 150 KB in all; without rolling the file would hold every line.
            for (var i = 0; i < 3 * FileLoggerProvider.RollCheckInterval; i++)
                logger.LogDebug("Remap: invoking Remap({Value})", i);

            Assert.True(File.Exists(LogPath + ".old"));
            Assert.True(new FileInfo(LogPath).Length < 10_000 + FileLoggerProvider.RollCheckInterval * 100);
        }

        [Fact]
        public void A_locked_log_file_does_not_fail_the_caller()
        {
            using var provider = new FileLoggerProvider(LogPath);
            var logger = provider.CreateLogger("Betta.Components.BettaComponent");

            using (new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.None))
                logger.LogInformation("written while another process holds the file");

            logger.LogInformation("written after it is released");
            Assert.Contains("written after it is released", File.ReadAllText(LogPath));
        }
    }
}
