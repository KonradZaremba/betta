// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Betta;
using Betta.Files;
using Betta.Services;
using Xunit;

namespace TestBetta
{
    /// <summary>
    /// Headless coverage for the Betta.Files pilot colony: two collections
    /// (Files › Watch, Files › IO), the FolderState output shape, the initial
    /// snapshot + live change behaviour of Watch Folder, and the IO utilities.
    /// Drives the public API against real temp files — no Rhino needed.
    /// </summary>
    public class TestFileToolkit : IDisposable
    {
        private readonly string _dir;

        public TestFileToolkit()
        {
            _dir = Path.Combine(Path.GetTempPath(), "BettaFilesTest_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private string P(string name) => Path.Combine(_dir, name);

        // ---- Discovery: two collections ------------------------------------

        [Fact]
        public void IoCollection_RegistersReadWriteFind()
        {
            var names = DiscoveredNames(typeof(FileToolkit));
            Assert.Equal(
                new[] { "Find Files", "Read Lines", "Read Text", "Split Delimited", "Write Lines", "Write Text" },
                names);
        }

        [Fact]
        public void WatchCollection_RegistersTheTwoWatchers()
        {
            var names = DiscoveredNames(typeof(FileWatchers));
            Assert.Equal(new[] { "Watch File", "Watch Folder" }, names);
        }

        [Fact]
        public void WatchFolder_IsStreaming_FolderState()
        {
            var registry = new ComponentRegistry();
            registry.DiscoverFromAssembly(typeof(FileWatchers).Assembly);

            var watch = registry.GetDescriptors().Single(d => d.Name == "Watch Folder");

            Assert.Equal(typeof(IObservable<FolderState>), watch.Method.ReturnType);
        }

        // ---- Output shape: FolderState explodes to AllPaths[] + 3 scalars --

        [Fact]
        public void FolderState_ExplodesToListPlusThreeScalars()
        {
            var plans = OutputPlanner.PlanOutputs(typeof(FolderState), null);

            var byName = plans.ToDictionary(p => p.Name);
            Assert.True(byName[nameof(FolderState.AllPaths)].IsList, "AllPaths must be a list output.");
            Assert.False(byName[nameof(FolderState.Changed)].IsList);
            Assert.False(byName[nameof(FolderState.Kind)].IsList);
            Assert.False(byName[nameof(FolderState.When)].IsList);
        }

        // ---- Watch Folder: initial snapshot + live change ------------------

        [Fact]
        public void WatchFolder_InitialSnapshot_ListsExistingFiles()
        {
            File.WriteAllText(P("already.txt"), "here first");

            var obs = new FileWatchers().WatchFolder(_dir, "*.*", recursive: false, debounceMs: 100);
            var collector = new Collector<FolderState>();

            using (obs.Subscribe(collector))
            {
                Assert.True(WaitForCount(collector, 1), "No initial snapshot emitted.");
                var first = collector.Snapshot()[0];
                Assert.Equal(string.Empty, first.Changed);      // snapshot has no "changed"
                Assert.Contains(first.AllPaths, p => p.EndsWith("already.txt"));
            }
        }

        [Fact]
        public void WatchFolder_OnChange_FlagsChangedAndReListsFolder()
        {
            var obs = new FileWatchers().WatchFolder(_dir, "*.*", recursive: false, debounceMs: 100);
            var collector = new Collector<FolderState>();

            using (obs.Subscribe(collector))
            {
                WaitForCount(collector, 1); // initial snapshot (empty folder)

                var target = P("new.txt");
                File.WriteAllText(target, "hi");

                Assert.True(WaitForCount(collector, 2), "No emission after a file change.");
                var change = collector.Snapshot().Last();
                Assert.True(string.Equals(change.Changed, target, StringComparison.OrdinalIgnoreCase),
                    "Changed did not point at the new file.");
                Assert.Contains(change.AllPaths, p => p.EndsWith("new.txt"));
            }
        }

        [Fact]
        public void WatchFolder_MissingFolder_StillSnapshots_NoThrow()
        {
            var obs = new FileWatchers().WatchFolder(P("no-such-dir"), "*.*", false, 50);
            var collector = new Collector<FolderState>();

            using (obs.Subscribe(collector))   // must not throw
            {
                Assert.True(WaitForCount(collector, 1), "Missing folder should still emit an (empty) snapshot.");
                Assert.Empty(collector.Snapshot()[0].AllPaths);
            }
        }

        // ---- Watch File ----------------------------------------------------

        [Fact]
        public void WatchFile_EmitsExistingStateOnStart()
        {
            var file = P("watched.txt");
            File.WriteAllText(file, "x");

            var obs = new FileWatchers().WatchFile(file, debounceMs: 100);
            var collector = new Collector<FileChange>();

            using (obs.Subscribe(collector))
            {
                Assert.True(WaitForCount(collector, 1), "Watch File did not emit its initial state.");
                Assert.Equal("Existing", collector.Snapshot()[0].Kind);
            }
        }

        // ---- IO: read / write / split / find -------------------------------

        [Fact]
        public async Task WriteThenReadText_RoundTrips()
        {
            var toolkit = new FileToolkit();
            var path = P("note.txt");

            var written = await toolkit.WriteText(path, "line one");

            Assert.Equal(path, written);
            Assert.Equal("line one", toolkit.ReadText(path));
        }

        [Fact]
        public async Task WriteThenReadLines_RoundTrips()
        {
            var toolkit = new FileToolkit();
            var path = P("lines.txt");

            await toolkit.WriteLines(path, new List<string> { "a", "b", "c" });

            Assert.Equal(new[] { "a", "b", "c" }, toolkit.ReadLines(path));
        }

        [Fact]
        public void ReadText_MissingFile_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, new FileToolkit().ReadText(P("ghost.txt")));
        }

        [Theory]
        [InlineData(CsvDelimiter.Comma, "a,b,c", 3)]
        [InlineData(CsvDelimiter.Semicolon, "a;b", 2)]
        [InlineData(CsvDelimiter.Tab, "a\tb\tc\td", 4)]
        [InlineData(CsvDelimiter.Pipe, "a|b", 2)]
        public void SplitDelimited_UsesSelectedDelimiter(CsvDelimiter delimiter, string line, int expected)
        {
            Assert.Equal(expected, new FileToolkit().SplitDelimited(line, delimiter).Count);
        }

        [Fact]
        public void FindFiles_MatchesGlobPattern()
        {
            File.WriteAllText(P("a.txt"), "");
            File.WriteAllText(P("b.txt"), "");
            File.WriteAllText(P("c.csv"), "");

            var txt = new FileToolkit().FindFiles(_dir, "*.txt", recursive: false);

            Assert.Equal(2, txt.Count);
            Assert.All(txt, p => Assert.EndsWith(".txt", p));
        }

        // ---- helpers -------------------------------------------------------

        private static string[] DiscoveredNames(Type serviceType)
        {
            var registry = new ComponentRegistry();
            registry.DiscoverFromAssembly(serviceType.Assembly);
            return registry.GetDescriptors()
                .Where(d => d.ServiceType == serviceType)
                .Select(d => d.Name)
                .OrderBy(n => n)
                .ToArray();
        }

        private static bool WaitForCount<T>(Collector<T> c, int count, int timeoutMs = 5000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (c.Count >= count) return true;
                Thread.Sleep(25);
            }
            return c.Count >= count;
        }

        private sealed class Collector<T> : IObserver<T>
        {
            private readonly List<T> _items = new();
            public void OnNext(T value) { lock (_items) _items.Add(value); }
            public void OnError(Exception error) { }
            public void OnCompleted() { }
            public int Count { get { lock (_items) return _items.Count; } }
            public List<T> Snapshot() { lock (_items) return _items.ToList(); }
        }
    }
}
