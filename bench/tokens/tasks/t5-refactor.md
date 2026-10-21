Change the existing Grasshopper component "Remap" (category "Bench"): rename the input "Value" to "Number"; add a boolean input "Clamp" (default false) that, when true, clamps the result to the target range; add a second output "Normalized" (the input's position in the source range, 0-1); and move it to subcategory "Mapping".

Follow the pattern this project already uses. Don't ask questions; make reasonable choices. You are done when `dotnet build` succeeds.
