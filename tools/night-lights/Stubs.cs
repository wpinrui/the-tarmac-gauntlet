// What the floodlights, stands, pit complex and sky clock need of UnityEngine beyond the circuit check's stubs.
namespace UnityEngine
{
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color operator *(Color c, float d) => new Color(c.r * d, c.g * d, c.b * d, c.a * d);
        public static Color operator +(Color x, Color y) => new Color(x.r + y.r, x.g + y.g, x.b + y.b, x.a + y.a);
        public static Color black => new Color(0, 0, 0, 1);
    }
}
namespace TarmacGauntlet.Raceday
{
    public sealed class StandMaterials
    {
        public UnityEngine.Material Concrete, Steel, Cladding, Seats, Roof, Bank, Crowd, FenceMesh, FencePosts;
    }
    public sealed class PitMaterials
    {
        public UnityEngine.Material Concrete, Glass, Steel, Palette, Fit, Metal, Rubber, Lamps;
    }
}
