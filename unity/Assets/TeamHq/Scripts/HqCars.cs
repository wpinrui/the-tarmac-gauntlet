using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.TeamHq
{
    /// <summary>Side profiles for cars that are not modelled yet, from the rear (x 0) to the nose (x 1), as fractions of the height.</summary>
    public enum HqBodyShape { Hatch, Coupe, Sedan, Wedge, Proto }

    /// <summary>
    /// What stands in a bay. <see cref="Key"/> tells cars apart, so the same car is left where it is. A car with an
    /// asset path loads that model; any other is a plain grey body of the given shape and size.
    /// </summary>
    public struct HqCarLook
    {
        public string Key, AssetPath;
        public HqBodyShape Shape;
        public float Length, Width, Height, WheelRadius;
    }

    /// <summary>
    /// The cars in the garage bays. A changed bay drives its car out through the bay door and the new one in.
    /// </summary>
    public sealed class HqCars : MonoBehaviour
    {
        private const float OutSeconds = 0.42f, InSeconds = 0.65f;
        /// <summary>How far past the bay doors a car drives to leave, in metres from the back wall's side (Unity Z).</summary>
        private const float OutZ = -12.5f;

        private enum Phase { Idle, Out, In }

        private sealed class Bay
        {
            public Vector3 Home;
            public string Key;
            public GameObject Car;
            public bool HasNext;
            public HqCarLook? Next;
            public Phase Phase;
            public float Start;
        }

        private static readonly Dictionary<HqBodyShape, Vector2[]> Profiles = new Dictionary<HqBodyShape, Vector2[]>
        {
            { HqBodyShape.Hatch, P(0, .3f, 0, .68f, .07f, .9f, .45f, .98f, .68f, .66f, 1, .54f, 1, .3f) },
            { HqBodyShape.Coupe, P(0, .3f, 0, .62f, .16f, .78f, .36f, 1, .6f, 1, .8f, .6f, 1, .5f, 1, .3f) },
            { HqBodyShape.Sedan, P(0, .3f, 0, .64f, .12f, .72f, .3f, 1, .62f, 1, .8f, .66f, 1, .56f, 1, .3f) },
            { HqBodyShape.Wedge, P(0, .3f, 0, .66f, .22f, 1, .52f, 1, .84f, .56f, 1, .44f, 1, .3f) },
            { HqBodyShape.Proto, P(0, .3f, 0, .95f, .1f, .95f, .36f, 1, .58f, 1, .88f, .5f, 1, .4f, 1, .3f) },
        };

        private readonly List<Bay> bays = new List<Bay>();
        private HqCarLook?[] demo;
        private Material grey, rubber;

        public int Count => bays.Count;

        /// <summary>Whether a car stands still in a bay, so the crew can check its wheels.</summary>
        public bool Parked(int index) => index >= 0 && index < bays.Count && bays[index].Car != null && bays[index].Phase == Phase.Idle;

        /// <summary>Sets up the bays, with the cars that stand in them while no game is loaded.</summary>
        public void Init(IReadOnlyList<HqBay> places, HqCarLook?[] demoCars)
        {
            grey = HqMaterials.For("grey");
            rubber = HqMaterials.For("rubber");
            foreach (HqBay b in places) bays.Add(new Bay { Home = b.Position });
            demo = demoCars;
            ShowDemo();
        }

        /// <summary>The cars for the main menu, while no game is loaded.</summary>
        public void ShowDemo()
        {
            for (int i = 0; i < bays.Count; i++) Set(i, demo != null && i < demo.Length ? demo[i] : null, true);
        }

        /// <summary>Puts a car in a bay, or empties it with null. Instant skips the drive out and in.</summary>
        public void Set(int index, HqCarLook? look, bool instant = false)
        {
            if (index < 0 || index >= bays.Count) return;
            Bay b = bays[index];
            string key = look?.Key;
            string wanted = b.HasNext ? b.Next?.Key : b.Key;
            if (wanted == key && !instant) return;
            if (instant)
            {
                if (b.Key == key && !b.HasNext && b.Phase == Phase.Idle) return;
                b.Next = look;
                b.HasNext = true;
                SwapIn(b);
                if (b.Car != null) Place(b, b.Home.z);
                b.Phase = Phase.Idle;
                return;
            }
            b.Next = look;
            b.HasNext = true;
            if (b.Phase != Phase.Idle) return;
            b.Start = Time.unscaledTime;
            if (b.Car != null) b.Phase = Phase.Out;
            else
            {
                SwapIn(b);
                b.Phase = b.Car != null ? Phase.In : Phase.Idle;
            }
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            foreach (Bay b in bays)
            {
                if (b.Phase == Phase.Out)
                {
                    float t = Mathf.Clamp01((now - b.Start) / OutSeconds);
                    Place(b, Mathf.Lerp(b.Home.z, OutZ, t * t));
                    if (t < 1) continue;
                    SwapIn(b);
                    b.Start = now;
                    b.Phase = b.Car != null ? Phase.In : Phase.Idle;
                }
                else if (b.Phase == Phase.In)
                {
                    float t = Mathf.Clamp01((now - b.Start) / InSeconds), k = 1 - Mathf.Pow(1 - t, 3);
                    Place(b, Mathf.Lerp(OutZ, b.Home.z, k));
                    if (t < 1) continue;
                    b.Start = now;
                    b.Phase = b.HasNext ? Phase.Out : Phase.Idle;
                }
            }
        }

        private static void Place(Bay b, float z)
        {
            if (b.Car != null) b.Car.transform.position = new Vector3(b.Home.x, b.Home.y, z);
        }

        /// <summary>Replaces the bay's car with the next one, waiting at the doors.</summary>
        private void SwapIn(Bay b)
        {
            if (b.Car != null) Destroy(b.Car);
            HqCarLook? look = b.Next;
            b.Key = look?.Key;
            b.Next = null;
            b.HasNext = false;
            b.Car = look.HasValue ? Build(look.Value) : null;
            Place(b, OutZ);
        }

        private GameObject Build(HqCarLook look)
        {
            // Cars face the bay doors.
            Quaternion facing = Quaternion.Euler(0, 180, 0);
            GameObject prefab = null;
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(look.AssetPath)) prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(look.AssetPath);
#endif
            if (prefab != null)
            {
                GameObject car = Instantiate(prefab, transform);
                car.name = "Car " + prefab.name;
                car.transform.rotation = facing;
                return car;
            }
            return GreyCar(look, facing);
        }

        /// <summary>A plain grey body: the class's side profile pushed out to the car's width, on four tyres.</summary>
        private GameObject GreyCar(HqCarLook look, Quaternion facing)
        {
            var car = new GameObject("Car grey " + look.Key);
            car.transform.SetParent(transform, false);
            car.transform.rotation = facing;

            var body = new GameObject("Body");
            body.transform.SetParent(car.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = BodyMesh(Profiles[look.Shape], look.Length, look.Width - 0.12f, look.Height);
            body.AddComponent<MeshRenderer>().sharedMaterial = grey;

            float r = look.WheelRadius, axle = look.Length / 2 - 0.78f, side = look.Width / 2 - 0.08f;
            foreach (Vector2 w in new[] { new Vector2(-side, axle), new Vector2(side, axle), new Vector2(-side, -axle), new Vector2(side, -axle) })
            {
                GameObject tyre = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(tyre.GetComponent<Collider>());
                tyre.name = "Tyre";
                tyre.transform.SetParent(car.transform, false);
                tyre.transform.localPosition = new Vector3(w.x, r, w.y);
                tyre.transform.localRotation = Quaternion.Euler(0, 0, 90);
                tyre.transform.localScale = new Vector3(2 * r, 0.11f, 2 * r);
                tyre.GetComponent<Renderer>().sharedMaterial = rubber;
            }
            return car;
        }

        /// <summary>
        /// The side profile (x along the length, nose at +Z; y up) as a flat-shaded solid: two side faces and a strip
        /// of walls between them.
        /// </summary>
        private static Mesh BodyMesh(Vector2[] profile, float length, float width, float height)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Vector3 At(Vector2 p, float x) => new Vector3(x, p.y * height, (p.x - 0.5f) * length);
            // Every profile is a height over its length on a flat floor, so this point inside it sees every edge.
            var inside = new Vector2(0.5f, 0.32f);
            Vector3 centre = At(inside, 0);

            void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0) (b, c) = (c, b);
                int i = verts.Count;
                verts.Add(a);
                verts.Add(b);
                verts.Add(c);
                tris.Add(i);
                tris.Add(i + 1);
                tris.Add(i + 2);
            }

            float half = width / 2;
            int n = profile.Length;
            foreach (float x in new[] { -half, half })
            {
                var outward = new Vector3(Mathf.Sign(x), 0, 0);
                Vector3 c = At(inside, x);
                for (int i = 0; i < n; i++) Tri(c, At(profile[i], x), At(profile[(i + 1) % n], x), outward);
            }
            for (int i = 0; i < n; i++)
            {
                Vector3 a0 = At(profile[i], -half), a1 = At(profile[i], half), b0 = At(profile[(i + 1) % n], -half), b1 = At(profile[(i + 1) % n], half);
                Vector3 mid = (At(profile[i], 0) + At(profile[(i + 1) % n], 0)) / 2;
                Vector3 edge = b0 - a0;
                var normal = new Vector3(0, -edge.z, edge.y);
                if (Vector3.Dot(normal, mid - centre) < 0) normal = -normal;
                Tri(a0, a1, b1, normal);
                Tri(a0, b1, b0, normal);
            }
            var mesh = new Mesh { name = "grey body" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector2[] P(params float[] xy)
        {
            var points = new Vector2[xy.Length / 2];
            for (int i = 0; i < points.Length; i++) points[i] = new Vector2(xy[2 * i], xy[2 * i + 1]);
            return points;
        }
    }
}
