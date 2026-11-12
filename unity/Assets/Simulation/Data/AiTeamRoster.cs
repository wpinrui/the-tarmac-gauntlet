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
            new AiTeamEntry { Name = "Backfire Racing", Descriptor = "three mates and a trailer", Nationality = "es", ModelId = "f-03", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Shed Speed Co.", Descriptor = "built in an actual garden shed", Nationality = "jp", ModelId = "f-02", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Clutch & Pray", Descriptor = "optimistic underdogs", Nationality = "au", ModelId = "f-03", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Spare Parts Racing", Descriptor = "the car is mostly replacement bits", Nationality = "in", ModelId = "f-04", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Gravel Trap Racing", Descriptor = "frequent visitors to the run-off area", Nationality = "th", ModelId = "f-05", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Duct Tape Dynamics", Descriptor = "structural integrity is a suggestion", Nationality = "cl", ModelId = "f-01", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Flatbed Legends", Descriptor = "arrive on a flatbed, leave on a flatbed", Nationality = "us", ModelId = "f-04", CarClass = CarClass.F },
            new AiTeamEntry { Name = "No Insurance Motorsport", Descriptor = "living dangerously in every sense", Nationality = "nz", ModelId = "f-02", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Oily Rag Racing", Descriptor = "one rag, two spanners, zero budget", Nationality = "nz", ModelId = "f-03", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Horizon Motorsport", Descriptor = "eyes on the distance, not the standings", Nationality = "fr", ModelId = "f-04", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Ironbark Racing", Descriptor = "Australian hardwood — won't snap", Nationality = "au", ModelId = "f-05", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Mapleton Autosport", Descriptor = "small-town team with real ambition", Nationality = "ca", ModelId = "f-06", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Vega Racing", Descriptor = "named after the brightest star they could see", Nationality = "es", ModelId = "f-05", CarClass = CarClass.F },
            new AiTeamEntry { Name = "First Gear Motorsport", Descriptor = "everyone starts somewhere", Nationality = "ph", ModelId = "f-02", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Saltire Racing", Descriptor = "Scottish pride on a shoestring", Nationality = "gb-sct", ModelId = "f-03", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Croft & Davis", Descriptor = "two founders, one rusted car", Nationality = "za", ModelId = "f-04", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Long Road Racing", Descriptor = "long way to go, willing to drive it", Nationality = "ar", ModelId = "f-05", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Pemberton Motorsport", Descriptor = "family name on the door, heart on the sleeve", Nationality = "gb", ModelId = "f-06", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Basecamp Autosport", Descriptor = "starting from the bottom, climbing up", Nationality = "ca", ModelId = "f-06", CarClass = CarClass.F },
            new AiTeamEntry { Name = "Cornerstone Racing", Descriptor = "building something from nothing", Nationality = "ar", ModelId = "f-02", CarClass = CarClass.F },
            // CLASS E: 20 teams
            new AiTeamEntry { Name = "Dockyard Racing", Descriptor = "from slipway to fast lane", Nationality = "sg", ModelId = "e-07", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Whitfield & Sons", Descriptor = "father-and-sons operation since forever", Nationality = "gb", ModelId = "e-08", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Copper Lane Autosport", Descriptor = "named after the street they wrench on", Nationality = "br", ModelId = "e-09", CarClass = CarClass.E },
            new AiTeamEntry { Name = "North Star Racing", Descriptor = "Steady as the pole star", Nationality = "se", ModelId = "e-10", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Terrace Motorsport", Descriptor = "working-class fan-funded team", Nationality = "gb", ModelId = "e-11", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Gasworks Racing", Descriptor = "smells like petrol, runs like clockwork", Nationality = "gb", ModelId = "e-07", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Southgate Motorsport", Descriptor = "suburban team, real commitment", Nationality = "ke", ModelId = "e-08", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Barnstormer Racing", Descriptor = "rural team, loud cars, no apologies", Nationality = "us", ModelId = "e-09", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Overtime Racing", Descriptor = "day jobs fund the weekend habit", Nationality = "au", ModelId = "e-10", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Vogel Racing", Descriptor = "German-founded privateer with discipline", Nationality = "de", ModelId = "e-11", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Harrow Motorsport", Descriptor = "west London outfit, punching up", Nationality = "gb", ModelId = "e-07", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Caldwell Racing", Descriptor = "veteran driver turned team owner", Nationality = "at", ModelId = "e-08", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Half Tank Racing", Descriptor = "never quite enough fuel budget", Nationality = "id", ModelId = "e-09", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Blackthorn Racing", Descriptor = "thorny, stubborn, hard to pass", Nationality = "ae", ModelId = "e-10", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Cóndor Racing", Descriptor = "Colombian-founded, passionate about racing", Nationality = "co", ModelId = "e-11", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Pit Lane Dreamers", Descriptor = "small team with big ambitions", Nationality = "br", ModelId = "e-07", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Three Pedals Racing", Descriptor = "manual gearbox purists", Nationality = "de", ModelId = "e-08", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Overpass Racing", Descriptor = "headquartered under a motorway bridge", Nationality = "gb", ModelId = "e-09", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Sato Brothers Racing", Descriptor = "Japanese siblings building from scratch", Nationality = "jp", ModelId = "e-10", CarClass = CarClass.E },
            new AiTeamEntry { Name = "Yellow Flag Motorsport", Descriptor = "cautious but surprisingly consistent", Nationality = "fi", ModelId = "e-11", CarClass = CarClass.E },
            // CLASS D: 20 teams
            new AiTeamEntry { Name = "Seorak Technica", Descriptor = "Seorak's rally-bred factory effort", Nationality = "us", ModelId = "d-12", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Packet Loss Racing", Descriptor = "tech-money privateer, data-obsessed", Nationality = "us", ModelId = "d-13", CarClass = CarClass.D },
            new AiTeamEntry { Name = "KNS Seorak", Descriptor = "Seorak's Southeast Asian satellite programme", Nationality = "kr", ModelId = "d-14", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Poseidon Racing", Descriptor = "trident-bearing privateer with sharp ambitions", Nationality = "it", ModelId = "d-15", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Virginia Motorsport", Descriptor = "French-founded privateer on the rise", Nationality = "fr", ModelId = "d-12", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Insight Racing", Descriptor = "tech-backed team chasing efficiency gains", Nationality = "kr", ModelId = "d-13", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Ember Racing", Descriptor = "always bouncing back from the brink", Nationality = "mx", ModelId = "d-14", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Freightline Racing", Descriptor = "corporate-backed mid-tier squad", Nationality = "de", ModelId = "d-15", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Gearbox Grease Racing", Descriptor = "lubricant-sponsored touring car operation", Nationality = "jp", ModelId = "d-12", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Compost Racing", Descriptor = "scrappy but legitimate single-seater graduates", Nationality = "cn", ModelId = "d-13", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Van Dijk Racing", Descriptor = "Dutch-run team with an eye on the podium", Nationality = "nl", ModelId = "d-14", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Thornton Motorsport", Descriptor = "British family-run sports car team", Nationality = "gb", ModelId = "d-15", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Brackley Engineering", Descriptor = "ex-factory engineers gone independent", Nationality = "gb", ModelId = "d-12", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Nakamura Racing", Descriptor = "Japanese privateer with touge roots", Nationality = "jp", ModelId = "d-13", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Red Anvil Autosport", Descriptor = "heavy-handed but fast", Nationality = "de", ModelId = "d-14", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Costa Brava Racing", Descriptor = "Spanish sun-belt outfit", Nationality = "es", ModelId = "d-15", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Steelworks Motorsport", Descriptor = "industrial town pride project", Nationality = "de", ModelId = "d-12", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Müller Sport", Descriptor = "German precision on a modest budget", Nationality = "de", ModelId = "d-13", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Park Ferme Racing", Descriptor = "obsessive about procedure and prep", Nationality = "fr", ModelId = "d-14", CarClass = CarClass.D },
            new AiTeamEntry { Name = "Ravenhill Autosport", Descriptor = "Northern Irish grit, southern ambition", Nationality = "gb", ModelId = "d-15", CarClass = CarClass.D },
            // CLASS C: 15 teams
            new AiTeamEntry { Name = "Team Tora Racing", Descriptor = "Tora's official touring/GT squad", Nationality = "jp", ModelId = "c-18", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Tora GT Works", Descriptor = "Tora's factory GT programme", Nationality = "jp", ModelId = "c-20", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Greystone Archer", Descriptor = "Archer-supported independent squad", Nationality = "gb", ModelId = "c-16", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Golf Course Endurance", Descriptor = "heritage oil brand's flagship endurance entry", Nationality = "us", ModelId = "c-17", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Petrol Mush Racing", Descriptor = "fuel sponsor backing a scrappy sports car team", Nationality = "my", ModelId = "c-19", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Prodigy Racing", Descriptor = "feeder-series talent factory gone GT", Nationality = "it", ModelId = "c-16", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Canvas Grand Prix", Descriptor = "elite junior formula team stepping up", Nationality = "fr", ModelId = "c-17", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Agile 1 Motorsport", Descriptor = "technical sponsor turned team owner", Nationality = "dk", ModelId = "c-18", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Full ERS Racing", Descriptor = "fuel company's professional GT outfit", Nationality = "jp", ModelId = "c-19", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Lo-fi Racing", Descriptor = "engineering-first operation with junior pedigree", Nationality = "fr", ModelId = "c-20", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Maestro Racing", Descriptor = "polished operation punching into the pro ranks", Nationality = "it", ModelId = "c-16", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Celine Racing", Descriptor = "top-tier feeder series outfit stepping into GT", Nationality = "fr", ModelId = "c-17", CarClass = CarClass.C },
            new AiTeamEntry { Name = "TRAMS", Descriptor = "storied French racing operation", Nationality = "fr", ModelId = "c-18", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Alien Power Motorsport", Descriptor = "energy drink-sponsored privateer with deep pockets", Nationality = "us", ModelId = "c-19", CarClass = CarClass.C },
            new AiTeamEntry { Name = "Barnacle Racing", Descriptor = "fuel giant's long-running works partnership", Nationality = "nl", ModelId = "c-20", CarClass = CarClass.C },
            // CLASS B: 12 teams
            new AiTeamEntry { Name = "Scuderia Feretti", Descriptor = "Feretti's official works racing team", Nationality = "it", ModelId = "b-21", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Team Archer", Descriptor = "Archer's performance division works entry", Nationality = "gb", ModelId = "b-22", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Liberty Performance Racing", Descriptor = "Liberty's factory GT programme", Nationality = "us", ModelId = "b-25", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Molteni Corse", Descriptor = "Feretti customer team, Italian privateer", Nationality = "it", ModelId = "b-21", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Apex Archer", Descriptor = "Archer satellite with factory parts supply", Nationality = "ie", ModelId = "b-23", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Stonebridge Kaiser", Descriptor = "British-run Kaiser customer racing outfit", Nationality = "gb", ModelId = "b-24", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Sip Vanessa Racing", Descriptor = "veteran-run multi-series operation", Nationality = "br", ModelId = "b-22", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Vespro Corse", Descriptor = "Italian GT privateer powerhouse", Nationality = "it", ModelId = "b-23", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Green Goat Racing", Descriptor = "energy drink empire's factory racing arm", Nationality = "cn", ModelId = "b-24", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Vosper Liberty Racing", Descriptor = "Liberty-backed privateer with works engines", Nationality = "us", ModelId = "b-25", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Tsubame Racing", Descriptor = "Japanese motorcycle giant's four-wheel factory effort", Nationality = "jp", ModelId = "b-21", CarClass = CarClass.B },
            new AiTeamEntry { Name = "Zugzwang Factory Racing", Descriptor = "Austrian works team with two-wheel pedigree", Nationality = "at", ModelId = "b-22", CarClass = CarClass.B },
            // CLASS A: 12 teams
            new AiTeamEntry { Name = "Archer Prototype Works", Descriptor = "Archer's factory endurance programme", Nationality = "de", ModelId = "a-27", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Kaiser Motorsport", Descriptor = "Kaiser's flagship prototype operation", Nationality = "at", ModelId = "a-26", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Kaiser Dynamo Racing", Descriptor = "Kaiser's factory LMP effort", Nationality = "de", ModelId = "a-28", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Team Seorak", Descriptor = "Seorak's global endurance works team", Nationality = "kr", ModelId = "a-29", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Liberty Frontier", Descriptor = "Liberty's works prototype team", Nationality = "us", ModelId = "a-30", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Panski Autosport", Descriptor = "multi-discipline racing dynasty", Nationality = "us", ModelId = "a-26", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Fiorini Motorsport", Descriptor = "expanding privateer empire", Nationality = "it", ModelId = "a-27", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Angsty Racing", Descriptor = "Swiss-bankrolled endurance privateer", Nationality = "ch", ModelId = "a-28", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Fairbairn Racing", Descriptor = "historic family-owned racing team", Nationality = "gb", ModelId = "a-29", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Zurlinden Motorsport", Descriptor = "Swiss engineering outfit with decades of pedigree", Nationality = "ch", ModelId = "a-30", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Saetta Corse", Descriptor = "Italian motorcycle powerhouse dominating on four wheels", Nationality = "it", ModelId = "a-26", CarClass = CarClass.A },
            new AiTeamEntry { Name = "Brezza Racing", Descriptor = "Italian factory team with MotoGP heritage", Nationality = "it", ModelId = "a-27", CarClass = CarClass.A },
        };
    }
}
