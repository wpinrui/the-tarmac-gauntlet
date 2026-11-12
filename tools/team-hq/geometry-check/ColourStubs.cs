// The colour UnityEngine gives LightFalloff and LinearLight, which the HQ's lighting compiles against.
namespace UnityEngine
{
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color black => new Color(0f, 0f, 0f, 1f);
        public static Color operator *(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, c.a * f);
        public static Color operator +(Color p, Color q) => new Color(p.r + q.r, p.g + q.g, p.b + q.b, p.a + q.a);
    }
}
