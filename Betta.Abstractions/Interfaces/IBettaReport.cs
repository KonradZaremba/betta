// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Betta.Interfaces
{
    /// <summary>
    /// Shows runtime messages on the component while the method still returns its
    /// results, for example a warning that some parts didn't fit. Declare a parameter
    /// of this type on a [GrasshopperMethod]: Betta supplies it at solve time and it
    /// gets no input pin, like <see cref="System.Threading.CancellationToken"/>.
    ///
    /// Messages appear when the result is output, also for async and streaming
    /// methods, and it is safe to call from any thread. Identical messages are shown
    /// once. To fail with an error and no outputs, throw an exception instead.
    /// </summary>
    /// <example>
    /// <code>
    /// [GrasshopperMethod("Pack", "Packs parts onto sheets")]
    /// public List&lt;Curve&gt; Pack(List&lt;double&gt; widths, IBettaReport report)
    /// {
    ///     // ...
    ///     if (leftOut &gt; 0) report.Warning($"{leftOut} parts didn't fit");
    ///     return outlines;
    /// }
    /// </code>
    /// </example>
    public interface IBettaReport
    {
        /// <summary>Shows an orange warning balloon on the component.</summary>
        void Warning(string message);

        /// <summary>Shows a grey remark on the component.</summary>
        void Remark(string message);
    }
}
