using Betta.Attributes;
using Betta.Interfaces;

namespace BenchPlugin
{
    [GrasshopperCollection("Bench", "Math")]
    public class RemapPack : IBettaCollection
    {
        [GrasshopperMethod("Remap", "Remaps a number from a source range to a target range")]
        [return: GrasshopperOutput("Result")]
        public double Remap(
            [GrasshopperParameter("Value")] double value,
            [GrasshopperParameter("Source Min")] double sourceMin,
            [GrasshopperParameter("Source Max")] double sourceMax,
            [GrasshopperParameter("Target Min")] double targetMin,
            [GrasshopperParameter("Target Max")] double targetMax)
        {
            var t = sourceMax == sourceMin ? 0 : (value - sourceMin) / (sourceMax - sourceMin);
            return targetMin + t * (targetMax - targetMin);
        }
    }
}
