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
  // TODO: 20 teams TBD

  // -----------------------------------------------------------------------
  // CLASS E — 20 teams
  // -----------------------------------------------------------------------
  // TODO: 20 teams TBD

  // -----------------------------------------------------------------------
  // CLASS D — 20 teams
  // -----------------------------------------------------------------------
  { name: "Rubasu Technica",              descriptor: "Rubasu's rally-bred factory effort", carClass: "D" },
  { name: "Amazing Wireless Services",    descriptor: "tech-money privateer, data-obsessed", carClass: "D" },
  { name: "Petrol Mush Racing",           descriptor: "fuel sponsor backing a scrappy sports car team", carClass: "D" },
  { name: "Poseidon Racing",              descriptor: "trident-bearing privateer with sharp ambitions", carClass: "D" },
  { name: "Virginia Motorsport",          descriptor: "French-founded privateer on the rise", carClass: "D" },
  { name: "Insight Racing",               descriptor: "tech-backed team chasing efficiency gains", carClass: "D" },
  { name: "Phoenix Autosport",            descriptor: "always bouncing back from the brink", carClass: "D" },
  { name: "DHM Logistics Racing",         descriptor: "corporate-backed mid-tier squad", carClass: "D" },
  { name: "Carol Oil Autosport",          descriptor: "lubricant-sponsored touring car operation", carClass: "D" },
  { name: "Compost Racing",               descriptor: "scrappy but legitimate single-seater graduates", carClass: "D" },
  { name: "ML Motorsport",                descriptor: "Dutch-run team with an eye on the podium", carClass: "D" },
  // TODO: 9 teams TBD

  // -----------------------------------------------------------------------
  // CLASS C — 15 teams
  // -----------------------------------------------------------------------
  { name: "Team Hando Racing",            descriptor: "Hando's official touring/GT squad", carClass: "C" },
  { name: "Ichisan Racing",               descriptor: "Ichisan's factory GT programme", carClass: "C" },
  { name: "Greystone Maecides OMG",       descriptor: "Maecides-supported independent squad", carClass: "C" },
  { name: "Vosper Fuud Racing",           descriptor: "Fuud-backed privateer with works engines", carClass: "C" },
  { name: "KNS Tonata",                   descriptor: "Tonata's Southeast Asian satellite programme", carClass: "C" },
  { name: "Premier Racing",               descriptor: "feeder-series talent factory gone GT", carClass: "C" },
  { name: "CRAFT Grand Prix",             descriptor: "elite junior formula team stepping up", carClass: "C" },
  { name: "Agile 1 Motorsport",           descriptor: "technical sponsor turned team owner", carClass: "C" },
  { name: "Complete Energy Racing",        descriptor: "fuel company's professional GT outfit", carClass: "C" },
  { name: "High Tech Racing",             descriptor: "engineering-first operation with junior pedigree", carClass: "C" },
  { name: "Virtuoso Racing",              descriptor: "polished operation punching into the pro ranks", carClass: "C" },
  { name: "Carline Racing",               descriptor: "top-tier feeder series outfit stepping into GT", carClass: "C" },
  { name: "TAMS",                         descriptor: "storied French racing operation", carClass: "C" },
  { name: "Alien Power Motorsport",       descriptor: "energy drink-sponsored privateer with deep pockets", carClass: "C" },
  { name: "Seashell Racing",              descriptor: "fuel giant's long-running works partnership", carClass: "C" },

  // -----------------------------------------------------------------------
  // CLASS B — 12 teams
  // -----------------------------------------------------------------------
  { name: "Scuderia Feretti",             descriptor: "Feretti's official works racing team", carClass: "B" },
  { name: "Maecides-OMG Team",            descriptor: "Maecides' performance division works entry", carClass: "B" },
  { name: "Fuud Performance Racing",      descriptor: "Fuud's factory GT programme", carClass: "B" },
  { name: "Molteni Corse",                descriptor: "Feretti customer team, Italian privateer", carClass: "B" },
  { name: "Apex McRaven",                 descriptor: "McRaven satellite with factory parts supply", carClass: "B" },
  { name: "Stonebridge Rocher",           descriptor: "British-run Rocher customer racing outfit", carClass: "B" },
  { name: "Sip Vanessa Racing",           descriptor: "veteran-run multi-series operation", carClass: "B" },
  { name: "BG Corsa",                     descriptor: "Italian GT privateer powerhouse", carClass: "B" },
  { name: "Blue Cow Racing",              descriptor: "energy drink empire's factory racing arm", carClass: "B" },
  { name: "Golf Endurance",               descriptor: "heritage oil brand's flagship endurance entry", carClass: "B" },
  { name: "Yamada Racing",                descriptor: "Japanese motorcycle giant's four-wheel factory effort", carClass: "B" },
  { name: "KRM Factory Racing",           descriptor: "Austrian works team with two-wheel pedigree", carClass: "B" },

  // -----------------------------------------------------------------------
  // CLASS A — 12 teams
  // -----------------------------------------------------------------------
  { name: "McRaven Racing",               descriptor: "McRaven's factory endurance programme", carClass: "A" },
  { name: "Rocher Motorsport",            descriptor: "Rocher's flagship prototype operation", carClass: "A" },
  { name: "Nodi Sport Team",              descriptor: "Nodi's factory LMP effort", carClass: "A" },
  { name: "Team Tonata",                  descriptor: "Tonata's global endurance works team", carClass: "A" },
  { name: "Bureau Sport",                 descriptor: "Bureau's works prototype team", carClass: "A" },
  { name: "Panski Autosport",             descriptor: "multi-discipline racing dynasty", carClass: "A" },
  { name: "Avetti Motorsport",            descriptor: "expanding privateer empire", carClass: "A" },
  { name: "Revolution Racing",            descriptor: "Swiss-bankrolled endurance privateer", carClass: "A" },
  { name: "Williamson Racing",            descriptor: "historic family-owned racing team", carClass: "A" },
  { name: "Saaber Motorsport",            descriptor: "Swiss engineering outfit with decades of pedigree", carClass: "A" },
  { name: "Fukati Corse",                 descriptor: "Italian motorcycle powerhouse dominating on four wheels", carClass: "A" },
  { name: "Aprolla Racing",               descriptor: "Italian factory team with MotoGP heritage", carClass: "A" },
];
