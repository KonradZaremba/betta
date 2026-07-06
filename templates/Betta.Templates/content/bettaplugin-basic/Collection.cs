using Betta.Attributes;
using Betta.Interfaces;

namespace MyBettaPlugin
{
    [GrasshopperCollection("MY_BETTA_CATEGORY", "Math")]
    public interface IMyPack : IBettaCollection
    {
        [GrasshopperMethod("Cube", "x cubed")]
        double Cube([GrasshopperParameter("Value", DefaultValue = 2.0)] double x);

        [GrasshopperMethod("Stats", "Sum and average of a list of numbers")]
        (double Sum, double Average) Stats(
            [GrasshopperParameter("Numbers")] System.Collections.Generic.List<double> numbers);
    }

    public class MyPack : IMyPack
    {
        public double Cube(double x) => x * x * x;

        public (double Sum, double Average) Stats(System.Collections.Generic.List<double> numbers)
        {
            if (numbers == null || numbers.Count == 0) return (0, 0);
            double sum = 0;
            foreach (var n in numbers) sum += n;
            return (sum, sum / numbers.Count);
        }
    }
}
