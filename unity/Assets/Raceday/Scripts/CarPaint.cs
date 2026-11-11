using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Resprays a car model. Every car's paint is the material named "body"; everything else
    /// (glass, lamps, badges, tyres) keeps its own colour. See docs/car-paint.md.
    /// </summary>
    public static class CarPaint
    {
        public const string PaintMaterial = "body";

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
                    if (sprayed == null)
                    {
                        sprayed = new Material(mats[i]) { name = PaintMaterial + " (sprayed)" };
                        if (sprayed.HasProperty("_Color")) sprayed.SetColor("_Color", colour);
                        if (sprayed.HasProperty("_BaseColor")) sprayed.SetColor("_BaseColor", colour);
                    }
                    mats[i] = sprayed;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }
    }
}
