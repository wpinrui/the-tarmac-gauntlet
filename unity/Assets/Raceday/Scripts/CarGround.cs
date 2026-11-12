using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The soft dark on the ground under a car, so it sits on the road instead of looking pasted on: one soft rounded
    /// rectangle the size of the car's footprint and a smaller, darker one under each tyre, on a mesh that moves with the
    /// car just above the ground (CarGround.shader). Every quality level draws it.
    /// </summary>
    public static class CarGround
    {
        /// <summary>Height of the mesh above the ground under the car at rest, m: above the road when the car rolls or dips.</summary>
        private const float Lift = 0.05f;
        /// <summary>How far the dark spreads past the footprint, m, and how dark its core is, 0 to 1.</summary>
        private const float BodyEdge = 0.45f, BodyDark = 0.55f;
        /// <summary>The dark under a tyre: its core's half size across and along, m, its soft edge, m, and how dark it is.</summary>
        private const float TyreHalfWidth = 0.1f, TyreHalfLength = 0.12f, TyreEdge = 0.18f, TyreDark = 0.5f;

        private static Material material;

        /// <summary>
        /// Puts the dark under a car just made at the origin, unturned: under the bounds of its renderers and its wheels'
        /// pivots (front left, front right, rear left, rear right; any may be missing).
        /// </summary>
        public static void Fit(GameObject car, Transform[] wheels)
        {
            if (material == null)
            {
                Shader shader = Resources.Load<Shader>("CarGround/CarGround");
                if (shader == null)
                {
                    Debug.LogError("Raceday: Resources/CarGround/CarGround is missing.");
                    return;
                }
                material = new Material(shader) { name = "Car ground" };
            }

            Bounds body = Footprint(car);
            var vertices = new List<Vector3>();
            var shapes = new List<Vector4>();
            var edges = new List<Vector2>();
            var triangles = new List<int>();
            Vector2 core = new Vector2(Mathf.Max(0.2f, body.extents.x - 0.1f), Mathf.Max(0.4f, body.extents.z - 0.15f));
            Quad(new Vector2(body.center.x, body.center.z), core, BodyEdge, BodyDark, vertices, shapes, edges, triangles);
            if (wheels != null)
                foreach (Transform w in wheels)
                    if (w != null)
                    {
                        Vector3 at = car.transform.InverseTransformPoint(w.position);
                        Quad(new Vector2(at.x, at.z), new Vector2(TyreHalfWidth, TyreHalfLength), TyreEdge, TyreDark, vertices, shapes, edges, triangles);
                    }

            var mesh = new Mesh { name = "Car ground" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, shapes);
            mesh.SetUVs(1, edges);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            var go = new GameObject("Ground");
            go.layer = car.layer;
            go.transform.SetParent(car.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>The car's renderers' bounds in its own space, or the stand-in box's if it has none.</summary>
        private static Bounds Footprint(GameObject car)
        {
            var b = new Bounds(CarBuilder.BoxCentre, CarBuilder.BoxSize);
            bool any = false;
            foreach (Renderer r in car.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                Bounds world = r.bounds;
                if (world.size.sqrMagnitude <= 0f) continue;
                Bounds local = new Bounds(car.transform.InverseTransformPoint(world.center), world.size);
                if (any) b.Encapsulate(local);
                else b = local;
                any = true;
            }
            // Never wider or longer than a car can be, whatever else hangs off it.
            b.extents = new Vector3(Mathf.Min(b.extents.x, 1.1f), b.extents.y, Mathf.Min(b.extents.z, 2.7f));
            return b;
        }

        private static void Quad(Vector2 centre, Vector2 core, float edge, float dark, List<Vector3> vertices,
            List<Vector4> shapes, List<Vector2> edges, List<int> triangles)
        {
            int first = vertices.Count;
            Vector2 half = core + new Vector2(edge, edge);
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector2(i == 0 || i == 3 ? -half.x : half.x, i < 2 ? -half.y : half.y);
                vertices.Add(new Vector3(centre.x + corner.x, Lift, centre.y + corner.y));
                shapes.Add(new Vector4(corner.x, corner.y, core.x, core.y));
                edges.Add(new Vector2(edge, dark));
            }
            triangles.AddRange(new[] { first, first + 2, first + 1, first, first + 3, first + 2 });
        }
    }
}
