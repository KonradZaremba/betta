// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Linq;

namespace Betta.Strings
{
    public class StringCollection : IStringCollection
    {
        public string ToUpper(string text) => text?.ToUpperInvariant() ?? string.Empty;

        public string ToLower(string text) => text?.ToLowerInvariant() ?? string.Empty;

        public string Reverse(string text) =>
            string.IsNullOrEmpty(text) ? string.Empty : new string(text.Reverse().ToArray());

        public int Length(string text) => text?.Length ?? 0;

        public string Concat(string left, string right, string separator) =>
            (left ?? "") + (separator ?? "") + (right ?? "");
    }
}
