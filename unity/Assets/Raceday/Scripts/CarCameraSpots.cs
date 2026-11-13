using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Where a camera sits on each car model, measured from its meshes once per model. The bonnet camera sits on the
    /// centreline just ahead of the windscreen's base, a little above the bonnet, so it looks over the bonnet and never
    /// through the glass. A car with no windscreen, such as a single seater, has it a metre behind the front axle, above
    /// the bodywork there.
    /// </summary>
    public static class CarCameraSpots
    {
        /// <summary>How far ahead of the windscreen's base and above the bonnet the camera sits, m.</summary>
        private const float Ahead = 0.2f, Above = 0.15f;
        /// <summary>The strip either side of the centreline that is measured, m.</summary>
        private const float Strip = 0.3f;
        /// <summary>Where it went before cars were measured, and for a car that can't be.</summary>
        private static readonly Vector3 Fallback = new Vector3(0f, 1.2f, 0.3f);

        private static readonly Dictionary<string, Vector3> bonnets = new Dictionary<string, Vector3>();

        /// <summary>The bonnet camera's place in the car's own frame.</summary>
        public static Vector3 Bonnet(Transform car)
        {
            if (bonnets.TryGetValue(car.name, out Vector3 spot)) return spot;
            spot = Measure(car) ?? Fallback;
            bonnets[car.name] = spot;
            return spot;
        }

        private static Vector3? Measure(Transform car)
        {
            Transform model = car.Find("Model");
            if (model == null) return null;
            var glass = new List<Vector3>();
            var body = new List<Vector3>();
            Matrix4x4 toCar = car.worldToLocalMatrix;
            foreach (MeshRenderer r in model.GetComponentsInChildren<MeshRenderer>())
            {
                // Wheels turn and doors swing, so neither says where the bonnet is.
                Transform part = r.transform;
                if (part.name.StartsWith("wheel_") || part.name.StartsWith("door")) continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                Mesh mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null || !mesh.isReadable) continue;
                Vector3[] vertices = mesh.vertices;
                Material[] mats = r.sharedMaterials;
                Matrix4x4 into = toCar * part.localToWorldMatrix;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    Material m = mats.Length > 0 ? mats[Mathf.Min(sub, mats.Length - 1)] : null;
                    // Named as in the model, with " (finished)" once dressed (CarMaterials).
                    string look = m != null ? m.name.Replace(" (Instance)", "").Replace(" (finished)", "") : "";
                    List<Vector3> to = look == "glass" ? glass : look.Contains("glass") ? null : body;
                    if (to == null) continue;
                    foreach (int i in mesh.GetTriangles(sub))
                    {
                        Vector3 p = into.MultiplyPoint3x4(vertices[i]);
                        if (Mathf.Abs(p.x) < Strip) to.Add(p);
                    }
                }
            }
            if (body.Count == 0) return null;

            if (glass.Count > 0)
            {
                // The windscreen leans back, so its front edge is its base.
                float zBase = float.MinValue;
                foreach (Vector3 p in glass) zBase = Mathf.Max(zBase, p.z);
                float yBase = float.MaxValue;
                foreach (Vector3 p in glass) if (p.z > zBase - 0.05f) yBase = Mathf.Min(yBase, p.y);
                float top = Top(body, zBase + 0.05f, zBase + 0.4f, yBase);
                return new Vector3(0f, Mathf.Max(yBase, top) + Above, zBase + Ahead);
            }

            Transform fl = car.Find("Model/Wheel fl"), fr = car.Find("Model/Wheel fr");
            if (fl == null || fr == null) return null;
            float z = (car.InverseTransformPoint(fl.position).z + car.InverseTransformPoint(fr.position).z) * 0.5f - 1f;
            return new Vector3(0f, Top(body, z - 0.1f, z + 0.1f, Fallback.y - Above) + Above, z);
        }

        /// <summary>The highest bodywork on the centreline between two distances along the car, or the given height if there is none.</summary>
        private static float Top(List<Vector3> body, float from, float to, float none)
        {
            float top = float.MinValue;
            foreach (Vector3 p in body) if (p.z > from && p.z < to) top = Mathf.Max(top, p.y);
            return top > float.MinValue ? top : none;
        }
    }
}
