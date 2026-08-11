// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Betta.Attributes;
using Betta.Interfaces;

namespace Betta.Files
{
    /// <summary>Delimiter choices for Split Delimited, surfaced as a right-click menu.</summary>
    public enum CsvDelimiter
    {
        Comma,
        Semicolon,
        Tab,
        Pipe
    }

    /// <summary>
    /// The read/write/find half of Betta.Files (<b>Files › IO</b>) — plain,
    /// synchronous-feeling file utilities generated from attributed methods, no
    /// GH_Component code. The live watchers live separately in
    /// <see cref="FileWatchers"/> (Files › Watch). Class-direct authoring:
    /// attributes on the concrete class, which opts in via
    /// <see cref="IBettaCollection"/>.
    /// </summary>
    [GrasshopperCollection("Files", "IO")]
    public class FileToolkit : IBettaCollection
    {
        // ---- Read ----------------------------------------------------------

        [GrasshopperMethod("Read Text", "Read a whole file as text. Empty if it doesn't exist.")]
        public string ReadText(
            [GrasshopperParameter("Path", "P", "File path"), GrasshopperNotEmpty] string path)
            => File.Exists(path) ? File.ReadAllText(path) : string.Empty;

        [GrasshopperMethod("Read Lines", "Read a file into a list of lines.")]
        public List<string> ReadLines(
            [GrasshopperParameter("Path", "P", "File path"), GrasshopperNotEmpty] string path)
            => File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();

        [GrasshopperMethod("Split Delimited",
            "Split one delimited line into fields. Pick the delimiter from the component's right-click menu.")]
        public List<string> SplitDelimited(
            [GrasshopperParameter("Line", "L", "One line of delimited text")] string line,
            [GrasshopperMenuState] CsvDelimiter delimiter)
            => (line ?? string.Empty).Split(CharFor(delimiter)).ToList();

        // ---- Write (async) -------------------------------------------------

        [GrasshopperMethod("Write Text", "Write text to a file (async). Returns the written path.")]
        public async Task<string> WriteText(
            [GrasshopperParameter("Path", "P", "File path"), GrasshopperNotEmpty] string path,
            [GrasshopperParameter("Text", "T", "Text to write")] string text)
        {
            EnsureDir(path);
            await Task.Run(() => File.WriteAllText(path, text ?? string.Empty));
            return path;
        }

        [GrasshopperMethod("Write Lines", "Write a list of lines to a file (async). Returns the written path.")]
        public async Task<string> WriteLines(
            [GrasshopperParameter("Path", "P", "File path"), GrasshopperNotEmpty] string path,
            [GrasshopperParameter("Lines", "L", "Lines to write")] List<string> lines)
        {
            EnsureDir(path);
            await Task.Run(() => File.WriteAllLines(path, lines ?? new List<string>()));
            return path;
        }

        // ---- Find ----------------------------------------------------------

        [GrasshopperMethod("Find Files", "List files in a folder matching a glob pattern.")]
        public List<string> FindFiles(
            [GrasshopperParameter("Folder", "D", "Folder to search"), GrasshopperNotEmpty] string folder,
            [GrasshopperParameter("Pattern", "F", "Glob pattern", DefaultValue = "*.*")] string pattern,
            [GrasshopperParameter("Recursive", "R", "Include sub-folders", DefaultValue = false)] bool recursive)
            => Directory.Exists(folder)
                ? Directory.EnumerateFiles(
                    folder,
                    string.IsNullOrWhiteSpace(pattern) ? "*.*" : pattern,
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).ToList()
                : new List<string>();

        // ---- helpers -------------------------------------------------------

        private static char CharFor(CsvDelimiter d) => d switch
        {
            CsvDelimiter.Semicolon => ';',
            CsvDelimiter.Tab => '\t',
            CsvDelimiter.Pipe => '|',
            _ => ',',
        };

        private static void EnsureDir(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
    }
}
