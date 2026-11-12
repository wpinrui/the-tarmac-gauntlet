import type { CarClass } from "../types/car";

export interface AiTeamEntry {
  name: string;
  descriptor: string;
  carClass: CarClass;
}

/**
 * 99 pre-authored AI team identities, distributed across classes to match
 * the tier counts in gameInit (F:20, E:20, D:20, C:15, B:12, A:12).
 *
 * Teams are ordered by class so gameInit can assign them sequentially
 * as it iterates through AI_TIER_SPECS.
 */
export const AI_TEAM_ROSTER: AiTeamEntry[] = [
  // -----------------------------------------------------------------------
  // CLASS F — 20 teams
  // -----------------------------------------------------------------------
  { name: "Backfire Racing",              descriptor: "three mates and a trailer", carClass: "F" },
  { name: "Shed Speed Co.",               descriptor: "built in an actual garden shed", carClass: "F" },
  { name: "Clutch & Pray",                descriptor: "optimistic underdogs", carClass: "F" },
  { name: "Spare Parts Racing",            descriptor: "the car is mostly replacement bits", carClass: "F" },
  { name: "Gravel Trap Racing",           descriptor: "frequent visitors to the run-off area", carClass: "F" },
  { name: "Duct Tape Dynamics",           descriptor: "structural integrity is a suggestion", carClass: "F" },
  { name: "Flatbed Legends",              descriptor: "arrive on a flatbed, leave on a flatbed", carClass: "F" },
  { name: "No Insurance Motorsport",      descriptor: "living dangerously in every sense", carClass: "F" },
  { name: "Oily Rag Racing",              descriptor: "one rag, two spanners, zero budget", carClass: "F" },
  { name: "Horizon Motorsport",           descriptor: "eyes on the distance, not the standings", carClass: "F" },
  { name: "Ironbark Racing",              descriptor: "Australian hardwood — won't snap", carClass: "F" },
  { name: "Mapleton Autosport",           descriptor: "small-town team with real ambition", carClass: "F" },
  { name: "Vega Racing",                  descriptor: "named after the brightest star they could see", carClass: "F" },
  { name: "First Gear Motorsport",        descriptor: "everyone starts somewhere", carClass: "F" },
  { name: "Saltire Racing",               descriptor: "Scottish pride on a shoestring", carClass: "F" },
  { name: "Croft & Davis",                descriptor: "two founders, one rusted car", carClass: "F" },
  { name: "Long Road Racing",             descriptor: "long way to go, willing to drive it", carClass: "F" },
  { name: "Pemberton Motorsport",         descriptor: "family name on the door, heart on the sleeve", carClass: "F" },
  { name: "Basecamp Autosport",           descriptor: "starting from the bottom, climbing up", carClass: "F" },
  { name: "Cornerstone Racing",           descriptor: "building something from nothing", carClass: "F" },

  // -----------------------------------------------------------------------
  // CLASS E — 20 teams
  // -----------------------------------------------------------------------
  { name: "Dockyard Racing",              descriptor: "from slipway to fast lane", carClass: "E" },
  { name: "Whitfield & Sons",             descriptor: "father-and-sons operation since forever", carClass: "E" },
  { name: "Copper Lane Autosport",        descriptor: "named after the street they wrench on", carClass: "E" },
  { name: "North Star Racing",            descriptor: "Steady as the pole star", carClass: "E" },
  { name: "Terrace Motorsport",           descriptor: "working-class fan-funded team", carClass: "E" },
  { name: "Gasworks Racing",              descriptor: "smells like petrol, runs like clockwork", carClass: "E" },
  { name: "Southgate Motorsport",         descriptor: "suburban team, real commitment", carClass: "E" },
  { name: "Barnstormer Racing",           descriptor: "rural team, loud cars, no apologies", carClass: "E" },
  { name: "Overtime Racing",              descriptor: "day jobs fund the weekend habit", carClass: "E" },
  { name: "Vogel Racing",               descriptor: "German-founded privateer with discipline", carClass: "E" },
  { name: "Harrow Motorsport",            descriptor: "west London outfit, punching up", carClass: "E" },
  { name: "Caldwell Racing",              descriptor: "veteran driver turned team owner", carClass: "E" },
  { name: "Half Tank Racing",             descriptor: "never quite enough fuel budget", carClass: "E" },
  { name: "Blackthorn Racing",            descriptor: "thorny, stubborn, hard to pass", carClass: "E" },
  { name: "Cóndor Racing",           descriptor: "Colombian-founded, passionate about racing", carClass: "E" },
  { name: "Pit Lane Dreamers",            descriptor: "small team with big ambitions", carClass: "E" },
  { name: "Three Pedals Racing",          descriptor: "manual gearbox purists", carClass: "E" },
  { name: "Overpass Racing",              descriptor: "headquartered under a motorway bridge", carClass: "E" },
  { name: "Sato Brothers Racing",         descriptor: "Japanese siblings building from scratch", carClass: "E" },
  { name: "Yellow Flag Motorsport",       descriptor: "cautious but surprisingly consistent", carClass: "E" },

  // -----------------------------------------------------------------------
  // CLASS D — 20 teams
  // -----------------------------------------------------------------------
  { name: "Seorak Technica",              descriptor: "Seorak's rally-bred factory effort", carClass: "D" },
  { name: "Packet Loss Racing",    descriptor: "tech-money privateer, data-obsessed", carClass: "D" },
  { name: "Petrol Mush Racing",           descriptor: "fuel sponsor backing a scrappy sports car team", carClass: "D" },
  { name: "Poseidon Racing",              descriptor: "trident-bearing privateer with sharp ambitions", carClass: "D" },
  { name: "Virginia Motorsport",          descriptor: "French-founded privateer on the rise", carClass: "D" },
  { name: "Insight Racing",               descriptor: "tech-backed team chasing efficiency gains", carClass: "D" },
  { name: "Ember Racing",            descriptor: "always bouncing back from the brink", carClass: "D" },
  { name: "Freightline Racing",         descriptor: "corporate-backed mid-tier squad", carClass: "D" },
  { name: "Gearbox Grease Racing",          descriptor: "lubricant-sponsored touring car operation", carClass: "D" },
  { name: "Compost Racing",               descriptor: "scrappy but legitimate single-seater graduates", carClass: "D" },
  { name: "Van Dijk Racing",                descriptor: "Dutch-run team with an eye on the podium", carClass: "D" },
  { name: "Thornton Motorsport",          descriptor: "British family-run sports car team", carClass: "D" },
  { name: "Brackley Engineering",         descriptor: "ex-factory engineers gone independent", carClass: "D" },
  { name: "Nakamura Racing",              descriptor: "Japanese privateer with touge roots", carClass: "D" },
  { name: "Red Anvil Autosport",          descriptor: "heavy-handed but fast", carClass: "D" },
  { name: "Costa Brava Racing",           descriptor: "Spanish sun-belt outfit", carClass: "D" },
  { name: "Steelworks Motorsport",        descriptor: "industrial town pride project", carClass: "D" },
  { name: "Müller Sport",                 descriptor: "German precision on a modest budget", carClass: "D" },
  { name: "Park Ferme Racing",            descriptor: "obsessive about procedure and prep", carClass: "D" },
  { name: "Ravenhill Autosport",          descriptor: "Northern Irish grit, southern ambition", carClass: "D" },

  // -----------------------------------------------------------------------
  // CLASS C — 15 teams
  // -----------------------------------------------------------------------
  { name: "Team Tora Racing",            descriptor: "Tora's official touring/GT squad", carClass: "C" },
  { name: "Tora GT Works",               descriptor: "Tora's factory GT programme", carClass: "C" },
  { name: "Greystone Archer",       descriptor: "Archer-supported independent squad", carClass: "C" },
  { name: "Vosper Liberty Racing",           descriptor: "Liberty-backed privateer with works engines", carClass: "C" },
  { name: "KNS Seorak",                   descriptor: "Seorak's Southeast Asian satellite programme", carClass: "C" },
  { name: "Prodigy Racing",               descriptor: "feeder-series talent factory gone GT", carClass: "C" },
  { name: "Canvas Grand Prix",             descriptor: "elite junior formula team stepping up", carClass: "C" },
  { name: "Agile 1 Motorsport",           descriptor: "technical sponsor turned team owner", carClass: "C" },
  { name: "Full ERS Racing",        descriptor: "fuel company's professional GT outfit", carClass: "C" },
  { name: "Lo-fi Racing",             descriptor: "engineering-first operation with junior pedigree", carClass: "C" },
  { name: "Maestro Racing",              descriptor: "polished operation punching into the pro ranks", carClass: "C" },
  { name: "Celine Racing",               descriptor: "top-tier feeder series outfit stepping into GT", carClass: "C" },
  { name: "TRAMS",                         descriptor: "storied French racing operation", carClass: "C" },
  { name: "Alien Power Motorsport",       descriptor: "energy drink-sponsored privateer with deep pockets", carClass: "C" },
  { name: "Barnacle Racing",              descriptor: "fuel giant's long-running works partnership", carClass: "C" },

  // -----------------------------------------------------------------------
  // CLASS B — 12 teams
  // -----------------------------------------------------------------------
  { name: "Scuderia Feretti",             descriptor: "Feretti's official works racing team", carClass: "B" },
  { name: "Team Archer",            descriptor: "Archer's performance division works entry", carClass: "B" },
  { name: "Liberty Performance Racing",      descriptor: "Liberty's factory GT programme", carClass: "B" },
  { name: "Molteni Corse",                descriptor: "Feretti customer team, Italian privateer", carClass: "B" },
  { name: "Apex Archer",                 descriptor: "Archer satellite with factory parts supply", carClass: "B" },
  { name: "Stonebridge Kaiser",           descriptor: "British-run Kaiser customer racing outfit", carClass: "B" },
  { name: "Sip Vanessa Racing",           descriptor: "veteran-run multi-series operation", carClass: "B" },
  { name: "Vespro Corse",                     descriptor: "Italian GT privateer powerhouse", carClass: "B" },
  { name: "Green Goat Racing",              descriptor: "energy drink empire's factory racing arm", carClass: "B" },
  { name: "Golf Course Endurance",               descriptor: "heritage oil brand's flagship endurance entry", carClass: "B" },
  { name: "Tsubame Racing",                descriptor: "Japanese motorcycle giant's four-wheel factory effort", carClass: "B" },
  { name: "Zugzwang Factory Racing",           descriptor: "Austrian works team with two-wheel pedigree", carClass: "B" },

  // -----------------------------------------------------------------------
  // CLASS A — 12 teams
  // -----------------------------------------------------------------------
  { name: "Archer Prototype Works",               descriptor: "Archer's factory endurance programme", carClass: "A" },
  { name: "Kaiser Motorsport",            descriptor: "Kaiser's flagship prototype operation", carClass: "A" },
  { name: "Kaiser Dynamo Racing",              descriptor: "Kaiser's factory LMP effort", carClass: "A" },
  { name: "Team Seorak",                  descriptor: "Seorak's global endurance works team", carClass: "A" },
  { name: "Liberty Frontier",                 descriptor: "Liberty's works prototype team", carClass: "A" },
  { name: "Panski Autosport",             descriptor: "multi-discipline racing dynasty", carClass: "A" },
  { name: "Fiorini Motorsport",            descriptor: "expanding privateer empire", carClass: "A" },
  { name: "Angsty Racing",            descriptor: "Swiss-bankrolled endurance privateer", carClass: "A" },
  { name: "Fairbairn Racing",            descriptor: "historic family-owned racing team", carClass: "A" },
  { name: "Zurlinden Motorsport",            descriptor: "Swiss engineering outfit with decades of pedigree", carClass: "A" },
  { name: "Saetta Corse",                 descriptor: "Italian motorcycle powerhouse dominating on four wheels", carClass: "A" },
  { name: "Brezza Racing",               descriptor: "Italian factory team with MotoGP heritage", carClass: "A" },
];
