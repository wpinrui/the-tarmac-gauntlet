using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Resprays a car model. Every car's paint is the material named "body"; everything else
    /// (glass, lamps, badges, tyres) keeps its own colour. The new paint is drawn by the CarPaint
    /// shader, with its clear coat and flakes (<see cref="CarMaterials"/>). See docs/car-paint.md.
    /// </summary>
    public static class CarPaint
    {
        public const string PaintMaterial = "body";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static Shader shader;

        /// <summary>Gives the car's paint the colour. Only this car changes, not the shared model.</summary>
        public static void Respray(GameObject car, Color colour)
        {
            Material sprayed = null;
            foreach (Renderer r in car.GetComponentsInChildren<Renderer>())
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || (mats[i].name != PaintMaterial && mats[i] != sprayed)) continue;
                    if (sprayed == null) sprayed = Spray(mats[i], colour);
                    mats[i] = sprayed;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>The model's own paint colour, or null when it has no paint.</summary>
        public static Color? ColourOf(GameObject car)
        {
            foreach (Renderer r in car.GetComponentsInChildren<Renderer>())
            foreach (Material m in r.sharedMaterials)
                if (m != null && m.name == PaintMaterial) return CarMaterials.ColourOf(m);
            return null;
        }

        private static Material Spray(Material source, Color colour)
        {
            if (shader == null) shader = Resources.Load<Shader>("CarPaint/CarPaint");
            Material sprayed = shader != null ? new Material(shader) : new Material(source);
            sprayed.name = PaintMaterial + " (sprayed)";
            if (sprayed.HasProperty("_Color")) sprayed.SetColor("_Color", colour);
            if (sprayed.HasProperty(BaseColorId)) sprayed.SetColor(BaseColorId, new Color(colour.r, colour.g, colour.b, 1f));
            return sprayed;
        }
    }
}
