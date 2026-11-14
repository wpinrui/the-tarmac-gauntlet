using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.Visuals;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The floodlights' real lights: one URP spot light for each of <see cref="FloodlightLayout.Sources"/>, at the intensity
    /// the layout balanced it to, all dimmed together by <see cref="Level"/>. Forward+ lights every surface with every
    /// light near it, so the whole lit area takes their light the same way. The lights near the camera are on, the nearest
    /// first up to <see cref="MostOn"/>; with light shadows on, the few nearest the camera's view cast soft shadows.
    /// The lenses glow with the lights, on the shared lens material.
    /// </summary>
    public sealed class FloodlightRig : MonoBehaviour
    {
        public FloodlightLayout Layout;
        /// <summary>Galvanised steel for the masts and brackets, and the lenses, which glow with the lights; made on Awake, so they are there as soon as the rig is added.</summary>
        [System.NonSerialized] public Material Steel, Lens;

        /// <summary>How much of their light the floodlights throw now, 0 to 1 (<see cref="NightLighting"/>).</summary>
        [System.NonSerialized] public float Level;
        /// <summary>How far the lenses glow now, 0 to 1.</summary>
        [System.NonSerialized] public float Glow;

        /// <summary>Lights farther from the camera than this are off, m, and at most this many are on.</summary>
        private const float Reach = 600f;
        private const int MostOn = 200;
        /// <summary>With light shadows on, this many lights nearest the camera's view cast shadows; the view is this far ahead, m.</summary>
        private const int Shadowed = 4;
        private const float FocusAhead = 30f;
        /// <summary>Real seconds between looks at where the camera is.</summary>
        private const float Check = 0.25f;

        private Light[] lights;
        private readonly List<int> order = new List<int>();
        private readonly HashSet<int> lit = new HashSet<int>(), shaded = new HashSet<int>();
        private float nextCheck, shownLevel = -1f, shownGlow = -1f;
        private bool shadows;
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private static Material Make(string name, Color colour, float smoothness, float metallic)
        {
            var m = new Material(Lit.Shader) { name = name, enableInstancing = true, color = colour };
            m.SetFloat(Lit.SmoothnessId, smoothness);
            m.SetFloat(Lit.MetallicId, metallic);
            return m;
        }

        private void Awake()
        {
            Steel = Make("Floodlight steel", new Color(0.55f, 0.57f, 0.58f), 0.4f, 0.5f);
            Lens = Make("Floodlight lenses", new Color(0.55f, 0.57f, 0.56f), 0.9f, 0f);
            Lens.EnableKeyword("_EMISSION");
            Lens.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            Lens.SetColor(EmissionId, Color.black);
        }

        private void OnDestroy()
        {
            Destroy(Steel);
            Destroy(Lens);
        }

        private void Start()
        {
            shadows = GraphicsOptions.LightShadows.On;
            lights = new Light[Layout.Sources.Count];
            for (int i = 0; i < lights.Length; i++)
            {
                FloodSource s = Layout.Sources[i];
                var go = new GameObject("Floodlight " + (i + 1));
                go.transform.SetParent(transform, false);
                go.transform.SetPositionAndRotation(s.Position, Quaternion.LookRotation(s.Aim, Mathf.Abs(s.Aim.y) > 0.99f ? Vector3.forward : Vector3.up));
                Light l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.color = NightBudget.Flood;
                l.range = s.Range;
                l.spotAngle = s.Cone;
                l.innerSpotAngle = s.Core;
                l.shadows = LightShadows.None;
                l.intensity = 0f;
                l.enabled = false;
                lights[i] = l;
            }
        }

        private void LateUpdate()
        {
            if (lights == null) return;
            if (Time.unscaledTime >= nextCheck || (Level > 0f) != (shownLevel > 0f))
            {
                nextCheck = Time.unscaledTime + Check;
                Pick();
            }
            if (!Mathf.Approximately(Level, shownLevel))
            {
                shownLevel = Level;
                for (int i = 0; i < lights.Length; i++) lights[i].intensity = Layout.Sources[i].Intensity * Level / NightBudget.Luma(NightBudget.Flood);
            }
            if (Mathf.Abs(Glow - shownGlow) > 0.002f)
            {
                shownGlow = Glow;
                Lens.SetColor(EmissionId, NightBudget.Glow(NightBudget.Flood, NightBudget.FloodLens * Glow).gamma);
            }
        }

        /// <summary>Turns on the lights near the camera and gives the nearest few shadows.</summary>
        private void Pick()
        {
            Camera cam = Camera.main;
            lit.Clear();
            shaded.Clear();
            if (cam != null && Level > 0f)
            {
                Vector3 eye = cam.transform.position;
                order.Clear();
                for (int i = 0; i < lights.Length; i++)
                    if ((Layout.Sources[i].Target - eye).sqrMagnitude < Reach * Reach) order.Add(i);
                order.Sort((a, b) => (Layout.Sources[a].Target - eye).sqrMagnitude.CompareTo((Layout.Sources[b].Target - eye).sqrMagnitude));
                for (int k = 0; k < order.Count && k < MostOn; k++) lit.Add(order[k]);
                if (shadows)
                {
                    Vector3 focus = eye + cam.transform.forward * FocusAhead;
                    order.Sort((a, b) => (Layout.Sources[a].Target - focus).sqrMagnitude.CompareTo((Layout.Sources[b].Target - focus).sqrMagnitude));
                    for (int k = 0; k < order.Count && k < Shadowed; k++) shaded.Add(order[k]);
                }
            }
            for (int i = 0; i < lights.Length; i++)
            {
                bool on = lit.Contains(i);
                if (lights[i].enabled != on) lights[i].enabled = on;
                LightShadows want = shaded.Contains(i) ? LightShadows.Soft : LightShadows.None;
                if (lights[i].shadows != want) lights[i].shadows = want;
            }
        }
    }
}
