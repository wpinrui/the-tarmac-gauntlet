using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Makes the 3D car for a model. A model with a folder at
    /// Resources/CarModels/&lt;model id&gt;/ holding a prefab named "Car" uses that prefab
    /// (metres, +Z forward, origin on the ground at the car's centre). Any other model draws
    /// as a car-sized cuboid in its class colour.
    /// </summary>
    public static class CarFactory
    {
        public static GameObject Create(CarModel model, bool isPlayer, Materials materials)
        {
            GameObject prefab = Resources.Load<GameObject>($"CarModels/{model.Id}/Car");
            var root = new GameObject(model.Id);
            if (prefab != null)
            {
                Object.Instantiate(prefab, root.transform, false);
                return root;
            }

            Vector3 size = Size(model.CarClass);
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0, size.y / 2, 0);
            body.transform.localScale = size;
            body.GetComponent<Renderer>().sharedMaterial = materials.Get(ClassColour(model.CarClass));

            // The player's car carries a white roof block so it reads at a glance.
            if (isPlayer)
            {
                GameObject roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
                roof.name = "Roof";
                roof.transform.SetParent(root.transform, false);
                roof.transform.localPosition = new Vector3(0, size.y + 0.15f, -0.2f);
                roof.transform.localScale = new Vector3(size.x * 0.7f, 0.3f, size.z * 0.4f);
                roof.GetComponent<Renderer>().sharedMaterial = materials.Get(Color.white);
            }
            return root;
        }

        /// <summary>Width, height, length in metres.</summary>
        private static Vector3 Size(CarClass cls) => cls switch
        {
            CarClass.F => new Vector3(1.65f, 1.45f, 3.9f),
            CarClass.E => new Vector3(1.8f, 1.45f, 4.4f),
            CarClass.D => new Vector3(1.8f, 1.3f, 4.3f),
            CarClass.C => new Vector3(1.9f, 1.25f, 4.6f),
            CarClass.B => new Vector3(1.95f, 1.15f, 4.6f),
            CarClass.A => new Vector3(2.0f, 1.05f, 4.9f),
            CarClass.F1 => new Vector3(1.8f, 0.95f, 5.0f),
            _ => new Vector3(1.8f, 1.3f, 4.5f),
        };

        public static Color ClassColour(CarClass cls) => cls switch
        {
            CarClass.F => new Color(0.55f, 0.45f, 0.35f),
            CarClass.E => new Color(0.30f, 0.55f, 0.85f),
            CarClass.D => new Color(0.25f, 0.70f, 0.40f),
            CarClass.C => new Color(0.95f, 0.75f, 0.15f),
            CarClass.B => new Color(0.95f, 0.45f, 0.10f),
            CarClass.A => new Color(0.85f, 0.15f, 0.20f),
            CarClass.F1 => new Color(0.60f, 0.20f, 0.80f),
            _ => Color.grey,
        };
    }
}
