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
  { name: "Amazing Wild Services",        descriptor: "tech-money privateer, data-obsessed", carClass: "D" },
  { name: "Petrol Mush Racing",           descriptor: "fuel sponsor backing a scrappy sports car team", carClass: "D" },
  // TODO: 17 teams TBD

  // -----------------------------------------------------------------------
  // CLASS C — 15 teams
  // -----------------------------------------------------------------------
  { name: "Team Hando Racing",           descriptor: "Hando's official touring/GT squad", carClass: "C" },
  { name: "Ichisan Racing",              descriptor: "Ichisan's factory GT programme", carClass: "C" },
  { name: "Greystone Maecides OMG",      descriptor: "Maecides-supported independent squad", carClass: "C" },
  { name: "Vosper Fuud Racing",          descriptor: "Fuud-backed privateer with works engines", carClass: "C" },
  { name: "KNS Tonata",                  descriptor: "Tonata's Southeast Asian satellite programme", carClass: "C" },
  { name: "Premier Racing",              descriptor: "feeder-series talent factory gone GT", carClass: "C" },
  { name: "CRAFT Grand Prix",            descriptor: "elite junior formula team stepping up", carClass: "C" },
  { name: "Carol Oil Autosport",         descriptor: "lubricant-sponsored touring car operation", carClass: "C" },
  { name: "Agile 1 Motorsport",          descriptor: "technical sponsor turned team owner", carClass: "C" },
  { name: "Complete Energy Racing",      descriptor: "fuel company's professional GT outfit", carClass: "C" },
  { name: "DHM Logistics Racing",        descriptor: "corporate-backed mid-tier squad", carClass: "C" },
  // TODO: 4 teams TBD

  // -----------------------------------------------------------------------
  // CLASS B — 12 teams
  // -----------------------------------------------------------------------
  { name: "Scuderia Feretti",            descriptor: "Feretti's official works racing team", carClass: "B" },
  { name: "Maecides-AMG Team",           descriptor: "Maecides' performance division works entry", carClass: "B" },
  { name: "Fuud Performance Racing",     descriptor: "Fuud's factory GT programme", carClass: "B" },
  { name: "Molteni Corse",               descriptor: "Feretti customer team, Italian privateer", carClass: "B" },
  { name: "Apex McRaven",                descriptor: "McRaven satellite with factory parts supply", carClass: "B" },
  { name: "Stonebridge Rocher",          descriptor: "British-run Rocher customer racing outfit", carClass: "B" },
  { name: "Sip Vanessa Racing",          descriptor: "veteran-run multi-series operation", carClass: "B" },
  { name: "BG Corsa",                    descriptor: "Italian GT privateer powerhouse", carClass: "B" },
  { name: "Alien Power Motorsport",      descriptor: "energy drink-sponsored privateer with deep pockets", carClass: "B" },
  { name: "Seashell Racing",             descriptor: "fuel giant's long-running works partnership", carClass: "B" },
  // TODO: 2 teams TBD

  // -----------------------------------------------------------------------
  // CLASS A — 12 teams
  // -----------------------------------------------------------------------
  { name: "McRaven Racing",              descriptor: "McRaven's factory endurance programme", carClass: "A" },
  { name: "Rocher Motorsport",           descriptor: "Rocher's flagship prototype operation", carClass: "A" },
  { name: "Nodi Sport Team",             descriptor: "Nodi's factory LMP effort", carClass: "A" },
  { name: "Team Tonata",                 descriptor: "Tonata's global endurance works team", carClass: "A" },
  { name: "Bureau Sport",                descriptor: "Bureau's works prototype team", carClass: "A" },
  { name: "Panski Autosport",            descriptor: "multi-discipline racing dynasty", carClass: "A" },
  { name: "Avetti Motorsport",           descriptor: "expanding privateer empire", carClass: "A" },
  { name: "Revolution Racing",           descriptor: "Swiss-bankrolled endurance privateer", carClass: "A" },
  { name: "Blue Cow Racing",             descriptor: "energy drink empire's factory racing arm", carClass: "A" },
  { name: "Golf Endurance",              descriptor: "heritage oil brand's flagship endurance entry", carClass: "A" },
  // TODO: 2 teams TBD
];
