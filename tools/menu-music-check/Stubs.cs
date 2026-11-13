// Stand-ins for the parts of Unity the menu music touches. The audio source only records what it was told to play.
using System;
using System.Collections.Generic;
using System.Linq;
namespace UnityEngine {
  public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, SubsystemRegistration }
  [AttributeUsage(AttributeTargets.Method)] public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { } }
  public enum FindObjectsSortMode { None }
  public class Object {
    public string name; public static void DontDestroyOnLoad(Object o) { }
    public static T[] FindObjectsByType<T>(FindObjectsSortMode m) where T : Object => typeof(T) == typeof(AudioListener) ? (T[])(object)AudioListener.All.ToArray() : new T[0];
    public static void Destroy(Object o) { if (o is AudioListener l) AudioListener.All.Remove(l); }
    public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b) || (a is null ? b.Gone : b is null ? a.Gone : false);
    public static bool operator !=(Object a, Object b) => !(a == b);
    public override bool Equals(object o) => ReferenceEquals(this, o); public override int GetHashCode() => base.GetHashCode();
    protected virtual bool Gone => false;
  }
  public class AudioListener : Behaviour { public static List<AudioListener> All = new List<AudioListener>(); public AudioListener() { All.Add(this); } protected override bool Gone => !All.Contains(this); }
  public class AudioClip : Object { }
  public class Component : Object { public GameObject gameObject; }
  public class Behaviour : Component { }
  public class MonoBehaviour : Behaviour { }
  public class GameObject : Object {
    public static List<MonoBehaviour> Live = new List<MonoBehaviour>();
    public GameObject(string n) { name = n; }
    public T AddComponent<T>() where T : Component, new() { var c = new T { gameObject = this }; if (c is MonoBehaviour m) Live.Add(m); c.GetType().GetMethod("Awake", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)?.Invoke(c, null); return c; }
  }
  public class AudioSource : Behaviour {
    public bool playOnAwake, loop, bypassListenerEffects; public float spatialBlend, volume; public AudioClip clip; public bool isPlaying; public List<AudioClip> Played = new List<AudioClip>();
    public void Play() { isPlaying = true; Played.Add(clip); } public void Stop() { isPlaying = false; }
  }
  public static class Resources { public static AudioClip[] Clips; public static T[] LoadAll<T>(string p) where T : Object => (T[])(object)Clips; }
  public static class Time { public static float unscaledDeltaTime = 0.1f; }
  public static class Random { static System.Random r = new System.Random(1); public static int Range(int a, int b) => r.Next(a, b); }
  public static class Mathf { public static float MoveTowards(float c, float t, float d) => Math.Abs(t - c) <= d ? t : c + Math.Sign(t - c) * d; }
}
namespace UnityEngine.SceneManagement {
  public enum LoadSceneMode { Single }
  public struct Scene { public string name; }
  public static class SceneManager { public static Scene Active; public static event Action<Scene, LoadSceneMode> sceneLoaded; public static Scene GetActiveScene() => Active; public static void Fire(Scene s) => sceneLoaded?.Invoke(s, LoadSceneMode.Single); }
}
namespace TarmacGauntlet.Rendering {
  /// <summary>The loader's three moments, driven by hand: the load starts, the new scene is drawn, the screen is gone.</summary>
  public static class SceneLoader {
    public static event Action Leaving, Arrived; public static string Target { get; private set; } public static bool Loading { get; private set; }
    public static void Load(string scene) { Target = scene; Loading = true; Leaving?.Invoke(); }
    public static void Arrive() { UnityEngine.SceneManagement.SceneManager.Active = new UnityEngine.SceneManagement.Scene { name = Target }; Arrived?.Invoke(); }
    public static void Done() => Loading = false;
  }
}
namespace TarmacGauntlet.Management { public static class RaceSummaryScreen { public const string HubScene = "TeamHq"; } public static class GameSettings { public static float Music = 0.7f; } }
