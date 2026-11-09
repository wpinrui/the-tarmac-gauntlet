using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>Builds the circuit's meshes: ground, road, pit lane, start line and a few landmarks.</summary>
    public static class TrackDressing
    {
        private static readonly Color Grass = new Color(0.30f, 0.46f, 0.24f);
        private static readonly Color Asphalt = new Color(0.20f, 0.20f, 0.22f);
        private static readonly Color PitAsphalt = new Color(0.26f, 0.26f, 0.28f);
        private static readonly Color Kerb = new Color(0.85f, 0.85f, 0.85f);
        private static readonly Color Concrete = new Color(0.62f, 0.62f, 0.60f);

        public static GameObject Build(TrackLayout track, Materials materials)
        {
            var root = new GameObject("Track");

            Bounds bounds = new Bounds(track.Main.Points[0], Vector3.zero);
            foreach (Vector3 p in track.Main.Points) bounds.Encapsulate(p);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(root.transform);
            ground.transform.position = new Vector3(bounds.center.x, -0.05f, bounds.center.z);
            float size = Mathf.Max(bounds.size.x, bounds.size.z) * 3f / 10f; // Plane is 10 m wide
            ground.transform.localScale = new Vector3(size, 1, size);
            ground.GetComponent<Renderer>().sharedMaterial = materials.Get(Grass);

            Ribbon(root, "Kerbs", track.Main, TrackLayout.RoadWidth + 1.6f, -0.01f, materials.Get(Kerb));
            Ribbon(root, "Road", track.Main, TrackLayout.RoadWidth, 0f, materials.Get(Asphalt));
            if (track.PitLane != null)
            {
                Ribbon(root, "Pit lane", track.PitLane, TrackLayout.PitLaneWidth, 0.005f, materials.Get(PitAsphalt));
                Garages(root, track, materials);
            }

            // Start/finish line.
            track.Main.Sample(0, out Vector3 sf, out Vector3 fwd);
            GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
            line.name = "Start line";
            line.transform.SetParent(root.transform);
            line.transform.SetPositionAndRotation(sf + Vector3.up * 0.01f, Quaternion.LookRotation(fwd));
            line.transform.localScale = new Vector3(TrackLayout.RoadWidth, 0.02f, 1.2f);
            line.GetComponent<Renderer>().sharedMaterial = materials.Get(Color.white);

            Grandstand(root, track, materials);
            return root;
        }

        /// <summary>A flat strip following a line, lying on the ground.</summary>
        private static void Ribbon(GameObject root, string name, Polyline line, float width, float height, Material material)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            int n = line.Points.Count;
            int count = line.Closed ? n + 1 : n;
            float d = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = line.Points[i % n];
                Vector3 prev = line.Points[(i - 1 + n) % n];
                Vector3 next = line.Points[(i + 1) % n];
                if (!line.Closed && i == 0) prev = p;
                if (!line.Closed && i == n - 1) next = p;
                Vector3 dir = (next - prev).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
                if (i > 0) d += Vector3.Distance(p, line.Points[(i - 1) % n]);
                verts.Add(p + right * (width / 2) + Vector3.up * height);
                verts.Add(p - right * (width / 2) + Vector3.up * height);
                uvs.Add(new Vector2(0, d / width));
                uvs.Add(new Vector2(1, d / width));
                if (i > 0)
                {
                    int a = (i - 1) * 2;
                    tris.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
                }
            }

            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(root.transform);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static void Garages(GameObject root, TrackLayout track, Materials materials)
        {
            // A row of pit garages on the far side of the pit lane, either side of the pit box.
            for (float d = track.PitBox - 120f; d <= track.PitBox + 120f; d += 12f)
            {
                Vector3 pos = track.PitLane.Offset(d, track.PitSide * (TrackLayout.PitLaneWidth / 2 + 6f), out Vector3 fwd);
                Box(root, "Garage", pos + Vector3.up * 2.5f, fwd, new Vector3(10f, 5f, 11.5f), materials.Get(Concrete));
            }
        }

        private static void Grandstand(GameObject root, TrackLayout track, Materials materials)
        {
            // Grandstand on the outside of the main straight, facing the pit wall.
            float side = -track.PitSide * (TrackLayout.RoadWidth / 2 + 14f);
            for (float d = -90f; d <= 90f; d += 30f)
            {
                Vector3 pos = track.Main.Offset(d, side, out Vector3 fwd);
                Box(root, "Grandstand", pos + Vector3.up * 4f, fwd, new Vector3(14f, 8f, 29f), materials.Get(Concrete));
            }
        }

        private static void Box(GameObject root, string name, Vector3 pos, Vector3 forward, Vector3 size, Material mat)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(root.transform);
            box.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(forward));
            box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }

    /// <summary>Flat-colour materials made from the default material, so any render pipeline works.</summary>
    public sealed class Materials
    {
        private readonly Material template;
        private readonly Dictionary<Color, Material> cache = new Dictionary<Color, Material>();

        public Materials()
        {
            GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            template = probe.GetComponent<Renderer>().sharedMaterial;
            Object.Destroy(probe);
        }

        public Material Get(Color color)
        {
            if (!cache.TryGetValue(color, out Material m))
            {
                m = new Material(template) { color = color };
                cache[color] = m;
            }
            return m;
        }
    }
}
