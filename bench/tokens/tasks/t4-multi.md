Add five new Grasshopper components in one go, all in category "Bench":

1. "Remap" (subcategory "Math") remaps a number from a source range to a target range. Inputs: Value, Source Min, Source Max, Target Min, Target Max (all numbers). Output: Result.
2. "Clamp" (subcategory "Math") limits a number to a range. Inputs: Value, Min, Max (all numbers). Output: Result.
3. "Lerp" (subcategory "Math") linearly interpolates between two numbers. Inputs: A, B, T (all numbers). Output: Result.
4. "Divide Curve Stats" (subcategory "Geometry") divides a curve into N equal-length segments. Inputs: Curve, Count (integer, default 10). Outputs: Points (list of points) and Length (total curve length).
5. "Async Stats" (subcategory "Data") computes the mean and standard deviation of a list of numbers asynchronously, without blocking Grasshopper's UI, and cancels the previous computation when the inputs change. Input: Values (list of numbers). Outputs: Mean, StdDev.

Follow the pattern this project already uses. Don't ask questions; make reasonable choices. You are done when `dotnet build` succeeds.
