#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// The 3D model a car is drawn with: its own model once it has one, otherwise the one
    /// finished model of its class.
    /// </summary>
    public static class CarArt
    {
        private static readonly Dictionary<string, string> ById = new Dictionary<string, string>
        {
            { "b-22", "Assets/Art/Vehicles/ArcherTachyon/ArcherTachyon.obj" },
        };

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

        /// <summary>Each painted team's livery folder: atlas.png and livery.json, from tools/liveries/.</summary>
        private static readonly Dictionary<string, string> LiveryByTeam = new Dictionary<string, string>
        {
            { "Scuderia Feretti", "Assets/Art/Liveries/ScuderiaFeretti" },
            { "Team Seorak", "Assets/Art/Liveries/TeamSeorak" },
        };

        /// <summary>The team's livery folder, or null for a team with no livery yet.</summary>
        public static string? LiveryPath(string teamName) =>
            LiveryByTeam.TryGetValue(teamName, out string? path) ? path : null;

        /// <summary>The model's asset path, or null for an unknown model id.</summary>
        public static string? ModelPath(string modelId)
        {
            CarModel? model = CarModels.All.FirstOrDefault(c => c.Id == modelId);
            if (model == null) return null;
            return ById.TryGetValue(model.Id, out string? own) ? own : ByClass[model.CarClass];
        }
    }
}
