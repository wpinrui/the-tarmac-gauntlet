// Stand-ins for the parts of Unity and the race the pit stop and collision sounds touch. Audio sources only record
// what they were told to play; the kit is a plain bag of fields the check sets frame by frame.
using System;
using System.Collections.Generic;
namespace UnityEngine {
  [AttributeUsage(AttributeTargets.Class)] public sealed class RequireComponent : Attribute { public RequireComponent(Type t) { } }
  [AttributeUsage(AttributeTargets.Class)] public sealed class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int o) { } }
  public struct Vector3 {
    public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    public static Vector3 zero => default;
    public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
    public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
    public static Vector3 operator *(Vector3 a, float k) => new Vector3(a.x * k, a.y * k, a.z * k);
    public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
    public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
  }
  public static class Mathf {
    public static float Max(float a, float b) => Math.Max(a, b); public static int Max(int a, int b) => Math.Max(a, b);
    public static float Min(float a, float b) => Math.Min(a, b); public static int Min(int a, int b) => Math.Min(a, b);
    public static float Abs(float a) => Math.Abs(a); public static float Exp(float a) => (float)Math.Exp(a);
    public static float Clamp01(float a) => a < 0 ? 0 : a > 1 ? 1 : a;
    public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
    public static float InverseLerp(float a, float b, float v) => Clamp01((v - a) / (b - a));
    public static float MoveTowards(float c, float t, float d) => Math.Abs(t - c) <= d ? t : c + Math.Sign(t - c) * d;
  }
  public static class Time { public static float time, deltaTime = 1f / 60f, unscaledTime, unscaledDeltaTime = 1f / 60f; public static int frameCount;
    public static void Step() { time += deltaTime; unscaledTime += unscaledDeltaTime; frameCount++; } }
  public class Object {
    static int ids; readonly int id = ++ids; public string name;
    public int GetInstanceID() => id;
    public static T FindFirstObjectByType<T>() where T : Object { foreach (var g in GameObject.All) foreach (var c in g.Components) if (c is T t) return t; return null; }
    public static bool operator ==(Object a, Object b) => ReferenceEquals(a, null) ? ReferenceEquals(b, null) : ReferenceEquals(a, b);
    public static bool operator !=(Object a, Object b) => !(a == b);
    public override bool Equals(object o) => ReferenceEquals(this, o); public override int GetHashCode() => id;
  }
  public class Transform { public Vector3 position; public Transform parent; public void SetParent(Transform p, bool w) { parent = p; } }
  public class Component : Object {
    public GameObject gameObject; public Transform transform => gameObject.transform;
    public bool TryGetComponent<T>(out T c) { foreach (var x in gameObject.Components) if (x is T t) { c = t; return true; } c = default; return false; }
  }
  public class Behaviour : Component { }
  public class MonoBehaviour : Behaviour { }
  public sealed class GameObject : Object {
    public static List<GameObject> All = new List<GameObject>();
    public readonly List<Component> Components = new List<Component>(); public readonly Transform transform = new Transform();
    public GameObject(string n) { name = n; All.Add(this); }
    public T AddComponent<T>() where T : Component, new() {
      var c = new T { gameObject = this, name = name }; Components.Add(c);
      c.GetType().GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(c, null); return c; }
  }
  public enum AudioRolloffMode { Logarithmic, Linear, Custom }
  public class AudioClip : Object { public float length = 1f; }
  public class AudioListener : Behaviour { }
  public class AudioSource : Behaviour {
    public bool playOnAwake, loop, isPlaying; public float spatialBlend, dopplerLevel, volume = 1, pitch = 1, minDistance, maxDistance, time;
    public int priority; public AudioRolloffMode rolloffMode; public AudioClip clip; public int Plays;
    public void Play() { isPlaying = true; Plays++; } public void Stop() { isPlaying = false; }
  }
  public static class Resources {
    public static Dictionary<string, int> Takes = new Dictionary<string, int>();
    public static T[] LoadAll<T>(string path) where T : Object {
      int n = Takes.TryGetValue(path, out int k) ? k : 0; var a = new T[n];
      for (int i = 0; i < n; i++) a[i] = (T)(object)new AudioClip { name = $"{path}/{i + 1}" }; return a; }
  }
  public class Rigidbody : Component { }
  public class Collider : Component { public Rigidbody attachedRigidbody; }
  public struct ContactPoint { public Vector3 point, normal; }
  public class Collision {
    public Collider collider; public Vector3 relativeVelocity; public ContactPoint[] Contacts = new ContactPoint[0];
    public int contactCount => Contacts.Length; public ContactPoint GetContact(int i) => Contacts[i];
  }
}
namespace TarmacGauntlet.Rendering { public static class QualityLevels { public static int Level = 1; } }
namespace TarmacGauntlet.Raceday {
  using UnityEngine;
  public sealed class CarController : MonoBehaviour { }
  public enum KitItem { Nozzle, GunFrontLeft, GunFrontRight, GunRearLeft, GunRearRight, Lance, Lollipop, Extinguisher, TyreFrontLeft, TyreFrontRight, TyreRearLeft, TyreRearRight, Trolley }
  public struct KitPlace { public Vector3 Position; }
  public sealed class KitWork {
    public readonly KitPlace[] Pieces = new KitPlace[13]; public readonly bool[] OnHub = new bool[4];
    public bool NozzleOn, LanceIn, Busy = true; public float Lift;
    public KitItem GunOf(int w) => KitItem.GunFrontLeft + w;
  }
  public sealed class PitKitWork : MonoBehaviour {
    public KitWork[] Works = new KitWork[0]; public bool[] Lives = new bool[0];
    public KitWork Work(int i) => Works[i]; public int Count => Works.Length; public bool Live(int i) => Lives[i];
    public Vector3 CarAt(int i) => new Vector3(i * 10f, 0f, 0f);
  }
}
