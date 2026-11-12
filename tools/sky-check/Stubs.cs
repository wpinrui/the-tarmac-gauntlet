// Just enough of UnityEngine for the sky clock to run outside Unity.
using System;
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color operator +(Color p, Color q) => new Color(p.r + q.r, p.g + q.g, p.b + q.b, p.a + q.a);
        public static Color operator *(Color p, float d) => new Color(p.r * d, p.g * d, p.b * d, p.a * d);
        public float grayscale => 0.299f * r + 0.587f * g + 0.114f * b;
        public override string ToString() => $"({r:0.00} {g:0.00} {b:0.00})";
    }
    public static class Mathf
    {
        public const float Deg2Rad = (float)(Math.PI / 180), Rad2Deg = (float)(180 / Math.PI);
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static float Clamp01(float v) => Clamp(v, 0, 1);
        public static float Sin(float v) => (float)Math.Sin(v);
        public static float Cos(float v) => (float)Math.Cos(v);
        public static float Asin(float v) => (float)Math.Asin(v);
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Exp(float v) => (float)Math.Exp(v);
        public static float Repeat(float t, float l) => Clamp(t - (float)Math.Floor(t / l) * l, 0, l);
    }
}
