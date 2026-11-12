using System;
using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.Visuals;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.TeamHq
{
    /// <summary>
    /// Lights the building. The sun is always live, for the cars' and the crew's shadows. With Baked lighting on, the
    /// lightmap from tools/team-hq/bake (Resources/Lighting) carries the lamps, the daylight through the windows and every
    /// bounce, and whatever moves takes the room's ambient light from the same bake; off, live lamps and a flat ambient
    /// light the room. Baked reflections swaps the live reflection probe for one baked probe per room. Wear shows the
    /// grime laid over the floor and walls. Each follows its graphics option live.
    /// </summary>
    public sealed class HqLightingView : MonoBehaviour
    {
        // Filled by JsonUtility.
#pragma warning disable 0649
        [Serializable]
        private sealed class Bake
        {
            public string signature;
            public Ambient ambient;
        }

        [Serializable]
        private sealed class Ambient
        {
            public float[] sky, equator, ground;
        }
#pragma warning restore 0649

        private static readonly Color FlatSky = new Color(0.62f, 0.65f, 0.68f), FlatEquator = new Color(0.5f, 0.51f, 0.5f), FlatGround = new Color(0.3f, 0.31f, 0.29f);

        private readonly List<MeshRenderer> lit = new List<MeshRenderer>();
        private readonly List<GameObject> lamps = new List<GameObject>();
        private readonly List<ReflectionProbe> bakedProbes = new List<ReflectionProbe>();
        private GameObject wear;
        private Bake bake;
        private Texture2D lightmap;
        private ReflectionProbe liveProbe;

        public void Init(HqGeometry geometry, List<MeshRenderer> litRenderers, GameObject wearRoot)
        {
            lit.AddRange(litRenderers);
            wear = wearRoot;
            AddSun();
            foreach (HqLight l in geometry.Lights) lamps.Add(AddLamp(l));
            AddProbes();

            var file = Resources.Load<TextAsset>("Lighting/bake");
            lightmap = Resources.Load<Texture2D>("Lighting/hq_lightmap");
            if (file != null) bake = JsonUtility.FromJson<Bake>(file.text);
            if (bake != null && bake.signature != geometry.Lightmap.Signature)
            {
                Debug.LogWarning("Team HQ: the baked lighting is for an older building, so the room is lit live. Run tools/team-hq/bake/run.sh.");
                bake = null;
            }

            GraphicsOptions.BakedLighting.Changed += OnOption;
            GraphicsOptions.BakedReflections.Changed += OnOption;
            GraphicsOptions.Wear.Changed += OnOption;
            Apply();
        }

        private void OnDestroy()
        {
            GraphicsOptions.BakedLighting.Changed -= OnOption;
            GraphicsOptions.BakedReflections.Changed -= OnOption;
            GraphicsOptions.Wear.Changed -= OnOption;
        }

        private void OnOption(GraphicsOption _) => Apply();

        private void Apply()
        {
            bool baked = GraphicsOptions.BakedLighting.On && bake != null && lightmap != null;
            LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;
            LightmapSettings.lightmaps = baked ? new[] { new LightmapData { lightmapColor = lightmap } } : new LightmapData[0];
            foreach (MeshRenderer r in lit)
            {
                r.lightmapIndex = baked ? 0 : -1;
                r.lightmapScaleOffset = new Vector4(1, 1, 0, 0);
            }
            foreach (GameObject l in lamps) l.SetActive(!baked);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = baked ? Gamma(bake.ambient.sky) : FlatSky;
            RenderSettings.ambientEquatorColor = baked ? Gamma(bake.ambient.equator) : FlatEquator;
            RenderSettings.ambientGroundColor = baked ? Gamma(bake.ambient.ground) : FlatGround;

            bool reflections = GraphicsOptions.BakedReflections.On && bakedProbes.Count == HqLighting.Probes.Length;
            foreach (ReflectionProbe p in bakedProbes) p.enabled = reflections;
            liveProbe.enabled = !reflections;
            // The live probe draws the room as it is lit now.
            if (!reflections) liveProbe.RenderProbe();

            wear.SetActive(GraphicsOptions.Wear.On);
        }

        /// <summary>The bake writes linear light; the ambient colours are set as sRGB.</summary>
        private static Color Gamma(float[] c) => new Color(c[0], c[1], c[2]).gamma;

        private static Color ToColor(Vector3 c) => new Color(c.x, c.y, c.z);

        private void AddSun()
        {
            var sunObject = new GameObject("Sun");
            sunObject.transform.SetParent(transform, false);
            sunObject.transform.rotation = Quaternion.LookRotation(HqLighting.SunDirection, Vector3.up);
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = ToColor(HqLighting.SunColour);
            sun.intensity = HqLighting.SunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.shadowNormalBias = 0.2f;
        }

        private GameObject AddLamp(HqLight l)
        {
            var go = new GameObject("Lamp light");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = l.Position;
            Light p = go.AddComponent<Light>();
            p.type = LightType.Point;
            p.range = l.Range;
            p.intensity = LightFalloff.Urp(l.Intensity, l.Range);
            p.color = ToColor(l.Warm ? HqLighting.WarmLamp : HqLighting.WhiteLamp);
            p.shadows = LightShadows.None;
            return go;
        }

        /// <summary>Reflections of the room itself, for the epoxy floor, glass and chrome.</summary>
        private void AddProbes()
        {
            var live = new GameObject("Reflection probe");
            live.transform.SetParent(transform, false);
            live.transform.localPosition = new Vector3(0, 2.5f, -2);
            liveProbe = live.AddComponent<ReflectionProbe>();
            liveProbe.mode = ReflectionProbeMode.Realtime;
            liveProbe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            liveProbe.size = new Vector3(HqGeometry.W, HqGeometry.H * 2, HqGeometry.D);
            liveProbe.boxProjection = true;

            foreach (HqProbe p in HqLighting.Probes)
            {
                var cube = Resources.Load<Cubemap>("Lighting/probe_" + p.Name);
                if (cube == null) continue;
                var go = new GameObject("Reflection probe " + p.Name);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = p.Capture;
                ReflectionProbe probe = go.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Custom;
                probe.customBakedTexture = cube;
                probe.center = p.Centre - p.Capture;
                probe.size = p.Size;
                probe.boxProjection = true;
                probe.importance = p.Importance;
                probe.blendDistance = 0.5f;
                bakedProbes.Add(probe);
            }
        }
    }
}
