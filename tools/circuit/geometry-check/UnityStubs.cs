// Just enough of UnityEngine for the circuit builder to run outside Unity.
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator /(Vector2 a, float d) => new Vector2(a.x / d, a.y / d);
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public float magnitude => (float)Math.Sqrt(x * x + y * y);
        public Vector2 normalized { get { float m = magnitude; return m > 1e-5f ? this / m : zero; } }
        public override bool Equals(object o) => o is Vector2 v && v.x == x && v.y == y;
        public override int GetHashCode() => HashCode.Combine(x, y); }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => a * d;
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized { get { float m = magnitude; return m > 1e-5f ? this / m : new Vector3(0, 0, 0); } }
        public void Normalize() { this = normalized; }
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { t = Mathf.Clamp01(t); return a + (b - a) * t; }
        public static Vector3 up => new Vector3(0, 1, 0); public static Vector3 down => new Vector3(0, -1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1); public static Vector3 back => new Vector3(0, 0, -1);
        public static Vector3 left => new Vector3(-1, 0, 0); public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 zero => new Vector3(0, 0, 0);
        public override bool Equals(object o) => o is Vector3 v && v.x == x && v.y == y && v.z == z;
        public override int GetHashCode() => HashCode.Combine(x, y, z);
    }
    public static class Mathf
    {
        public const float Deg2Rad = (float)(Math.PI / 180);
        public const float PI = (float)Math.PI;
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
        public static float Clamp01(float v) => Clamp(v, 0, 1);
        public static float Max(float a, float b) => a > b ? a : b; public static int Max(int a, int b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b; public static int Min(int a, int b) => a < b ? a : b;
        public static float Abs(float v) => Math.Abs(v);
        public static float Sqrt(float v) => (float)System.Math.Sqrt(v);
        public static float Sign(float v) => v >= 0 ? 1 : -1;
        public static float Repeat(float t, float l) => Clamp(t - (float)Math.Floor(t / l) * l, 0, l);
        public static int RoundToInt(float v) => (int)Math.Round(v, MidpointRounding.ToEven);
        public static int FloorToInt(float v) => (int)Math.Floor(v);
        public static int CeilToInt(float v) => (int)Math.Ceiling(v);
        public static float Floor(float v) => (float)Math.Floor(v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float Sin(float v) => (float)Math.Sin(v); public static float Asin(float v) => (float)Math.Asin(v); public static float Cos(float v) => (float)Math.Cos(v);
        public static float DeltaAngle(float a, float b) { float d = Repeat(b - a, 360); if (d > 180) d -= 360; return d; }
        public const float Rad2Deg = (float)(180 / Math.PI);
        public static float Atan(float v) => (float)Math.Atan(v); public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Pow(float a, float b) => (float)Math.Pow(a, b); public static float Exp(float v) => (float)Math.Exp(v);
        public static float MoveTowards(float c, float t, float d) => Math.Abs(t - c) <= d ? t : c + Math.Sign(t - c) * d;
    }
    public class Material { }
    public class TextAsset { public string text; }
    public static class Resources { public static T Load<T>(string p) where T : class => null; }
    public static class JsonUtility { public static T FromJson<T>(string s) => default; }
    public class Mesh { public string name; public Rendering.IndexFormat indexFormat;
        public void SetVertices(List<Vector3> v) { } public void SetNormals(List<Vector3> v) { } public void SetUVs(int c, List<Vector2> v) { }
        public void SetTriangles(List<int> t, int s) { } public void RecalculateBounds() { } }
}
namespace UnityEngine.Rendering { public enum IndexFormat { UInt16, UInt32 } }
namespace TarmacGauntlet.Raceday
{
    public sealed class CircuitMaterials
    {
        public UnityEngine.Material Asphalt, PitAsphalt, White, Yellow, Kerb, Verge, Grass, Gravel, Armco, Tyres, Concrete, Hatch, Terrain, GarageInside, Bollard;
    }
}
