using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Materials for the floodlights, made in code like <see cref="CircuitMaterials"/>: galvanised steel for the masts,
    /// lenses that glow at night, and the pools of light on the ground (<see cref="Floodlights"/>).
    /// </summary>
    public sealed class FloodMaterials
    {
        public readonly Material Steel, Lens, Pool;

        private readonly Color tint;
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor"), ColorId = Shader.PropertyToID("_Color"),
            LiftId = Shader.PropertyToID("_Lift");

        public FloodMaterials()
        {
            tint = NightLook.Flood;
            Steel = Standard("Floodlight steel", new Color(0.55f, 0.57f, 0.58f), 0.4f, 0.5f);
            Lens = Standard("Floodlight lenses", new Color(0.55f, 0.57f, 0.56f), 0.9f, 0f);
            Lens.EnableKeyword("_EMISSION");
            Lens.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            Pool = PoolMaterial("Floodlight pools");
            Night(0f);
        }

        /// <summary>Colour of the floodlights' light.</summary>
        public Color Tint => tint;

        /// <summary>Lights the lenses and the pools for a night amount from <see cref="SkyClock"/>.</summary>
        public void Night(float night)
        {
            Lens.SetColor(EmissionId, tint * (NightLook.LensGlow * NightLook.On(night)));
            Pool.SetVector(ColorId, Brightening(tint, NightLook.PoolBrightening * night));
            Pool.SetVector(LiftId, Lift(tint, NightLook.PoolLift * night));
        }

        /// <summary>
        /// A pool's first pass multiplies the colour behind it by one plus this. The brightening is meant in linear
        /// light; in gamma colour space the blend happens on gamma values, so the factor is taken down to match.
        /// </summary>
        public static Vector4 Brightening(Color tint, float amount)
        {
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) return (Vector4)(tint.linear * amount);
            return (Vector4)(tint * (Mathf.Pow(1f + amount, 1f / 2.2f) - 1f));
        }

        /// <summary>A pool's second pass adds this: an amount of linear light, carried into gamma space where it blends there.</summary>
        public static Vector4 Lift(Color tint, float amount)
        {
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) return (Vector4)(tint.linear * amount);
            return (Vector4)(tint * Mathf.Pow(Mathf.Max(amount, 0f), 1f / 2.2f));
        }

        /// <summary>A pool of light material, drawn over the ground (Night/LightPool).</summary>
        public static Material PoolMaterial(string name) =>
            new Material(Resources.Load<Shader>("Night/LightPool")) { name = name, enableInstancing = true };

        private static Material Standard(string name, Color colour, float smoothness, float metallic)
        {
            var m = new Material(Shader.Find("Standard")) { name = name, enableInstancing = true, color = colour };
            m.SetFloat("_Glossiness", smoothness);
            m.SetFloat("_Metallic", metallic);
            return m;
        }
    }
}
