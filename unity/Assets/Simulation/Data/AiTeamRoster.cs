#nullable enable
using System.Collections.Generic;

namespace TarmacGauntlet.Simulation
{
    /// <summary>Port of simulation/aiTeamNames.ts.</summary>
    public static class AiTeamRoster
    {
        /// <summary>
        /// 99 pre-authored AI team identities, distributed across classes to match
        /// the tier counts in GameInit (F:20, E:20, D:20, C:15, B:12, A:12).
        ///
        /// Teams are ordered by class so GameInit can assign them sequentially
        /// as it iterates through AiTierSpecs.
        /// </summary>
        public static readonly IReadOnlyList<AiTeamEntry> All = new[]
        {
            // CLASS F: 20 teams
            new AiTeamEntry { Name = "Backfire Racing", Descriptor = "three mates and a trailer", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Shed Speed Co.", Descriptor = "built in an actual garden shed", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Clutch & Pray", Descriptor = "optimistic underdogs", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Spare Parts Racing", Descriptor = "the car is mostly replacement bits", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Gravel Trap Racing", Descriptor = "frequent visitors to the run-off area", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Duct Tape Dynamics", Descriptor = "structural integrity is a suggestion", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Flatbed Legends", Descriptor = "arrive on a flatbed, leave on a flatbed", CarClass = CarClass.F },
            new AiTeamEntry { Name = "No Insurance Motorsport", Descriptor = "living dangerously in every sense", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Oily Rag Racing", Descriptor = "one rag, two spanners, zero budget", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Horizon Motorsport", Descriptor = "eyes on the distance, not the standings", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Ironbark Racing", Descriptor = "Australian hardwood — won't snap", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Mapleton Autosport", Descriptor = "small-town team with real ambition", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Vega Racing", Descriptor = "named after the brightest star they could see", CarClass = CarClass.F },
            new AiTeamEntry { Name = "First Gear Motorsport", Descriptor = "everyone starts somewhere", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Saltire Racing", Descriptor = "Scottish pride on a shoestring", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Croft & Davis", Descriptor = "two founders, one rusted car", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Long Road Racing", Descriptor = "long way to go, willing to drive it", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Pemberton Motorsport", Descriptor = "family name on the door, heart on the sleeve", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Basecamp Autosport", Descriptor = "starting from the bottom, climbing up", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Cornerstone Racing", Descriptor = "building something from nothing", CarClass = CarClass.F },
            // CLASS E: 20 teams
            new AiTeamEntry { Name = "Dockyard Racing", Descriptor = "harbour-town mechanics turned racers", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Whitfield & Sons", Descriptor = "father-and-sons operation since forever", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Copper Lane Autosport", Descriptor = "named after the street they wrench on", CarClass = CarClass.E },
            new AiTeamEntry { Name = "North Star Racing", Descriptor = "Scandinavian expats chasing points", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Terrace Motorsport", Descriptor = "working-class fan-funded team", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Gasworks Racing", Descriptor = "smells like petrol, runs like clockwork", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Southgate Motorsport", Descriptor = "suburban team, real commitment", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Barnstormer Racing", Descriptor = "rural team, loud cars, no apologies", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Overtime Racing", Descriptor = "day jobs fund the weekend habit", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Vogel Racing", Descriptor = "German-founded privateer with discipline", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Harrow Motorsport", Descriptor = "west London outfit, punching up", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Caldwell Racing", Descriptor = "veteran driver turned team owner", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Half Tank Racing", Descriptor = "never quite enough fuel budget", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Blackthorn Racing", Descriptor = "thorny, stubborn, hard to pass", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Cóndor Racing", Descriptor = "Colombian-founded, passionate about racing", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Pit Lane Dreamers", Descriptor = "small team with big ambitions", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Three Pedals Racing", Descriptor = "manual gearbox purists", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Overpass Racing", Descriptor = "headquartered under a motorway bridge", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Sato Brothers Racing", Descriptor = "Japanese siblings building from scratch", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Yellow Flag Motorsport", Descriptor = "cautious but surprisingly consistent", CarClass = CarClass.E },
            // CLASS D: 20 teams
            new AiTeamEntry { Name = "Seorak Technica", Descriptor = "Seorak's rally-bred factory effort", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Packet Loss Racing", Descriptor = "tech-money privateer, data-obsessed", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Petrol Mush Racing", Descriptor = "fuel sponsor backing a scrappy sports car team", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Poseidon Racing", Descriptor = "trident-bearing privateer with sharp ambitions", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Virginia Motorsport", Descriptor = "French-founded privateer on the rise", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Insight Racing", Descriptor = "tech-backed team chasing efficiency gains", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Ember Racing", Descriptor = "always bouncing back from the brink", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Freightline Racing", Descriptor = "corporate-backed mid-tier squad", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Gearbox Grease Racing", Descriptor = "lubricant-sponsored touring car operation", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Compost Racing", Descriptor = "scrappy but legitimate single-seater graduates", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Van Dijk Racing", Descriptor = "Dutch-run team with an eye on the podium", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Thornton Motorsport", Descriptor = "British family-run sports car team", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Brackley Engineering", Descriptor = "ex-factory engineers gone independent", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Nakamura Racing", Descriptor = "Japanese privateer with touge roots", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Red Anvil Autosport", Descriptor = "heavy-handed but fast", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Costa Brava Racing", Descriptor = "Spanish sun-belt outfit", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Steelworks Motorsport", Descriptor = "industrial town pride project", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Müller Sport", Descriptor = "German precision on a modest budget", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Park Ferme Racing", Descriptor = "obsessive about procedure and prep", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Ravenhill Autosport", Descriptor = "Northern Irish grit, southern ambition", CarClass = CarClass.D },
            // CLASS C: 15 teams
            new AiTeamEntry { Name = "Team Tora Racing", Descriptor = "Tora's official touring/GT squad", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Tora GT Works", Descriptor = "Tora's factory GT programme", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Greystone Archer", Descriptor = "Archer-supported independent squad", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Vosper Liberty Racing", Descriptor = "Liberty-backed privateer with works engines", CarClass = CarClass.C },
            new AiTeamEntry { Name = "KNS Seorak", Descriptor = "Seorak's Southeast Asian satellite programme", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Prodigy Racing", Descriptor = "feeder-series talent factory gone GT", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Canvas Grand Prix", Descriptor = "elite junior formula team stepping up", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Agile 1 Motorsport", Descriptor = "technical sponsor turned team owner", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Full ERS Racing", Descriptor = "fuel company's professional GT outfit", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Lo-fi Racing", Descriptor = "engineering-first operation with junior pedigree", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Maestro Racing", Descriptor = "polished operation punching into the pro ranks", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Celine Racing", Descriptor = "top-tier feeder series outfit stepping into GT", CarClass = CarClass.C },
            new AiTeamEntry { Name = "TRAMS", Descriptor = "storied French racing operation", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Alien Power Motorsport", Descriptor = "energy drink-sponsored privateer with deep pockets", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Barnacle Racing", Descriptor = "fuel giant's long-running works partnership", CarClass = CarClass.C },
            // CLASS B: 12 teams
            new AiTeamEntry { Name = "Scuderia Feretti", Descriptor = "Feretti's official works racing team", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Team Archer", Descriptor = "Archer' performance division works entry", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Liberty Performance Racing", Descriptor = "Liberty's factory GT programme", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Molteni Corse", Descriptor = "Feretti customer team, Italian privateer", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Apex Archer", Descriptor = "Archer satellite with factory parts supply", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Stonebridge Kaiser", Descriptor = "British-run Kaiser customer racing outfit", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Sip Vanessa Racing", Descriptor = "veteran-run multi-series operation", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Vespro Corse", Descriptor = "Italian GT privateer powerhouse", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Green Goat Racing", Descriptor = "energy drink empire's factory racing arm", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Golf Course Endurance", Descriptor = "heritage oil brand's flagship endurance entry", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Tsubame Racing", Descriptor = "Japanese motorcycle giant's four-wheel factory effort", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Zugzwang Factory Racing", Descriptor = "Austrian works team with two-wheel pedigree", CarClass = CarClass.B },
            // CLASS A: 12 teams
            new AiTeamEntry { Name = "Archer Prototype Works", Descriptor = "Archer's factory endurance programme", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Kaiser Motorsport", Descriptor = "Kaiser's flagship prototype operation", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Kaiser Dynamo Racing", Descriptor = "Kaiser's factory LMP effort", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Team Seorak", Descriptor = "Seorak's global endurance works team", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Liberty Frontier", Descriptor = "Liberty's works prototype team", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Panski Autosport", Descriptor = "multi-discipline racing dynasty", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Fiorini Motorsport", Descriptor = "expanding privateer empire", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Angsty Racing", Descriptor = "Swiss-bankrolled endurance privateer", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Fairbairn Racing", Descriptor = "historic family-owned racing team", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Zurlinden Motorsport", Descriptor = "Swiss engineering outfit with decades of pedigree", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Saetta Corse", Descriptor = "Italian motorcycle powerhouse dominating on four wheels", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Brezza Racing", Descriptor = "Italian factory team with MotoGP heritage", CarClass = CarClass.A },
        };
    }
}
