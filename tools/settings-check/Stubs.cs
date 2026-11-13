using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

// Stand-ins for the parts of Unity the settings code touches. PlayerPrefs keeps only what was saved, in a file, so a
// second launch sees exactly what a restarted game would.
namespace UnityEngine
{
    public class Object { public string name; }

    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, SubsystemRegistration }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { }
    }

    public static class Mathf
    {
        public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static int RoundToInt(float v) => (int)Math.Round(v);
    }

    public struct Vector2Int : IEquatable<Vector2Int>
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public bool Equals(Vector2Int o) => x == o.x && y == o.y;
        public override bool Equals(object o) => o is Vector2Int v && Equals(v);
        public override int GetHashCode() => x * 73856093 ^ y;
        public static bool operator ==(Vector2Int a, Vector2Int b) => a.Equals(b);
        public static bool operator !=(Vector2Int a, Vector2Int b) => !a.Equals(b);
        public override string ToString() => x + " x " + y;
    }

    public struct Resolution { public int width, height; }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }

    public class Shader : Object
    {
        public static readonly Dictionary<int, Vector4> Globals = new Dictionary<int, Vector4>();
        private static readonly Dictionary<string, int> ids = new Dictionary<string, int>();
        public static int PropertyToID(string name) => ids.TryGetValue(name, out int id) ? id : ids[name] = ids.Count + 1;
        public static void SetGlobalVector(int id, Vector4 v) => Globals[id] = v;
        public static void SetGlobalFloat(int id, float v) => Globals[id] = new Vector4(v, 0, 0, 0);
    }

    public enum FullScreenMode { ExclusiveFullScreen, FullScreenWindow, MaximizedWindow, Windowed }

    /// <summary>A 1920 x 1080 monitor. A launch opens borderless at full size, as if Unity remembered nothing.</summary>
    public static class Screen
    {
        public static int width = 1920, height = 1080;
        public static FullScreenMode fullScreenMode = FullScreenMode.FullScreenWindow;
        public static Resolution[] resolutions => new[]
        {
            new Resolution { width = 1280, height = 720 }, new Resolution { width = 1600, height = 900 },
            new Resolution { width = 1920, height = 1080 },
        };
        public static void SetResolution(int w, int h, FullScreenMode m) { width = w; height = h; fullScreenMode = m; }
    }

    public static class QualitySettings
    {
        public static int vSyncCount = 1;
        public static Rendering.RenderPipelineAsset renderPipeline;
    }

    public static class AudioListener { public static float volume = 1; }

    public static class Application
    {
        public static event Action quitting;
        public static void Quit() => quitting?.Invoke();
    }

    public static class Debug
    {
        public static void LogError(object m) => Console.WriteLine("ERROR " + m);
    }

    public static class Resources
    {
        private static readonly Dictionary<string, Object> loaded = new Dictionary<string, Object>();

        public static T Load<T>(string path) where T : Object, new()
        {
            if (!loaded.TryGetValue(path, out Object o))
            {
                o = new T { name = path };
                if (o is Rendering.Universal.UniversalRendererData r)
                {
                    r.rendererFeatures.Add(new Rendering.Universal.ScreenSpaceAmbientOcclusion());
                    r.rendererFeatures.Add(new Rendering.Universal.ScreenSpaceShadows());
                }
                loaded[path] = o;
            }
            return (T)o;
        }
    }

    public enum CameraType { Game, SceneView, Preview }

    public class Camera : Object { public CameraType cameraType; }

    public static class PlayerPrefs
    {
        public static string File;
        private static Dictionary<string, string> live;

        private static Dictionary<string, string> Live => live ??= System.IO.File.Exists(File)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(File))
            : new Dictionary<string, string>();

        public static bool HasKey(string k) => Live.ContainsKey(k);
        public static int GetInt(string k, int d = 0) => Live.TryGetValue(k, out string v) ? int.Parse(v, CultureInfo.InvariantCulture) : d;
        public static float GetFloat(string k, float d = 0) => Live.TryGetValue(k, out string v) ? float.Parse(v, CultureInfo.InvariantCulture) : d;
        public static void SetInt(string k, int v) => Live[k] = v.ToString(CultureInfo.InvariantCulture);
        public static void SetFloat(string k, float v) => Live[k] = v.ToString("R", CultureInfo.InvariantCulture);
        /// <summary>Only a save reaches the file, so a setting the game forgets to save is gone at the next launch.</summary>
        public static void Save() => System.IO.File.WriteAllText(File, JsonSerializer.Serialize(Live));
    }
}

namespace UnityEngine.Rendering
{
    public class RenderPipelineAsset : Object { }
    public struct ScriptableRenderContext { }

    public static class RenderPipelineManager
    {
        public static event Action<ScriptableRenderContext, Camera> beginCameraRendering;
    }
}

namespace UnityEngine.Rendering.Universal
{
    public class UniversalRenderPipelineAsset : RenderPipelineAsset { }

    public class ScriptableRendererFeature : Object
    {
        public bool isActive { get; private set; } = true;
        public void SetActive(bool on) => isActive = on;
    }

    public class ScreenSpaceAmbientOcclusion : ScriptableRendererFeature { }

    /// <summary>URP's own is internal; the game finds it by its type's name.</summary>
    public class ScreenSpaceShadows : ScriptableRendererFeature { }

    public class UniversalRendererData : Object
    {
        public List<ScriptableRendererFeature> rendererFeatures = new List<ScriptableRendererFeature>();
    }

    public enum AntialiasingMode { None, FastApproximateAntialiasing, SubpixelMorphologicalAntiAliasing, TemporalAntiAliasing }
    public enum AntialiasingQuality { Low, Medium, High }

    public class UniversalAdditionalCameraData
    {
        public bool renderPostProcessing;
        public AntialiasingMode antialiasing;
        public AntialiasingQuality antialiasingQuality;
    }

    public static class CameraExtensions
    {
        public static UniversalAdditionalCameraData GetUniversalAdditionalCameraData(this Camera c) => new UniversalAdditionalCameraData();
    }
}
