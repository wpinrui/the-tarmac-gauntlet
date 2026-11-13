// What the floodlights' layout, the stands, the pit complex and the night's budget need of UnityEngine beyond the circuit
// check's stubs.
namespace UnityEngine
{
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color operator *(Color c, float d) => new Color(c.r * d, c.g * d, c.b * d, c.a * d);
        public static Color operator +(Color x, Color y) => new Color(x.r + y.r, x.g + y.g, x.b + y.b, x.a + y.a);
        public static Color black => new Color(0, 0, 0, 1);
        public float grayscale => 0.299f * r + 0.587f * g + 0.114f * b;
        public static Color Lerp(Color x, Color y, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color(x.r + (y.r - x.r) * t, x.g + (y.g - x.g) * t, x.b + (y.b - x.b) * t, x.a + (y.a - x.a) * t);
        }
    }
    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
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
