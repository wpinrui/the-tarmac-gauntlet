using TarmacGauntlet.TeamHq;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The pit complex in the race: paints each garage in its team's colour, puts the Poly Haven tool carts and
    /// chests in every garage with a car (dropped once a garage is too small on screen to show them) with the pit box
    /// kit (<see cref="PitBoxKitView"/>), and lights the office windows as the sky darkens.
    /// </summary>
    public sealed class PitComplexView : MonoBehaviour
    {
        public TimeOfDay Sky;
        public PitMaterials Materials;

        /// <summary>A garage's props are dropped below this share of the screen's height.</summary>
        private const float PropsCull = 0.05f;
        /// <summary>Real seconds between checks of the sky.</summary>
        private const float SkyCheck = 0.5f;

        /// <summary>The kit in and over each box with a car, which the crews take and put back.</summary>
        public PitBoxKitView Kit { get; private set; }

        private float nextCheck;
        private float lastNight = -1f;

        /// <summary>
        /// Paints each car's garage in its colour and fits out each garage with a car. Garages no car uses stay plain
        /// and empty. <c>carGarages</c> is each car's garage.
        /// </summary>
        public void Fit(PitComplex complex, Color[] cars, int[] carGarages, int garages)
        {
            var paint = new Color[garages];
            for (int g = 0; g < garages; g++) paint[g] = PitMaterials.Empty;
            var used = new int[cars.Length];
            for (int i = 0; i < used.Length; i++)
            {
                int g = i < carGarages.Length ? carGarages[i] : -1;
                used[i] = g >= 0 && g < garages ? g : -1;
                if (used[i] >= 0) paint[used[i]] = cars[i];
            }
            Materials.Paint(paint);
            var fitted = new bool[garages];
            foreach (int g in used)
            {
                if (g < 0 || fitted[g]) continue;
                fitted[g] = true;
                Props(complex.Frame(g), g);
            }
            Kit = new GameObject("Pit box kit").AddComponent<PitBoxKitView>();
            Kit.transform.SetParent(transform, false);
            Kit.Build(complex, used, cars);
        }

        private void Props(GarageFrame f, int g)
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
            float night = Sky.Lights;
            if (Mathf.Abs(night - lastNight) < 0.01f) return;
            lastNight = night;
            Materials.Night(night);
        }
    }
}
