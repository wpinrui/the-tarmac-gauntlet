using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>One mesh of the circuit and the material it is drawn with.</summary>
    public sealed class CircuitPart
    {
        public readonly string Name;
        public readonly MeshKit Kit;
        public readonly Material Material;
        /// <summary>Cars collide with it.</summary>
        public readonly bool Solid;
        /// <summary>Stands up off the ground, so it casts a shadow worth drawing.</summary>
        public readonly bool CastsShadows;

        public CircuitPart(string name, MeshKit kit, Material material, bool solid, bool castsShadows)
        {
            CastsShadows = castsShadows;
            Name = name;
            Kit = kit;
            Material = material;
            Solid = solid;
        }
    }
}
