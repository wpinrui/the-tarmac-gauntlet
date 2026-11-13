using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Materials for the floodlights, made in code like <see cref="CircuitMaterials"/>: galvanised steel for the masts,
    /// and lenses that glow at night (<see cref="Floodlights"/>).
    /// </summary>
    public sealed class FloodMaterials
    {
        public readonly Material Steel, Lens;

        private readonly Color tint;
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public FloodMaterials()
        {
            tint = NightLook.Flood;
            Steel = Standard("Floodlight steel", new Color(0.55f, 0.57f, 0.58f), 0.4f, 0.5f);
            Lens = Standard("Floodlight lenses", new Color(0.55f, 0.57f, 0.56f), 0.9f, 0f);
            Lens.EnableKeyword("_EMISSION");
            Lens.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            Night(0f);
        }

        /// <summary>Colour of the floodlights' light.</summary>
        public Color Tint => tint;

        /// <summary>Lights the lenses for a darkness from <see cref="TimeOfDay.Lights"/>.</summary>
        public void Night(float night)
        {
            Lens.SetColor(EmissionId, tint * (NightLook.LensGlow * NightLook.On(night)));
        }

        private static Material Standard(string name, Color colour, float smoothness, float metallic)
        {
            var m = new Material(Lit.Shader) { name = name, enableInstancing = true, color = colour };
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            return m;
        }
    }
}
