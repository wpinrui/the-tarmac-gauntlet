using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// A car's lamps: headlamps and tail lamps that glow at night, brake lamps that glow whenever the brakes are on, and
    /// where its headlamps' light comes from, for <see cref="HeadlampRig"/> to put a real light there at night. On a modelled car it lights the lamp
    /// parts the model names (lamp_head, lamp_tail, lamp_brake, see docs/car-modelling.md) with glowing copies of their own materials, so the
    /// model itself is never changed. A stand-in box gets plain lamps on its nose and tail. A car on physics brakes when
    /// its brake pedal is down; a gliding car when it slows hard.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public sealed class CarLights : MonoBehaviour
    {
        /// <summary>Every car's lamps in the scene.</summary>
        public static readonly List<CarLights> All = new List<CarLights>();

        /// <summary>A gliding car shows its brakes from this deceleration, m/s², and puts them out below the second.</summary>
        private const float BrakeOn = 2.5f, BrakeOff = 1.2f;
        /// <summary>How quickly the measured deceleration follows the speed, per second.</summary>
        private const float DecelRate = 4f;

        /// <summary>False for a car built without headlamps, such as an F1 car: it lights nothing ahead.</summary>
        public bool HasHeadlamps { get; private set; } = true;
        /// <summary>Middle of the headlamps, in the car's space.</summary>
        public Vector3 Nose { get; private set; } = new Vector3(0f, 0.7f, 2.3f);
        /// <summary>How far forward the car reaches, in its space, m: the front of its bumper.</summary>
        public float Front { get; private set; } = 2.3f;

        private readonly struct BrakeSlot
        {
            public readonly Renderer Renderer;
            public readonly int Index;
            public readonly Material Off, On;

            public BrakeSlot(Renderer renderer, int index, Material off, Material on)
            {
                Renderer = renderer;
                Index = index;
                Off = off;
                On = on;
            }
        }

        private readonly List<BrakeSlot> brakes = new List<BrakeSlot>();
        private CarController car;
        private bool braking;
        private float lastSpeed, decel;

        /// <summary>Fits the lamps: on the model's lamp parts, or on a stand-in box when there is no model.</summary>
        public static CarLights Fit(CarController car, GameObject model)
        {
            CarLights lamps = car.gameObject.AddComponent<CarLights>();
            lamps.car = car;
            if (model != null) lamps.FitModel(model);
            else lamps.FitBox();
            return lamps;
        }

        private void FitModel(GameObject model)
        {
            LampMaterials shared = LampMaterials.Shared;
            Bounds heads = default;
            bool anyHead = false;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string name = mats[i].name;
                    if (name.StartsWith("lamp_head"))
                    {
                        mats[i] = shared.HeadFor(mats[i]);
                        changed = true;
                        Bounds b = r.bounds;
                        if (anyHead) heads.Encapsulate(b); else heads = b;
                        anyHead = true;
                    }
                    else if (name.StartsWith("lamp_tail"))
                    {
                        mats[i] = shared.TailFor(mats[i]);
                        changed = true;
                    }
                    else if (name.StartsWith("lamp_brake"))
                        brakes.Add(new BrakeSlot(r, i, mats[i], shared.BrakeFor(mats[i])));
                }
                if (changed) r.sharedMaterials = mats;
            }
            if (anyHead) Nose = transform.InverseTransformPoint(heads.center);
            HasHeadlamps = anyHead;
            Front = Nose.z;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
            {
                Bounds b = r.bounds;
                for (int k = 0; k < 8; k++)
                {
                    var corner = new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z);
                    Front = Mathf.Max(Front, transform.InverseTransformPoint(corner).z);
                }
            }
        }

        private void FitBox()
        {
            LampMaterials shared = LampMaterials.Shared;
            Lamp("Headlamps", shared.BoxFront, shared.BoxHead);
            Renderer rear = Lamp("Tail lamps", shared.BoxRear, shared.BoxTail);
            brakes.Add(new BrakeSlot(rear, 0, shared.BoxTail, shared.BoxBrake));
            Vector3 c = CarBuilder.BoxCentre, s = CarBuilder.BoxSize;
            Nose = new Vector3(0f, c.y + s.y * 0.2f, c.z + s.z * 0.5f);
            Front = Nose.z;
        }

        private Renderer Lamp(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name) { layer = gameObject.layer };
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        /// <summary>True while the car's brake lamps are lit.</summary>
        public bool Braking => braking;

        private void OnEnable() => All.Add(this);

        private void OnDisable() => All.Remove(this);

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || car == null) return;
            float speed = car.Speed;
            decel += ((lastSpeed - speed) / dt - decel) * (1f - Mathf.Exp(-DecelRate * dt));
            lastSpeed = speed;
            bool on = car.Asleep ? (braking ? decel > BrakeOff : decel > BrakeOn) : car.Brake > 0.1f || car.Hold;
            if (on == braking) return;
            braking = on;
            // The renderer's materials are read afresh: the car's other materials can change live (CarMaterials).
            foreach (BrakeSlot b in brakes)
            {
                Material[] mats = b.Renderer.sharedMaterials;
                mats[b.Index] = on ? b.On : b.Off;
                b.Renderer.sharedMaterials = mats;
            }
        }
    }

    /// <summary>
    /// The lamp materials every car shares, so the field's lamps draw together: glowing copies of each model's own lamp
    /// materials, clear lenses over them and the stand-in boxes' lamps. <see cref="Night"/> lights them
    /// all at once.
    /// </summary>
    public sealed class LampMaterials
    {
        private static LampMaterials shared;
        public static LampMaterials Shared => shared ??= new LampMaterials();

        public readonly Material BoxHead, BoxTail, BoxBrake;
        public readonly Mesh BoxFront, BoxRear;

        private readonly Dictionary<Material, Material> heads = new Dictionary<Material, Material>(), tails = new Dictionary<Material, Material>(),
            brakes = new Dictionary<Material, Material>(), headLenses = new Dictionary<Material, Material>(),
            tailLenses = new Dictionary<Material, Material>();
        private readonly Color headTint;
        private float night;

        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        /// <summary>How much of its lamps' glow a lens shows, and how see-through a lens is when the model's own is opaque.</summary>
        private const float LensGlow = 0.3f, LensAlpha = 0.25f, LensSmoothness = 0.98f;

        private LampMaterials()
        {
            headTint = NightBudget.Headlamp;
            BoxHead = Glowing(Standard("Headlamp", new Color(0.78f, 0.8f, 0.82f), 0.92f));
            BoxTail = Glowing(Standard("Tail lamp", new Color(0.32f, 0.03f, 0.03f), 0.85f));
            BoxBrake = Glowing(Standard("Brake lamp", new Color(0.55f, 0.04f, 0.03f), 0.85f));
            Vector3 c = CarBuilder.BoxCentre, s = CarBuilder.BoxSize;
            BoxFront = Pair("Headlamps", c.z + s.z * 0.5f + 0.005f, 0.62f, c.y + s.y * 0.2f, 0.42f, 0.16f, 1f);
            BoxRear = Pair("Tail lamps", c.z - s.z * 0.5f - 0.005f, 0.68f, c.y + s.y * 0.25f, 0.36f, 0.14f, -1f);
            Night(0f);
        }

        /// <summary>A glowing copy of a model's headlamp material, made once and shared by every car with that model.</summary>
        public Material HeadFor(Material source) => CopyOf(heads, source, "lit");
        public Material TailFor(Material source) => CopyOf(tails, source, "lit");
        public Material BrakeFor(Material source)
        {
            Material m = CopyOf(brakes, source, "braking");
            m.SetColor(EmissionId, Brake);
            return m;
        }

        /// <summary>
        /// A clear, glossy lens from a model's lamp glass (<see cref="CarMaterials"/>), in its own tint, that glows softly
        /// with the head or tail lamps behind it. Made once and shared by every car with that model.
        /// </summary>
        public Material LensFor(Material source, bool tail)
        {
            Dictionary<Material, Material> lenses = tail ? tailLenses : headLenses;
            if (lenses.TryGetValue(source, out Material m)) return m;
            Color c = CarMaterials.ColourOf(source);
            m = new Material(Lit.Shader) { name = source.name + " lens", enableInstancing = true };
            m.SetColor(Lit.BaseColorId, new Color(c.r, c.g, c.b, c.a < 0.99f ? c.a : LensAlpha));
            m.SetFloat(Lit.SmoothnessId, LensSmoothness);
            Lit.Glass(m);
            lenses[source] = Glowing(m);
            Night(night);
            return m;
        }

        /// <summary>Lights the headlamps and the tail lamps for a darkness from <see cref="TimeOfDay.Lights"/>.</summary>
        public void Night(float amount)
        {
            night = amount;
            float on = NightBudget.On(amount);
            Color head = NightBudget.Glow(headTint, NightBudget.HeadlampGlow * on), tail = NightBudget.Glow(NightBudget.Red, NightBudget.TailGlow * on);
            BoxHead.SetColor(EmissionId, head.gamma);
            BoxTail.SetColor(EmissionId, tail.gamma);
            BoxBrake.SetColor(EmissionId, Brake);
            foreach (Material m in heads.Values) m.SetColor(EmissionId, head.gamma);
            foreach (Material m in tails.Values) m.SetColor(EmissionId, tail.gamma);
            foreach (Material m in headLenses.Values) m.SetColor(EmissionId, (head * LensGlow).gamma);
            foreach (Material m in tailLenses.Values) m.SetColor(EmissionId, (tail * LensGlow).gamma);
        }

        public Color HeadTint => headTint;

        /// <summary>A lit brake lamp's glow, as the material takes it.</summary>
        private static Color Brake => NightBudget.Glow(NightBudget.Red, NightBudget.BrakeGlow).gamma;

        private Material CopyOf(Dictionary<Material, Material> copies, Material source, string suffix)
        {
            if (copies.TryGetValue(source, out Material m)) return m;
            m = Glowing(new Material(source) { name = source.name + " " + suffix, enableInstancing = true });
            copies[source] = m;
            Night(night);
            return m;
        }

        private static Material Glowing(Material m)
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetColor(EmissionId, Color.black);
            return m;
        }

        private static Material Standard(string name, Color colour, float smoothness)
        {
            var m = new Material(Lit.Shader) { name = name, enableInstancing = true, color = colour };
            m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        /// <summary>Two lamps on a box end, at ±x, facing +z when facing is 1 and -z when it is -1.</summary>
        private static Mesh Pair(string name, float z, float x, float y, float w, float h, float facing)
        {
            var kit = new MeshKit();
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 m = new Vector3(side * x, y, z);
                Vector3 hx = Vector3.right * (w * 0.5f), hy = Vector3.up * (h * 0.5f);
                kit.Face(m - hx - hy, m + hx - hy, m - hx + hy, m + hx + hy, m - Vector3.forward * facing);
            }
            return kit.ToMesh(name);
        }
    }
}
