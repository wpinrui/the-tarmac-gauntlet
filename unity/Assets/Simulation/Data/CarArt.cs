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
            { "f-01", "Assets/Art/Vehicles/NusaCarrier/NusaCarrier.obj" },
            { "f-02", "Assets/Art/Vehicles/LibertyOmaha/LibertyOmaha.obj" },
            { "f-03", "Assets/Art/Vehicles/ToraDaruma/ToraDaruma.obj" },
            { "f-04", "Assets/Art/Vehicles/KaiserFirefly/KaiserFirefly.obj" },
            { "f-06", "Assets/Art/Vehicles/NusaFox/NusaFox.obj" },
            { "e-07", "Assets/Art/Vehicles/LibertyFantasy/LibertyFantasy.obj" },
            { "d-12", "Assets/Art/Vehicles/SeorakHoeori/SeorakHoeori.obj" },
            { "c-20", "Assets/Art/Vehicles/ToraGiga/ToraGiga.obj" },
            { "b-22", "Assets/Art/Vehicles/ArcherTachyon/ArcherTachyon.obj" },
            { "b-23", "Assets/Art/Vehicles/ArcherJetBlack/ArcherJetBlack.obj" },
            { "b-24", "Assets/Art/Vehicles/KaiserScream/KaiserScream.obj" },
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
            { "Gravel Trap Racing", "Assets/Art/Liveries/GravelTrapRacing" },
            { "Ironbark Racing", "Assets/Art/Liveries/IronbarkRacing" },
            { "Poseidon Racing", "Assets/Art/Liveries/PoseidonRacing" },
            { "North Star Racing", "Assets/Art/Liveries/NorthStarRacing" },
            { "Team Archer", "Assets/Art/Liveries/TeamArcher" },
            { "Dockyard Racing", "Assets/Art/Liveries/DockyardRacing" },
            { "Green Goat Racing", "Assets/Art/Liveries/GreenGoatRacing" },
            { "Duct Tape Dynamics", "Assets/Art/Liveries/DuctTapeDynamics" },
            { "Greystone Archer", "Assets/Art/Liveries/GreystoneArcher" },
            { "Lo-fi Racing", "Assets/Art/Liveries/LofiRacing" },
            { "Shed Monozukuri Co.", "Assets/Art/Liveries/ShedMonozukuriCo" },
            { "Saltire Racing", "Assets/Art/Liveries/SaltireRacing" },
            { "Flatbed Legends", "Assets/Art/Liveries/FlatbedLegends" },
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
