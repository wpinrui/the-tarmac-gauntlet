// What the grandstands need of UnityEngine beyond the circuit check's stubs.
namespace UnityEngine
{
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color operator +(Color p, Color q) => new Color(p.r + q.r, p.g + q.g, p.b + q.b, p.a + q.a);
        public static Color operator *(Color p, float d) => new Color(p.r * d, p.g * d, p.b * d, p.a * d);
        public static Color operator *(float d, Color p) => p * d;
        public static Color Lerp(Color p, Color q, float t) => p + (q + p * -1f) * Mathf.Clamp01(t);
    }
}
namespace TarmacGauntlet.Raceday
{
    public sealed class StandMaterials
    {
        public UnityEngine.Material Concrete, Steel, Cladding, Seats, Roof, Bank, Crowd, FenceMesh, FencePosts;
    }
}
