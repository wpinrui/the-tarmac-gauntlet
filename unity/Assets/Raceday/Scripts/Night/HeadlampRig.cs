using System.Collections.Generic;
using TarmacGauntlet.Visuals;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The headlamps' real lights: one URP spot light for each of the cars nearest the camera, up to the graphics option
    /// (<see cref="GraphicsOptions.RealLights"/>), at the middle of the car's lamps just ahead of its bumper, aimed down the
    /// road a little below level. A cookie shapes it as a low beam: wide, cut off just above level so it never lights the
    /// sky or a driver's eyes, and fading out below. Its intensity and every surface it lights are bounded by the night's
    /// budget (<see cref="NightBudget"/>, Lamps.hlsl), however close the car in front is and however many cars light the
    /// same spot. A light moving to another car fades out first and back in there, so nothing pops.
    /// </summary>
    public sealed class HeadlampRig : MonoBehaviour
    {
        /// <summary>How much of their light the headlamps throw now, 0 to 1 (<see cref="NightLighting"/>).</summary>
        [System.NonSerialized] public float Level;

        /// <summary>Real seconds between looks at which cars are nearest, and a light's fade out or in, real seconds.</summary>
        private const float Check = 0.25f, Fade = 0.3f;
        /// <summary>The cookie's size, texels.</summary>
        private const int CookieSize = 64;

        private sealed class Slot
        {
            public Light Light;
            public CarLights At, Want;
            public float Fade;
        }

        private Slot[] slots;
        private Texture2D cookie;
        private float nextCheck;
        private readonly List<CarLights> near = new List<CarLights>();
        private readonly HashSet<CarLights> wanted = new HashSet<CarLights>();
        private Vector3 eye;

        private void Start()
        {
            cookie = Cookie();
            slots = new Slot[Mathf.Max(GraphicsOptions.RealLights.Value, 0)];
            for (int i = 0; i < slots.Length; i++)
            {
                var go = new GameObject("Headlamp " + (i + 1));
                go.transform.SetParent(transform, false);
                Light l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.color = NightBudget.Headlamp;
                l.range = NightBudget.HeadlampRange;
                l.spotAngle = NightBudget.HeadlampCone;
                l.innerSpotAngle = NightBudget.HeadlampCore;
                l.cookie = cookie;
                l.shadows = LightShadows.None;
                l.enabled = false;
                slots[i] = new Slot { Light = l };
            }
        }

        private void OnDestroy()
        {
            if (cookie != null) Destroy(cookie);
        }

        private void LateUpdate()
        {
            if (slots == null) return;
            if (Time.unscaledTime >= nextCheck)
            {
                nextCheck = Time.unscaledTime + Check;
                Assign();
            }
            float step = Time.unscaledDeltaTime / Fade;
            foreach (Slot s in slots)
            {
                if (s.Want != s.At)
                {
                    s.Fade = Mathf.Max(0f, s.Fade - step);
                    if (s.Fade <= 0f) s.At = s.Want;
                }
                else if (s.At != null) s.Fade = Mathf.Min(1f, s.Fade + step);
                bool on = s.At != null && s.Fade > 0f && Level > 0f;
                if (on) Place(s.Light, s.At, Level * s.Fade);
                if (s.Light.enabled != on) s.Light.enabled = on;
            }
        }

        /// <summary>The cars nearest the camera get the lights; a light already on one of them stays there.</summary>
        private void Assign()
        {
            Camera cam = Camera.main;
            near.Clear();
            wanted.Clear();
            if (cam != null && Level > 0f)
            {
                eye = cam.transform.position;
                foreach (CarLights c in CarLights.All)
                    if (c != null && c.HasHeadlamps) near.Add(c);
                near.Sort((a, b) => (a.transform.position - eye).sqrMagnitude.CompareTo((b.transform.position - eye).sqrMagnitude));
                for (int i = 0; i < near.Count && i < slots.Length; i++) wanted.Add(near[i]);
            }
            var free = new List<CarLights>(wanted);
            foreach (Slot s in slots)
                if (s.At != null && wanted.Contains(s.At)) { s.Want = s.At; free.Remove(s.At); }
                else s.Want = null;
            foreach (Slot s in slots)
            {
                if (s.Want != null || free.Count == 0) continue;
                s.Want = free[0];
                free.RemoveAt(0);
            }
        }

        private static void Place(Light l, CarLights car, float level)
        {
            Transform t = car.transform;
            Vector3 at = t.TransformPoint(new Vector3(0f, car.Nose.y, car.Front + NightBudget.HeadlampAhead));
            Vector3 aim = Quaternion.AngleAxis(NightBudget.HeadlampDip, t.right) * t.forward;
            l.transform.SetPositionAndRotation(at, Quaternion.LookRotation(aim, t.up));
            l.intensity = NightBudget.HeadlampIntensity * level / NightBudget.Luma(NightBudget.Headlamp);
        }

        private static Texture2D Cookie()
        {
            var tex = new Texture2D(CookieSize, CookieSize, TextureFormat.RGBA32, false, true)
            {
                name = "Headlamp beam", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            var px = new Color[CookieSize * CookieSize];
            for (int j = 0; j < CookieSize; j++)
                for (int i = 0; i < CookieSize; i++)
                {
                    float u = (i + 0.5f) / CookieSize * 2f - 1f, v = (j + 0.5f) / CookieSize * 2f - 1f;
                    float b = LampLight.Beam(u, v);
                    px[j * CookieSize + i] = new Color(b, b, b, b);
                }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
