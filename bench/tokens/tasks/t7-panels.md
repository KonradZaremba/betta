Add a new Grasshopper component named "Panel Grid" (category "Bench", subcategory "Panels") that divides a surface into a grid of four-sided panels and checks how flat they are.

Inputs: Surface, U Count (integer, default 10), V Count (integer, default 10), Inset (number, default 0), Tolerance (number, default 0.01).

Divide the surface's domain evenly into U Count by V Count cells. For each cell, take the four surface points at the cell's corners. If Inset is greater than 0, move each corner toward the average of the four corners by Inset, but not past it. The panel is the closed polyline through the four corners.

Outputs: Panels (the closed polylines, U first then V), Areas (each panel's area, as the sum of its two triangles), Deviation (how far each panel is from flat: the distance from its fourth corner to the plane through the other three), and Non Planar (the indices of the panels whose Deviation is greater than Tolerance).

Errors and warnings: if U Count or V Count is less than 1, or Inset or Tolerance is negative, show an error and output nothing. If any panel's Deviation is greater than Tolerance, show a warning that says how many, and still output everything.

Follow the pattern this project already uses. Don't ask questions; make reasonable choices. You are done when `dotnet build` succeeds.
