Add a new Grasshopper component named "Async Stats" (category "Bench", subcategory "Data") that computes the mean and standard deviation of a list of numbers asynchronously, without blocking Grasshopper's UI, and cancels the previous computation when the inputs change. Input: Values (list of numbers). Outputs: Mean, StdDev.

Follow the pattern this project already uses. Don't ask questions; make reasonable choices. You are done when `dotnet build` succeeds.
