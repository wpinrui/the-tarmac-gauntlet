using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.TeamHq
{
    /// <summary>
    /// Builds the team's home workshop, lights it with fixed daylight and sets up the menu camera.
    /// Until the menus exist, Left and Right (or Up and Down) step through the menu shots.
    /// </summary>
    public sealed class HqScene : MonoBehaviour
    {
        /// <summary>Car models for the bays, left to right from the doors; an empty path stands in a painted box.</summary>
        private static readonly string[] BayModels =
        {
            "Assets/Art/Vehicles/LibertyCollier/LibertyCollier.obj",
            "Assets/Art/Vehicles/CheonmaCX24Si/CheonmaCX24Si.obj",
            "",
            "",
        };

        private HqCamera cam;
        private int screen;

        private void Awake()
        {
            var geometry = new HqGeometry();
            var building = new GameObject("Building").transform;
            building.SetParent(transform, false);
            foreach (KeyValuePair<string, HqPart> part in geometry.Parts) AddPart(building, part.Key, part.Value);
            for (int i = 0; i < geometry.Bays.Count; i++) AddCar(geometry.Bays[i], i < BayModels.Length ? BayModels[i] : "");
            AddLights(geometry);

            var camObject = new GameObject("Menu Camera") { tag = "MainCamera" };
            Camera c = camObject.AddComponent<Camera>();
            c.nearClipPlane = 0.05f;
            c.farClipPlane = 200;
            c.backgroundColor = new Color(0.1f, 0.12f, 0.11f);
            camObject.AddComponent<AudioListener>();
            cam = camObject.AddComponent<HqCamera>();
            cam.Cut(HqCamera.Screens[0]);
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

        private void AddCar(HqBay bay, string modelPath)
        {
            GameObject prefab = null;
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(modelPath)) prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
#endif
            // Cars face the bay doors.
            Quaternion facing = Quaternion.Euler(0, 180, 0);
            if (prefab != null)
            {
                GameObject car = Instantiate(prefab, bay.Position, facing, transform);
                car.name = "Car " + prefab.name;
                return;
            }
            var stand = new GameObject("Car stand-in");
            stand.transform.SetParent(transform, false);
            stand.transform.SetPositionAndRotation(bay.Position, facing);
            Material paint = HqMaterials.For(bay.Paint), tyre = HqMaterials.For("rubber"), glass = HqMaterials.For("dark");
            Block(stand.transform, PrimitiveType.Cube, new Vector3(0, 0.55f, 0), new Vector3(1.85f, 0.6f, 4.4f), paint);
            Block(stand.transform, PrimitiveType.Cube, new Vector3(0, 1.05f, 0.25f), new Vector3(1.55f, 0.48f, 2f), glass);
            foreach (Vector2 w in new[] { new Vector2(-0.84f, 1.4f), new Vector2(0.84f, 1.4f), new Vector2(-0.84f, -1.42f), new Vector2(0.84f, -1.42f) })
                Block(stand.transform, PrimitiveType.Cylinder, new Vector3(w.x, 0.33f, w.y), new Vector3(0.66f, 0.12f, 0.66f), tyre, Quaternion.Euler(0, 0, 90));
        }

        private static void Block(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material m, Quaternion? rot = null)
        {
            GameObject g = GameObject.CreatePrimitive(type);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = rot ?? Quaternion.identity;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
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

        // Keys come through IMGUI events, so this works with either of Unity's input systems.
        private void OnGUI()
        {
            Event ev = Event.current;
            if (ev.type != EventType.KeyDown) return;
            int step = ev.keyCode == KeyCode.RightArrow || ev.keyCode == KeyCode.DownArrow ? 1
                : ev.keyCode == KeyCode.LeftArrow || ev.keyCode == KeyCode.UpArrow ? -1 : 0;
            if (step == 0) return;
            screen = (screen + step + HqCamera.Screens.Length) % HqCamera.Screens.Length;
            cam.Show(HqCamera.Screens[screen]);
            ev.Use();
        }
    }
}
