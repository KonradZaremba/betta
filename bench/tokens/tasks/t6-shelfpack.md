Add a new Grasshopper component named "Shelf Pack" (category "Bench", subcategory "Fabrication") that packs rectangular parts onto rectangular sheets with a shelf algorithm.

Inputs: Widths (list of numbers), Heights (list of numbers; part i is Widths[i] by Heights[i]), Sheet Width (number, default 1000), Sheet Height (number, default 500), Spacing (number, default 5; the gap between parts and from the sheet edge), Rotate (boolean, default true).

Algorithm: sort the parts by height, tallest first. Fill shelves left to right; a shelf is as tall as its first part. When a part doesn't fit in the rest of the current shelf, start a new shelf above it; when a new shelf doesn't fit on the sheet, start a new sheet. If Rotate is true and a part fits only when turned 90 degrees, turn it. A part that doesn't fit on an empty sheet, even turned, is left out.

Outputs: Outlines (each placed part's outline as a closed curve, with the sheets laid out side by side along X, Sheet Width + 50 apart), Sheet (the sheet index of each placed part, in the same order as Outlines), Sheet Count, Utilization (total placed part area divided by Sheet Count times the sheet area, 0 to 1), and Unplaced (the input indices of the parts that were left out).

Errors and warnings: if Widths and Heights have different lengths, or any size is zero or negative, show an error and output nothing. If some parts were left out, show a warning that says how many, and still output everything else.

Follow the pattern this project already uses. Don't ask questions; make reasonable choices. You are done when `dotnet build` succeeds.
