// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Betta.Mcp.Inspection;

public static class SharedFile
{
    /// <summary>
    /// A file's lines, read while others may hold it open: Rhino writing Betta.log (and rolling
    /// it), an IDE or a build writing betta.lock.
    /// </summary>
    public static IEnumerable<string> ReadLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) != null) yield return line;
    }
}
