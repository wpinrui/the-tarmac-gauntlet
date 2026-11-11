using TarmacGauntlet.TeamHq;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The pit complex in the race: paints each garage in its team's colour, puts the Poly Haven tool carts and
    /// chests in every garage with a car (dropped once a garage is too small on screen to show them), and lights
    /// the office windows as the sky darkens.
    /// </summary>
    public sealed class PitComplexView : MonoBehaviour
    {
        public TimeOfDay Sky;
        public PitMaterials Materials;

        /// <summary>A garage's props are dropped below this share of the screen's height.</summary>
        private const float PropsCull = 0.05f;
        /// <summary>Real seconds between checks of the sky.</summary>
        private const float SkyCheck = 0.5f;

        private float nextCheck;
        private float lastNight = -1f;

        /// <summary>
        /// Paints garage g in the colour of car g, the car that uses it, and fits out each garage with a car.
        /// Garages beyond the field stay plain and empty.
        /// </summary>
        public void Fit(PitComplex complex, Color[] cars, int garages)
        {
            var paint = new Color[garages];
            for (int g = 0; g < garages; g++) paint[g] = g < cars.Length ? cars[g] : PitMaterials.Empty;
            Materials.Paint(paint);
            for (int g = 0; g < garages && g < cars.Length; g++) Kit(complex.Frame(g), g);
        }

        private void Kit(GarageFrame f, int g)
        {
            var kit = new GameObject("Garage " + (g + 1) + " kit");
            kit.transform.SetParent(transform, false);
            kit.transform.SetPositionAndRotation(f.Floor, Quaternion.LookRotation(f.Out, Vector3.up));
            foreach (GarageProp p in PitComplexBuilder.Props)
                HqProps.Place(kit.transform, new HqProp { Id = p.Id, Position = new Vector3(p.X, p.Y, p.Z), Yaw = p.Yaw, Scale = 1f });
            Renderer[] renderers = kit.GetComponentsInChildren<Renderer>();
            foreach (Renderer r in renderers)
                foreach (Material m in r.sharedMaterials) m.enableInstancing = true;
            LODGroup lod = kit.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(PropsCull, renderers) });
            lod.RecalculateBounds();
        }

        private void Update()
        {
            if (Sky == null || Materials == null || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + SkyCheck;
            float night = SkyClock.At(Sky.Hour, SkyClock.Look).Night;
            if (Mathf.Abs(night - lastNight) < 0.01f) return;
            lastNight = night;
            Materials.Night(night);
        }
    }
}
