using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>Makes a drivable car: the model, wheel pivots, collider and physics.</summary>
    public static class CarBuilder
    {
        private const int IgnoreRaycastLayer = 2;   // so the car's own suspension rays skip it
        /// <summary>Only the cars are on this layer, so a light can be told to light the cars alone.</summary>
        public const int CarLayer = IgnoreRaycastLayer;
        /// <summary>The stand-in box's shell, in the car's space.</summary>
        public static readonly Vector3 BoxCentre = new Vector3(0f, 0.65f, 0f), BoxSize = new Vector3(1.85f, 0.8f, 4.5f);
        private static int spawned;

        /// <summary>
        /// A car of the given model, drawn with its class's 3D model. If that cannot be loaded it is a
        /// plain box in the given paint. With no keys it can only be driven by the AI.
        /// </summary>
        public static CarController Spawn(string modelId, Circuit circuit, KeyboardState keys, Color? paint = null)
        {
            CarSpec spec = CarSpec.For(modelId);
            var root = new GameObject(spec.Name);
            root.layer = IgnoreRaycastLayer;

            string modelPath = CarArt.ModelPath(modelId);
            GameObject model = string.IsNullOrEmpty(modelPath) ? null : LoadModel(modelPath);
            Transform[] wheels = model != null ? AttachModel(root, model, paint) : BuildBox(root, spec, paint ?? new Color(0.8f, 0.81f, 0.83f));

            BoxCollider box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.62f, 0.06f);
            box.size = new Vector3(1.9f, 0.9f, 4.6f);
            box.sharedMaterial = new PhysicsMaterial("Car body")
            {
                dynamicFriction = 0.15f,
                staticFriction = 0.15f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
            };

            root.AddComponent<Rigidbody>();
            CarController car = root.AddComponent<CarController>();
            car.Keys = keys;
            car.Circuit = circuit;
            car.Setup(spec, wheels);
            CarLamps.Fit(car, model != null ? root.transform.Find("Model")?.gameObject : null);
            root.AddComponent<EngineAudio>().Setup(modelId, ++spawned);
            return car;
        }

        private static GameObject LoadModel(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
#else
            return null;
#endif
        }

        /// <summary>
        /// Puts the model under the car and gives each wheel a pivot at its centre, so it can
        /// steer, spin and move with the suspension. With a paint, the body is resprayed in it.
        /// Returns pivots front left, front right, rear left, rear right.
        /// </summary>
        private static Transform[] AttachModel(GameObject root, GameObject prefab, Color? paint)
        {
            GameObject model = Object.Instantiate(prefab, root.transform, false);
            model.name = "Model";
            if (paint.HasValue) CarPaint.Respray(model, paint.Value);
            foreach (Collider c in model.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            foreach (Transform t in model.GetComponentsInChildren<Transform>()) t.gameObject.layer = IgnoreRaycastLayer;

            string[] corners = { "fl", "fr", "rl", "rr" };
            var pivots = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                Transform tyre = Find(model.transform, "wheel_tyre_" + corners[i]);
                Transform rim = Find(model.transform, "wheel_rim_" + corners[i]);
                if (tyre == null) continue;
                Bounds b = tyre.GetComponent<Renderer>().bounds;
                var pivot = new GameObject("Wheel " + corners[i]).transform;
                pivot.SetParent(model.transform, false);
                pivot.position = b.center;
                pivot.gameObject.layer = IgnoreRaycastLayer;
                tyre.SetParent(pivot, true);
                if (rim != null) rim.SetParent(pivot, true);
                pivots[i] = pivot;
            }
            return pivots;
        }

        private static Transform Find(Transform parent, string name)
        {
            foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
                if (t.name == name || t.name.StartsWith(name + "_")) return t;
            return null;
        }

        /// <summary>A plain stand-in when the model is not available.</summary>
        private static Transform[] BuildBox(GameObject root, CarSpec spec, Color colour)
        {
            var paint = new Material(Shader.Find("Standard")) { color = colour };
            var rubber = new Material(Shader.Find("Standard")) { color = new Color(0.08f, 0.08f, 0.08f) };
            GameObject shell = Primitive(PrimitiveType.Cube, root.transform, paint);
            shell.transform.localPosition = BoxCentre;
            shell.transform.localScale = BoxSize;

            var pivots = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2, left = i % 2 == 0;
                var pivot = new GameObject("Wheel " + i).transform;
                pivot.SetParent(root.transform, false);
                pivot.localPosition = new Vector3(left ? -spec.HalfTrack : spec.HalfTrack, spec.WheelRadius, front ? spec.HalfWheelbase : -spec.HalfWheelbase);
                GameObject tyre = Primitive(PrimitiveType.Cylinder, pivot, rubber);
                tyre.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                tyre.transform.localScale = new Vector3(spec.WheelRadius * 2f, 0.15f, spec.WheelRadius * 2f);
                pivots[i] = pivot;
            }
            return pivots;
        }

        private static GameObject Primitive(PrimitiveType type, Transform parent, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.layer = IgnoreRaycastLayer;
            go.transform.SetParent(parent, false);
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }
    }
}
