using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.Visuals;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Switches the night lighting on as the sky darkens: the floodlights' lenses and pools, the stands' glow and every
    /// car's lamps, all from how dark the sky is for the lights (<see cref="TimeOfDay.Lights"/>), which
    /// brings them on earlier under cloud and rain. The ground is lit by the pools alone. Real
    /// lights are kept for the cars near the camera, and light nothing but cars: as many as the settings allow, a third
    /// of them the headlamps of the cars nearest the camera and the rest at the floodlights nearest the camera's view. A light
    /// moving to another floodlight or car fades out first and back in there, so nothing pops. With light shadows on, the
    /// nearer half of the floodlights' and of the headlamps' real lights cast soft shadows on the cars. Two uplights light
    /// the start gantry while the camera is near it.
    /// </summary>
    public sealed class NightLights : MonoBehaviour
    {
        public TimeOfDay Sky;
        public Floodlights Layout;
        public FloodMaterials Materials;
        public StandMaterials Stands;

        /// <summary>The real lights from the settings: a third of them headlamps, the rest at floodlights.</summary>
        public static int RealLights => GraphicsOptions.RealLights.Value;
        /// <summary>Real seconds between looks at the sky and at where the camera is.</summary>
        private const float Check = 0.25f;
        /// <summary>A real light fades in or out over this long, real seconds.</summary>
        private const float Fade = 0.35f;
        /// <summary>The camera's view is centred this far ahead of it, m.</summary>
        private const float FocusAhead = 30f;
        /// <summary>Real lights only go to floodlights and cars nearer the view than this, m.</summary>
        private const float FloodReach = 160f, HeadReach = 120f;
        private const float FloodIntensity = 2.5f, HeadRange = 70f, HeadCone = 55f;
        /// <summary>
        /// A headlamp light's intensity in Linear, candela-like: it lights a white car 15 m ahead to about a tenth of white,
        /// clearly lit against the night but with its shading and paint kept. Set directly: matched from the Gamma lamp at
        /// 15 m (LightFalloff.UrpLinear) it came out near 1000 and blew every car ahead out to white.
        /// </summary>
        private const float HeadIntensity = 24f;
        /// <summary>How far ahead the headlamps mostly light the cars in front, m.</summary>
        private const float HeadLights = 15f;
        /// <summary>
        /// A car's headlamp light sits this far ahead of its bumper, m, so it never lights its own car: from the nose it
        /// lit the bonnet's leading edge a hand's width away, hundreds of times brighter than the cars it was set for.
        /// </summary>
        private const float HeadAhead = 0.15f;
        /// <summary>
        /// A car close in front is lit at most this many times as brightly as one <see cref="HeadLights"/> ahead: the light
        /// dims as a car closes on it, where the square of the distance would light a car 3 m off 25 times as brightly.
        /// </summary>
        private const float HeadNearest = 2f;
        /// <summary>A car's body reaches this far from its middle, along it and across it, m.</summary>
        private const float CarHalfLength = 2.2f, CarHalfWidth = 0.9f;

        private sealed class Slot
        {
            public Light Light;
            /// <summary>The floodlight or car the light is at, and the one it should move to; -1 for none.</summary>
            public int At = -1, Want = -1;
            public float Level;
            /// <summary>How far from the light the things it lights mostly are, m: its brightness is matched there.</summary>
            public float Lights;
            /// <summary>The share of its brightness the light gives now: less with a car close in front of it.</summary>
            public float Scale = 1f;
        }

        /// <summary>The gantry's uplights are on only while the camera is this near the start line, m.</summary>
        private const float UplightReach = 350f;
        private const float UplightIntensity = 1.5f, UplightRange = 26f, UplightCone = 50f;
        /// <summary>How far the uplights are from the gantry they light, m.</summary>
        private const float UplightLights = 8f;

        /// <summary>The floodlights' beams in the volumetric mist come from the lamps nearer the camera than this, m.</summary>
        private const float BeamReach = 400f;
        /// <summary>How bright a beam lights the mist, and how far past its target it carries.</summary>
        private const float BeamStrength = 30000f, BeamCarry = 1.6f;
        /// <summary>
        /// The beams' strength was set in Gamma colour space, where the glow added to sRGB values. This keeps a beam
        /// glowing at a fifth of white as bright in Linear; a brighter glow comes out a little dimmer and a fainter one a
        /// little brighter, as linear light spreads it.
        /// </summary>
        private static readonly float BeamLinear = LinearLight.Linear(0.2f) / 0.2f;

        private Slot[] floods, heads;
        private readonly Vector4[] beamPos = new Vector4[Atmosphere.MostBeams], beamAim = new Vector4[Atmosphere.MostBeams];
        private int beamCount;
        /// <summary>Light shadows on, read at the start of the race.</summary>
        private bool shadows;
        private Light[] uplights;
        private float nextCheck, night = -1f;
        private readonly List<int> picks = new List<int>();
        private readonly List<float> scores = new List<float>();

        private void Start()
        {
            int real = RealLights;
            shadows = GraphicsOptions.LightShadows.On;
            heads = new Slot[Mathf.RoundToInt(real / 3f)];
            floods = new Slot[real - heads.Length];
            for (int i = 0; i < floods.Length; i++) floods[i] = new Slot { Light = Spot("Floodlight " + (i + 1), Materials.Tint) };
            for (int i = 0; i < heads.Length; i++) heads[i] = new Slot { Light = Spot("Headlamps " + (i + 1), LampMaterials.Shared.HeadTint) };
            uplights = new Light[Layout.Uplights.Count];
            for (int i = 0; i < uplights.Length; i++)
            {
                Light l = Spot("Gantry uplight " + (i + 1), Materials.Tint);
                l.cullingMask = ~(1 << CarBuilder.CarLayer);
                l.transform.SetPositionAndRotation(Layout.Uplights[i].position, Quaternion.LookRotation(Layout.Uplights[i].aim));
                l.range = UplightRange;
                l.spotAngle = UplightCone;
                l.innerSpotAngle = UplightCone * 0.6f;
                uplights[i] = l;
            }
        }

        private void OnDestroy() => HeatHazeFeature.HeadlampBeams = false;

        private Light Spot(string name, Color colour)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            Light l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.renderMode = LightRenderMode.ForcePixel;
            l.shadows = LightShadows.None;
            l.cullingMask = 1 << CarBuilder.CarLayer;
            l.color = colour;
            l.enabled = false;
            return l;
        }

        private void Update()
        {
            if (Sky == null || Layout == null) return;
            if (Time.unscaledTime >= nextCheck)
            {
                nextCheck = Time.unscaledTime + Check;
                float now = Sky.Lights;
                if (Mathf.Abs(now - night) >= 0.005f)
                {
                    night = now;
                    Materials.Night(night);
                    LampMaterials.Shared.Night(night);
                    Stands?.Night(night, Materials.Tint);
                    bool beams = LampMaterials.Shared.BeamsOn;
                    foreach (CarLamps c in CarLamps.All) c.ShowBeam(beams);
                }
                Assign();
            }
        }

        private void LateUpdate()
        {
            if (floods == null) return;
            float on = NightLook.On(Mathf.Max(night, 0f));
            Atmosphere.Beams(beamPos, beamAim, on > 0.001f ? beamCount : 0, NightLook.Flood.linear * (on * BeamStrength * BeamLinear));
            float step = Time.unscaledDeltaTime / Fade;
            foreach (Slot s in floods) Follow(s, step, on * FloodIntensity, PlaceFlood, false);
            foreach (Slot s in heads) Follow(s, step, on * HeadIntensity, PlaceHead, true);
            Camera cam = Camera.main;
            foreach (Light l in uplights)
            {
                bool near = cam != null && (cam.transform.position - l.transform.position).sqrMagnitude < UplightReach * UplightReach;
                l.intensity = LightFalloff.UrpLinear(l.color, on * UplightIntensity, UplightRange, UplightLights);
                bool lit = near && on > 0.001f;
                if (l.enabled != lit) l.enabled = lit;
            }
        }

        /// <summary>Picks the floodlights and the cars the real lights should be at now.</summary>
        private void Assign()
        {
            Camera cam = Camera.main;
            bool dark = night > 0.01f && cam != null;
            Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
            Vector3 focus = cam != null ? eye + cam.transform.forward * FocusAhead : Vector3.zero;

            picks.Clear();
            scores.Clear();
            if (dark)
                for (int i = 0; i < Layout.Lights.Count; i++)
                {
                    float d = (Layout.Lights[i].Target - focus).sqrMagnitude;
                    if (d < FloodReach * FloodReach) Insert(i, d, floods.Length);
                }
            Share(floods, picks);
            Shade(floods, picks, floods.Length / 2);

            picks.Clear();
            scores.Clear();
            if (dark)
                for (int i = 0; i < CarLamps.All.Count; i++)
                {
                    if (!CarLamps.All[i].HasHeadlamps) continue;
                    float d = (CarLamps.All[i].transform.position - eye).sqrMagnitude;
                    if (d < HeadReach * HeadReach) Insert(i, d, heads.Length);
                }
            Share(heads, picks);
            Shade(heads, picks, (heads.Length + 1) / 2);

            picks.Clear();
            scores.Clear();
            if (dark)
                for (int i = 0; i < Layout.Lights.Count; i++)
                {
                    float d = (Layout.Lights[i].Position - eye).sqrMagnitude;
                    if (d < BeamReach * BeamReach) Insert(i, d, Atmosphere.MostBeams);
                }
            beamCount = picks.Count;
            for (int k = 0; k < beamCount; k++)
            {
                Floodlight f = Layout.Lights[picks[k]];
                beamPos[k] = new Vector4(f.Position.x, f.Position.y, f.Position.z, f.Reach * BeamCarry);
                beamAim[k] = new Vector4(f.Aim.x, f.Aim.y, f.Aim.z, Mathf.Cos(f.Cone * 0.5f * Mathf.Deg2Rad));
            }
        }

        /// <summary>With light shadows on, the lights going to the nearest few picks cast shadows; the others do not.</summary>
        private void Shade(Slot[] slots, List<int> nearestFirst, int count)
        {
            foreach (Slot s in slots)
            {
                int rank = s.Want >= 0 ? nearestFirst.IndexOf(s.Want) : -1;
                LightShadows want = shadows && rank >= 0 && rank < count ? LightShadows.Soft : LightShadows.None;
                if (s.Light.shadows != want) s.Light.shadows = want;
            }
        }

        /// <summary>Keeps the nearest few: inserts index i at score d, dropping the worst past the limit.</summary>
        private void Insert(int i, float d, int limit)
        {
            int at = scores.Count;
            while (at > 0 && scores[at - 1] > d) at--;
            if (at >= limit) return;
            scores.Insert(at, d);
            picks.Insert(at, i);
            if (scores.Count > limit)
            {
                scores.RemoveAt(limit);
                picks.RemoveAt(limit);
            }
        }

        /// <summary>Lights already at a pick stay there; the others take the picks left over, or go out.</summary>
        private static void Share(Slot[] slots, List<int> wanted)
        {
            var left = new List<int>(wanted);
            foreach (Slot s in slots)
                if (s.At >= 0 && left.Remove(s.At)) s.Want = s.At;
                else s.Want = -2;
            foreach (Slot s in slots)
            {
                if (s.Want != -2) continue;
                if (left.Count > 0) { s.Want = left[0]; left.RemoveAt(0); }
                else s.Want = -1;
            }
        }

        private delegate bool Place(Slot s);

        /// <summary>
        /// Fades a light out from where it is, moves it, and fades it in at its new place. <paramref name="full"/> is its
        /// Linear intensity when <paramref name="linear"/>, otherwise its Gamma one, matched where it mostly lights things.
        /// </summary>
        private static void Follow(Slot s, float step, float full, Place place, bool linear)
        {
            if (s.Want != s.At)
            {
                s.Level = Mathf.Max(0f, s.Level - step);
                if (s.Level <= 0f) s.At = s.Want;
            }
            else if (s.At >= 0) s.Level = Mathf.Min(1f, s.Level + step);
            bool lit = s.At >= 0 && s.Level > 0f && full > 0f && place(s);
            if (!lit)
            {
                if (s.Light.enabled) s.Light.enabled = false;
                return;
            }
            float intensity = linear ? full : LightFalloff.UrpLinear(s.Light.color, full, s.Light.range, s.Lights);
            s.Light.intensity = s.Level * s.Scale * intensity;
            if (!s.Light.enabled) s.Light.enabled = true;
        }

        private bool PlaceFlood(Slot s)
        {
            if (s.At >= Layout.Lights.Count) return false;
            Floodlight f = Layout.Lights[s.At];
            Transform t = s.Light.transform;
            t.SetPositionAndRotation(f.Position, Quaternion.LookRotation(f.Aim));
            s.Light.range = f.Reach * 1.6f + 10f;
            s.Lights = f.Reach;
            s.Light.spotAngle = f.Cone;
            s.Light.innerSpotAngle = f.Cone * 0.6f;
            return true;
        }

        private bool PlaceHead(Slot s)
        {
            if (s.At >= CarLamps.All.Count) return false;
            CarLamps car = CarLamps.All[s.At];
            Transform c = car.transform;
            Vector3 at = c.TransformPoint(new Vector3(car.Nose.x, car.Nose.y, car.Front + HeadAhead));
            Quaternion aim = c.rotation * Quaternion.Euler(4f, 0f, 0f);
            s.Light.transform.SetPositionAndRotation(at, aim);
            s.Light.range = HeadRange;
            s.Lights = HeadLights;
            s.Light.spotAngle = HeadCone;
            s.Light.innerSpotAngle = HeadCone * 0.5f;
            float near = Nearest(car, at, aim * Vector3.forward);
            s.Scale = Mathf.Min(1f, HeadNearest * LightFalloff.At(HeadLights, HeadRange) / LightFalloff.At(near, HeadRange));
            return true;
        }

        /// <summary>
        /// How near the closest other car in front of a headlamp light is, m, to the nearest point of its body: those within
        /// the cone and a margin around it, for a car alongside catches the cone's soft edge.
        /// </summary>
        private static float Nearest(CarLamps own, Vector3 at, Vector3 forward)
        {
            float nearest = HeadRange, edge = Mathf.Cos((HeadCone * 0.5f + 25f) * Mathf.Deg2Rad);
            foreach (CarLamps other in CarLamps.All)
            {
                if (other == own) continue;
                Transform t = other.transform;
                Vector3 axis = t.forward * CarHalfLength, from = t.position - axis;
                // The nearest point on the car's middle line, then out to its side.
                float k = Mathf.Clamp01(Vector3.Dot(at - from, axis) / (2f * CarHalfLength * CarHalfLength));
                Vector3 v = from + axis * (2f * k) + Vector3.up * 0.6f - at;
                float d = v.magnitude;
                if (d >= nearest + CarHalfWidth || Vector3.Dot(v, forward) < edge * d) continue;
                nearest = Mathf.Min(nearest, Mathf.Max(d - CarHalfWidth, 0.5f));
            }
            return nearest;
        }
    }
}
