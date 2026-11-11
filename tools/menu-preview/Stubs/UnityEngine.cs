// Stand-ins for the parts of UnityEngine the management menus use, so the menu code runs outside Unity and its
// element tree can be written out as HTML (Html.cs). Nothing here draws; it only keeps what the tree needs.
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; } = "";
        public HideFlags hideFlags { get; set; }
        public static void Destroy(Object o) { }
        public static T FindAnyObjectByType<T>() where T : Object => GameObject.All.SelectMany(g => g.Components).OfType<T>().FirstOrDefault();
        public static implicit operator bool(Object o) => o != null;
    }

    public enum HideFlags { None, HideAndDontSave }

    public class GameObject : Object
    {
        internal static readonly List<GameObject> All = new List<GameObject>();
        internal readonly List<Component> Components = new List<Component>();
        public GameObject gameObject => this;
        public GameObject(string name = "")
        {
            this.name = name;
            All.Add(this);
        }

        public T AddComponent<T>() where T : Component, new()
        {
            var c = new T { gameObject = this };
            Components.Add(c);
            (c as MonoBehaviour)?.RunAwake();
            return c;
        }

        public T GetComponent<T>() where T : class => Components.OfType<T>().FirstOrDefault();
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public T AddComponent<T>() where T : Component, new() => gameObject.AddComponent<T>();
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; } = true;
    }

    /// <summary>Awake runs on AddComponent; Start runs when the harness calls <see cref="RunStart"/>.</summary>
    public class MonoBehaviour : Behaviour
    {
        internal void RunAwake() => Call("Awake");
        public void RunStart() => Call("Start");
        public void RunMethod(string method) => Call(method);

        private void Call(string method)
        {
            // Unity calls a private Awake declared on a base class too.
            for (Type t = GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                var m = t.GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (m == null) continue;
                m.Invoke(this, null);
                return;
            }
        }
    }

    public sealed class Camera : Behaviour
    {
        public static Camera main => null;
        public Vector3 WorldToScreenPoint(Vector3 p) => new Vector3(0, 0, -1);
    }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject, new() => new T();
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RequireComponentAttribute : Attribute
    {
        public RequireComponentAttribute(Type t) { }
    }

    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { }
    }

    public class Font : Object { }

    public class Shader : Object { }

    public class Material : Object
    {
        public Material(Shader s) { }
        public void SetFloat(string n, float v) { }
    }

    public enum TextureFormat { RGBA32, ARGB32 }
    public enum TextureWrapMode { Clamp, Repeat }
    public enum FilterMode { Point, Bilinear }
    public enum RenderTextureFormat { ARGB32 }

    public class Texture : Object
    {
        public int width { get; protected set; }
        public int height { get; protected set; }
        public TextureWrapMode wrapMode { get; set; }
        public FilterMode filterMode { get; set; }
    }

    public class Texture2D : Texture
    {
        /// <summary>The Resources path it was loaded from, or null when made in code.</summary>
        public string Path;

        /// <summary>Pixels made in code, kept so the HTML can draw small generated textures.</summary>
        public Color[] Pixels;

        public Texture2D(int w, int h, TextureFormat f = TextureFormat.RGBA32, bool mip = false)
        {
            width = w;
            height = h;
            Pixels = new Color[w * h];
        }

        public void SetPixel(int x, int y, Color c) => Pixels[y * width + x] = c;
        public void SetPixels32(Color32[] px) { for (int i = 0; i < px.Length && i < Pixels.Length; i++) Pixels[i] = px[i]; }
        public void Apply() { }
    }

    public class RenderTexture : Texture
    {
        public RenderTexture(int w, int h, int d, RenderTextureFormat f) { width = w; height = h; }
        public void Create() { }
        public void Release() { }
        public RenderTextureFormat format => RenderTextureFormat.ARGB32;
        public static RenderTexture GetTemporary(int w, int h, int d, RenderTextureFormat f) => new RenderTexture(w, h, d, f);
        public static void ReleaseTemporary(RenderTexture t) { }
    }

    public static class Graphics
    {
        public static void Blit(RenderTexture a, RenderTexture b, Material m = null, int pass = 0) { }
    }

    public static class Resources
    {
        /// <summary>The Assets folder; Resources.Load looks in every Resources folder under it.</summary>
        public static string AssetsRoot = "";

        private static readonly Dictionary<string, Object> Cache = new Dictionary<string, Object>();

        public static T Load<T>(string path) where T : Object
        {
            string key = typeof(T).Name + ":" + path;
            if (Cache.TryGetValue(key, out Object o)) return o as T;
            Object made = null;
            if (typeof(T) == typeof(Texture2D))
            {
                string file = Find(path, ".png") ?? Find(path, ".jpg");
                if (file != null) made = new Texture2D(1, 1) { Path = file, name = path };
            }
            else if (typeof(T) == typeof(Font)) made = new Font { name = System.IO.Path.GetFileName(path) };
            else if (typeof(T) == typeof(UnityEngine.UIElements.StyleSheet)) made = new UnityEngine.UIElements.StyleSheet { name = path };
            else if (typeof(T) == typeof(Shader)) made = null;
            Cache[key] = made;
            return made as T;
        }

        private static string Find(string path, string ext)
        {
            foreach (string dir in System.IO.Directory.GetDirectories(AssetsRoot, "Resources", System.IO.SearchOption.AllDirectories))
            {
                string f = System.IO.Path.Combine(dir, path + ext);
                if (System.IO.File.Exists(f)) return f;
            }
            return null;
        }
    }

    public static class Debug
    {
        public static void Log(object o) => Console.Error.WriteLine(o);
        public static void LogWarning(object o) => Console.Error.WriteLine(o);
        public static void LogException(Exception e) => Console.Error.WriteLine(e);
    }

    public static class Application
    {
        public static string persistentDataPath => System.IO.Path.GetTempPath();
        public static event Action quitting { add { } remove { } }
        public static void Quit() { }
    }

    public static class Time
    {
        public static float unscaledTime => 0;
        public static float timeScale { get; set; } = 1;
    }

    public struct Resolution
    {
        public int width, height;
    }

    public enum FullScreenMode { ExclusiveFullScreen, FullScreenWindow, MaximizedWindow, Windowed }

    public static class Screen
    {
        public static int width => 1600;
        public static int height => 900;
        public static FullScreenMode fullScreenMode { get; set; } = FullScreenMode.FullScreenWindow;
        public static Resolution[] resolutions => new[] { new Resolution { width = 1600, height = 900 }, new Resolution { width = 1920, height = 1080 } };
        public static void SetResolution(int w, int h, FullScreenMode m) { }
    }

    public static class QualitySettings
    {
        public static string[] names => new[] { "Low", "Medium", "High" };
        public static int vSyncCount { get; set; } = 1;
        public static int GetQualityLevel() => 2;
        public static void SetQualityLevel(int q, bool apply) { }
    }

    public static class PlayerPrefs
    {
        public static bool HasKey(string k) => false;
        public static int GetInt(string k, int d = 0) => d;
        public static float GetFloat(string k, float d = 0) => d;
        public static void SetInt(string k, int v) { }
        public static void SetFloat(string k, float v) { }
        public static void Save() { }
    }

    public static class AudioListener
    {
        public static float volume { get; set; } = 0.8f;
    }

    public static class ColorUtility
    {
        public static bool TryParseHtmlString(string s, out Color c)
        {
            c = Color.white;
            if (string.IsNullOrEmpty(s) || s[0] != '#' || (s.Length != 7 && s.Length != 9)) return false;
            int v(int i) => Convert.ToInt32(s.Substring(i, 2), 16);
            c = new Color32((byte)v(1), (byte)v(3), (byte)v(5), s.Length == 9 ? (byte)v(7) : (byte)255);
            return true;
        }
    }

    public enum KeyCode { None, Escape, Q, E, DownArrow, UpArrow, Return, KeypadEnter }
    public enum EventType { KeyDown, Other }

    public sealed class Event
    {
        public static Event current => null;
        public EventType type;
        public KeyCode keyCode;
        public void Use() { }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1);
        public static Color black => new Color(0, 0, 0);
        public static Color clear => new Color(0, 0, 0, 0);
        public static Color Lerp(Color a, Color b, float t) => new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
        public static implicit operator Color(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
        public static bool operator ==(Color x, Color y) => x.r == y.r && x.g == y.g && x.b == y.b && x.a == y.a;
        public static bool operator !=(Color x, Color y) => !(x == y);
        public override bool Equals(object o) => o is Color c && c == this;
        public override int GetHashCode() => (r, g, b, a).GetHashCode();
        public string Css => $"rgba({Math.Round(r * 255)}, {Math.Round(g * 255)}, {Math.Round(b * 255)}, {Math.Round(a, 3).ToString(System.Globalization.CultureInfo.InvariantCulture)})";
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color32(Color c) => new Color32((byte)Math.Round(c.r * 255), (byte)Math.Round(c.g * 255), (byte)Math.Round(c.b * 255), (byte)Math.Round(c.a * 255));
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
        public float sqrMagnitude => x * x + y * y;
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0);
    }

    public struct Vector2Int : IEquatable<Vector2Int>
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public bool Equals(Vector2Int o) => x == o.x && y == o.y;
        public override bool Equals(object o) => o is Vector2Int v && Equals(v);
        public override int GetHashCode() => (x, y).GetHashCode();
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
        public float xMin => x;
        public float yMin => y;
        public float xMax => x + width;
        public float yMax => y + height;
        public bool Contains(Vector2 p) => p.x >= x && p.x <= xMax && p.y >= y && p.y <= yMax;
        public bool Contains(Vector3 p) => Contains((Vector2)p);
    }

    public static class Mathf
    {
        public const float Deg2Rad = (float)(Math.PI / 180), Rad2Deg = (float)(180 / Math.PI);
        public static float Abs(float v) => Math.Abs(v);
        public static int Abs(int v) => Math.Abs(v);
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
        public static float Clamp01(float v) => Clamp(v, 0, 1);
        public static float Sin(float v) => (float)Math.Sin(v);
        public static float Cos(float v) => (float)Math.Cos(v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Max(params float[] v) => v.Max();
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Pow(float a, float b) => (float)Math.Pow(a, b);
        public static float Round(float v) => (float)Math.Round(v);
        public static int RoundToInt(float v) => (int)Math.Round(v);
        public static int FloorToInt(float v) => (int)Math.Floor(v);
        public static int CeilToInt(float v) => (int)Math.Ceiling(v);
        public static float SmoothStep(float a, float b, float t) { t = Clamp01(t); t = t * t * (3 - 2 * t); return a + (b - a) * t; }
    }
}

namespace UnityEngine.SceneManagement
{
    public static class SceneManager
    {
        public static void LoadScene(string name) { }
    }
}

#if UNITY_EDITOR
#endif
