#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// The 3D model a car is drawn with. For now every car of a class borrows that class's one
    /// finished model.
    /// </summary>
    public static class CarArt
    {
        private static readonly Dictionary<CarClass, string> ByClass = new Dictionary<CarClass, string>
        {
            { CarClass.F, "Assets/Art/Vehicles/LibertyCollier/LibertyCollier.obj" },
            { CarClass.E, "Assets/Art/Vehicles/KaiserOboe/KaiserOboe.obj" },
            { CarClass.D, "Assets/Art/Vehicles/ToraHanabi/ToraHanabi.obj" },
            { CarClass.C, "Assets/Art/Vehicles/ArcherCrescendo/ArcherCrescendo.obj" },
            { CarClass.B, "Assets/Art/Vehicles/FerettiVortice/FerettiVortice.obj" },
            { CarClass.A, "Assets/Art/Vehicles/CheonmaCX24Si/CheonmaCX24Si.obj" },
            { CarClass.F1, "Assets/Art/Vehicles/FerettiF256V8T/FerettiF256V8T.obj" },
        };

        /// <summary>The model's asset path, or null for an unknown model id.</summary>
        public static string? ModelPath(string modelId)
        {
            CarModel? model = CarModels.All.FirstOrDefault(c => c.Id == modelId);
            return model == null ? null : ByClass[model.CarClass];
        }
    }
}
