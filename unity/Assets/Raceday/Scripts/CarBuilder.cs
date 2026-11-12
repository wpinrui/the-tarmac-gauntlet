using TarmacGauntlet.Rendering;
using TarmacGauntlet.Simulation;
using TarmacGauntlet.Visuals;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>Makes a drivable car: the model, wheel pivots, the dark on the ground under it, collider and physics.</summary>
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
        /// plain box in the given paint. With no keys it can only be driven by the AI. A team with a
        /// livery for this model wears it, with its race number, instead of the paint.
        /// </summary>
        public static CarController Spawn(string modelId, Circuit circuit, KeyboardState keys, Color? paint = null,
            string team = null, int raceNumber = 0)
        {
            CarSpec spec = CarSpec.For(modelId);
            var root = new GameObject(spec.Name);
            root.layer = IgnoreRaycastLayer;

            string modelPath = CarArt.ModelPath(modelId);
            GameObject model = string.IsNullOrEmpty(modelPath) ? null : LoadModel(modelPath);
            Transform[] wheels = model != null ? AttachModel(root, model, modelPath, paint, team, raceNumber) : BuildBox(root, spec, paint ?? new Color(0.8f, 0.81f, 0.83f));
            CarGround.Fit(root, wheels);

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
            WheelBlur.Fit(car, wheels);
            CarLamps.Fit(car, model != null ? root.transform.Find("Model")?.gameObject : null);
            root.AddComponent<EngineAudio>().Setup(modelId, ++spawned);
            TyreAudio.Fit(car, spawned);
            root.AddComponent<ImpactAudio>();
            return car;
        }

        private static GameObject LoadModel(string path) => ArtLibrary.Load<GameObject>(path);

        /// <summary>
        /// Puts the model under the car and gives each wheel a pivot at its centre, so it can
        /// steer, spin and move with the suspension, and each opening door its hinge (CarDoors on
        /// the model). The team's livery goes on the body if it has one for this model; otherwise,
        /// with a paint, the body is resprayed in it. The glass, lenses and trims get their finish (CarMaterials).
        /// Returns pivots front left, front right, rear left, rear right.
        /// </summary>
        private static Transform[] AttachModel(GameObject root, GameObject prefab, string modelPath, Color? paint, string team, int raceNumber)
        {
            GameObject model = Object.Instantiate(prefab, root.transform, false);
            model.name = "Model";
            string livery = string.IsNullOrEmpty(team) ? null : CarArt.LiveryPath(team);
            bool painted = livery != null && CarLivery.Apply(model, modelPath, livery, raceNumber);
            // With no paint the model keeps its own colour, in the car paint all the same.
            Color? spray = painted ? null : paint ?? CarPaint.ColourOf(model);
            if (spray.HasValue) CarPaint.Respray(model, spray.Value);
            CarMaterials.Dress(model);
            foreach (Collider c in model.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            foreach (Transform t in model.GetComponentsInChildren<Transform>()) t.gameObject.layer = IgnoreRaycastLayer;
            CarDoors.Attach(model, modelPath);

            var pivots = new Transform[4];
            Transform[] parts = model.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < 4; i++)
            {
                Transform tyre = Find(parts, "wheel_tyre_" + Corners[i]);
                if (tyre == null) continue;
                Bounds b = tyre.GetComponent<Renderer>().bounds;
                var pivot = new GameObject("Wheel " + Corners[i]).transform;
                pivot.SetParent(model.transform, false);
                pivot.position = b.center;
                pivot.gameObject.layer = IgnoreRaycastLayer;
                // Every part of the wheel turns with it: tyre, rim, and any cap or disc the model adds.
                foreach (Transform t in parts)
                    if (IsWheelPart(t.name, Corners[i])) t.SetParent(pivot, true);
                pivots[i] = pivot;
            }
            return pivots;
        }

        private static readonly string[] Corners = { "fl", "fr", "rl", "rr" };

        /// <summary>A model part named wheel_&lt;part&gt;_&lt;corner&gt;, with or without a suffix after the corner.</summary>
        internal static bool IsWheelPart(string name, string corner)
        {
            if (!name.StartsWith("wheel_")) return false;
            string[] words = name.Split('_');
            for (int i = 2; i < words.Length; i++)
                if (words[i] == corner) return true;
            return false;
        }

        private static Transform Find(Transform[] parts, string name)
        {
            foreach (Transform t in parts)
                if (t.name == name || t.name.StartsWith(name + "_")) return t;
            return null;
        }

        /// <summary>A plain stand-in when the model is not available.</summary>
        private static Transform[] BuildBox(GameObject root, CarSpec spec, Color colour)
        {
            var paint = new Material(Lit.Shader) { color = colour };
            var rubber = new Material(Lit.Shader) { color = new Color(0.08f, 0.08f, 0.08f) };
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
