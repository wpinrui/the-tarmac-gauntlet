using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.TeamHq
{
    /// <summary>
    /// Builds the team's home workshop, lights it (<see cref="HqLightingView"/>) and sets up the menu camera.
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
            var lit = new List<MeshRenderer>();
            foreach (KeyValuePair<string, HqPart> part in geometry.Parts)
            {
                MeshRenderer r = AddPart(building, part.Key, part.Value);
                if (part.Value.Uv2s.Count > 0) lit.Add(r);
            }
            var wear = new GameObject("Wear").transform;
            wear.SetParent(transform, false);
            foreach (KeyValuePair<string, HqPart> part in geometry.Wear)
                AddPart(wear, part.Key, part.Value).shadowCastingMode = ShadowCastingMode.Off;
            var props = new GameObject("Props").transform;
            props.SetParent(transform, false);
            foreach (HqProp prop in geometry.Props) HqProps.Place(props, prop);
            HqCars cars = gameObject.AddComponent<HqCars>();
            cars.Init(geometry.Bays, DemoCars);
            var crew = new GameObject("Crew");
            crew.transform.SetParent(transform, false);
            crew.AddComponent<HqCrew>().HasCar = cars.Parked;
            gameObject.AddComponent<HqLightingView>().Init(geometry, lit, wear.gameObject);

            var camObject = new GameObject("Menu Camera") { tag = "MainCamera" };
            Camera c = camObject.AddComponent<Camera>();
            c.nearClipPlane = 0.05f;
            c.farClipPlane = 200;
            c.backgroundColor = new Color(0.1f, 0.12f, 0.11f);
            camObject.AddComponent<AudioListener>();
            camObject.AddComponent<HqCamera>().Cut(HqCamera.Screens[0]);
        }

        private static MeshRenderer AddPart(Transform parent, string key, HqPart part)
        {
            var mesh = new Mesh { name = key, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(part.Vertices);
            mesh.SetNormals(part.Normals);
            mesh.SetUVs(0, part.Uvs);
            if (part.Uv2s.Count > 0) mesh.SetUVs(1, part.Uv2s);
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
            return r;
        }
    }
}
