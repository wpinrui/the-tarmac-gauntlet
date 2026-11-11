using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.TeamHq
{
    /// <summary>
    /// Builds the team's home workshop, lights it with fixed daylight and sets up the menu camera.
    /// </summary>
    public sealed class HqScene : MonoBehaviour
    {
        /// <summary>Cars in the bays while no game is loaded, left to right from the doors.</summary>
        private static readonly HqCarLook?[] DemoCars =
        {
            new HqCarLook { Key = "demo-0", AssetPath = "Assets/Art/Vehicles/LibertyCollier/LibertyCollier.obj" },
            new HqCarLook { Key = "demo-1", AssetPath = "Assets/Art/Vehicles/CheonmaCX24Si/CheonmaCX24Si.obj" },
            null,
            new HqCarLook { Key = "demo-3", AssetPath = "Assets/Art/Vehicles/LibertyCollier/LibertyCollier.obj" },
        };

        private void Awake()
        {
            var geometry = new HqGeometry();
            var building = new GameObject("Building").transform;
            building.SetParent(transform, false);
            foreach (KeyValuePair<string, HqPart> part in geometry.Parts) AddPart(building, part.Key, part.Value);
            var props = new GameObject("Props").transform;
            props.SetParent(transform, false);
            foreach (HqProp prop in geometry.Props) HqProps.Place(props, prop);
            gameObject.AddComponent<HqCars>().Init(geometry.Bays, DemoCars);
            AddLights(geometry);

            var camObject = new GameObject("Menu Camera") { tag = "MainCamera" };
            Camera c = camObject.AddComponent<Camera>();
            c.nearClipPlane = 0.05f;
            c.farClipPlane = 200;
            c.backgroundColor = new Color(0.1f, 0.12f, 0.11f);
            camObject.AddComponent<AudioListener>();
            camObject.AddComponent<HqCamera>().Cut(HqCamera.Screens[0]);
        }

        private static void AddPart(Transform parent, string key, HqPart part)
        {
            var mesh = new Mesh { name = key, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(part.Vertices);
            mesh.SetNormals(part.Normals);
            mesh.SetUVs(0, part.Uvs);
            mesh.SetTriangles(part.Triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            var go = new GameObject(key);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = HqMaterials.For(key);
            // Walls and planes are one-sided, so they cast from both faces to keep daylight to the windows.
            r.shadowCastingMode = HqMaterials.NoShadow.Contains(key) ? ShadowCastingMode.Off : ShadowCastingMode.TwoSided;
        }

        /// <summary>Fixed daylight through the high windows, a soft fill, and the warm lamps.</summary>
        private void AddLights(HqGeometry geometry)
        {
            var sunObject = new GameObject("Sun");
            sunObject.transform.SetParent(transform, false);
            // From high over the front windows, as in the viewer.
            sunObject.transform.rotation = Quaternion.LookRotation(new Vector3(6, -16, 24).normalized, Vector3.up);
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.89f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.shadowNormalBias = 0.2f;
            QualitySettings.shadowDistance = Mathf.Max(QualitySettings.shadowDistance, 60);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.65f, 0.68f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.51f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.31f, 0.29f);

            foreach (HqLight l in geometry.Lights)
            {
                var go = new GameObject("Lamp light");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = l.Position;
                Light p = go.AddComponent<Light>();
                p.type = LightType.Point;
                p.range = l.Range;
                p.intensity = l.Intensity;
                p.color = l.Warm ? new Color(1f, 0.93f, 0.82f) : Color.white;
                p.shadows = LightShadows.None;
            }

            // Reflections of the room itself, for the epoxy floor, glass and chrome.
            var probeObject = new GameObject("Reflection probe");
            probeObject.transform.SetParent(transform, false);
            probeObject.transform.localPosition = new Vector3(0, 2.5f, -2);
            ReflectionProbe probe = probeObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.size = new Vector3(HqGeometry.W, HqGeometry.H * 2, HqGeometry.D);
            probe.boxProjection = true;
            probe.RenderProbe();
        }
    }
}
