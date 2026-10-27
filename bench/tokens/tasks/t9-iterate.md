Make these changes to the existing facade components in this project (category "Facade"):

1. Panel Stats: add a number input "Min Area" (default 0). Panels whose area is below it are left out of the statistics.
2. Planarity: add an output "Max Deviation", the largest deviation among the panels.
3. Planarity, Classify Panels and Group Identical Panels: when Tolerance is 0 or less, show an error and output nothing.
4. Group Identical Panels: run the grouping in the background so Grasshopper doesn't freeze on large inputs, stop it when the inputs change, and show its progress.
5. Classify Panels: rename the output "Type" to "Class".

Grasshopper files saved with the current version must still open with every component in place.

Follow the pattern this project already uses. Don't ask questions; make reasonable choices. You are done when `dotnet build` succeeds.
