// Just enough of UnityEngine, and of the rendering code, for the sky's clouds to run outside Unity.
using System;
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public Vector3 normalized { get { float m = magnitude; return new Vector3(x / m, y / m, z / m); } }
    }
    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color operator +(Color p, Color q) => new Color(p.r + q.r, p.g + q.g, p.b + q.b, p.a + q.a);
        public static Color operator *(Color p, float d) => new Color(p.r * d, p.g * d, p.b * d, p.a * d);
        public static Color Lerp(Color p, Color q, float t) { t = Mathf.Clamp01(t); return new Color(p.r + (q.r - p.r) * t, p.g + (q.g - p.g) * t, p.b + (q.b - p.b) * t, p.a + (q.a - p.a) * t); }
        public float grayscale => 0.299f * r + 0.587f * g + 0.114f * b;
    }
    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }
    public enum TextureFormat { RGBA32 }
    public enum TextureWrapMode { Repeat }
    public enum FilterMode { Bilinear }
    [Flags] public enum HideFlags { None = 0, DontSave = 52 }
    public class Object
    {
        public string name;
        public HideFlags hideFlags;
        public static implicit operator bool(Object o) => o != null;
    }
    public class Texture3D : Object
    {
        public TextureWrapMode wrapMode;
        public FilterMode filterMode;
        public Texture3D(int w, int h, int d, TextureFormat f, bool mips) { }
        public Texture3D(int w, int h, int d, Experimental.Rendering.GraphicsFormat f, Experimental.Rendering.TextureCreationFlags flags) { }
        public void SetPixels32(Color32[] px) { }
        public void Apply(bool mips, bool readOnly) { }
    }
    public static class Mathf
    {
        public const float Deg2Rad = (float)(Math.PI / 180), Rad2Deg = (float)(180 / Math.PI);
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
        public static float Clamp01(float v) => Clamp(v, 0, 1);
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Abs(float v) => Math.Abs(v);
        public static float Exp(float v) => (float)Math.Exp(v);
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static int RoundToInt(float v) => (int)Math.Round(v, MidpointRounding.ToEven);
        public static int FloorToInt(float v) => (int)Math.Floor(v);
        public static float Sin(float v) => (float)Math.Sin(v);
        public static float Cos(float v) => (float)Math.Cos(v);
        public static float Asin(float v) => (float)Math.Asin(v);
        public static float Repeat(float t, float l) => Clamp(t - (float)Math.Floor(t / l) * l, 0, l);
    }
}
namespace TarmacGauntlet.Rendering
{
    public sealed class PostEffects
    {
        public struct Grade
        {
            public float Exposure, Contrast, Saturation, Temperature, Tint;
        }
    }
}
namespace UnityEngine.Experimental.Rendering
{
    public enum GraphicsFormat { R8G8B8A8_UNorm }
    public enum TextureCreationFlags { None }
}
