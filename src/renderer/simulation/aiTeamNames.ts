import type { CarClass } from "../types/car";

export interface AiTeamEntry {
  name: string;
  descriptor: string;
  carClass: CarClass;
}

/**
 * 99 pre-authored AI team identities, distributed across classes to match
 * the tier counts in gameInit (F:20, E:20, D:20, C:15, B:12, A:8, F1:4).
 *
 * Teams are ordered by class so gameInit can assign them sequentially
 * as it iterates through AI_TIER_SPECS.
 */
export const AI_TEAM_ROSTER: AiTeamEntry[] = [
  // -----------------------------------------------------------------------
  // CLASS F — 20 teams (shoestring budgets, junkyard warriors)
  // -----------------------------------------------------------------------
  { name: "Backfire Racing",            descriptor: "three mates and a trailer", carClass: "F" },
  { name: "Rusty Bucket Motorsport",    descriptor: "junkyard rescue specialists", carClass: "F" },
  { name: "Sunday Senders",             descriptor: "weekend warriors with nothing to lose", carClass: "F" },
  { name: "Dead Last Autosport",        descriptor: "proud owners of rock bottom expectations", carClass: "F" },
  { name: "Kerb Monkey Racing",         descriptor: "enthusiastic corner-cutters", carClass: "F" },
  { name: "Spit & Polish Motorsport",   descriptor: "held together by zip ties and optimism", carClass: "F" },
  { name: "Gasket Posse",               descriptor: "blown head gaskets are a lifestyle", carClass: "F" },
  { name: "Shed Speed Co.",             descriptor: "built in an actual garden shed", carClass: "F" },
  { name: "Spare Parts FC",             descriptor: "the car is mostly replacement bits", carClass: "F" },
  { name: "Throttle Monkeys",           descriptor: "all gas, no brakes, no plan", carClass: "F" },
  { name: "Discount Motorsport",        descriptor: "bargain-bin racing on a beer budget", carClass: "F" },
  { name: "Banger Bros Racing",         descriptor: "demolition derby graduates", carClass: "F" },
  { name: "Clutch & Pray",              descriptor: "optimistic underdogs", carClass: "F" },
  { name: "Oily Rag Racing",            descriptor: "one rag, two spanners, zero budget", carClass: "F" },
  { name: "Folksbarrow Irregulars",     descriptor: "unofficial fan team of the people's brand", carClass: "F" },
  { name: "Chicane Chasers",            descriptor: "perpetually one lap behind", carClass: "F" },
  { name: "Fuud Runners",               descriptor: "sponsored by leftover Fuud dealer goodwill", carClass: "F" },
  { name: "Gravel Trap Racing",         descriptor: "frequent visitors to the run-off area", carClass: "F" },
  { name: "Petrol Fumes FC",            descriptor: "they can't afford a garage, just a car park", carClass: "F" },
  { name: "Last Chance Autosport",      descriptor: "one more DNF and they're done", carClass: "F" },

  // -----------------------------------------------------------------------
  // CLASS E — 20 teams (modest budgets, small operations, daily-driver tier)
  // -----------------------------------------------------------------------
  { name: "Tonata Club Racing",         descriptor: "dealer-backed weekend racers", carClass: "E" },
  { name: "Grid Filler Motorsport",     descriptor: "reliable mid-pack presence", carClass: "E" },
  { name: "Apex Amateurs",              descriptor: "enthusiasts punching above their weight", carClass: "E" },
  { name: "Fuud Sport Junior",          descriptor: "Fuud's grassroots development squad", carClass: "E" },
  { name: "Hando Lapping Club",         descriptor: "track day regulars gone competitive", carClass: "E" },
  { name: "Pit Lane Dreamers",          descriptor: "small team with big ambitions", carClass: "E" },
  { name: "Curbside Racing",            descriptor: "neighbourhood mechanics turned racers", carClass: "E" },
  { name: "Folksbarrow Touring Club",   descriptor: "community-run Folksbarrow enthusiasts", carClass: "E" },
  { name: "Oversteer United",           descriptor: "they slide, they survive, they score points", carClass: "E" },
  { name: "Nodi Grassroots",            descriptor: "Nodi's cheapest models, maximum effort", carClass: "E" },
  { name: "Tarmac Terriers",            descriptor: "scrappy and tenacious on any surface", carClass: "E" },
  { name: "Parc Fermé Racing",          descriptor: "obsessive about procedure and prep", carClass: "E" },
  { name: "Bureau Economy Run",         descriptor: "fuel-sipping Bureau sedans on a budget", carClass: "E" },
  { name: "Rolling Start Autosport",    descriptor: "always ready, never fast", carClass: "E" },
  { name: "Ichisan Touring",            descriptor: "steady, dependable Ichisan operation", carClass: "E" },
  { name: "Grassroots GP",              descriptor: "community-funded entry-level team", carClass: "E" },
  { name: "Hubcap Heroes",             descriptor: "they lost a wheel once and still finished", carClass: "E" },
  { name: "Rubasu Club Sport",          descriptor: "Rubasu owners club turned race team", carClass: "E" },
  { name: "Sunday Slipstreamers",       descriptor: "draft kings of the back of the grid", carClass: "E" },
  { name: "Midfield Motorsport",        descriptor: "permanent residents of the middle pack", carClass: "E" },

  // -----------------------------------------------------------------------
  // CLASS D — 20 teams (enthusiast tier, semi-serious sports car operations)
  // -----------------------------------------------------------------------
  { name: "Hando Sport Division",       descriptor: "factory-adjacent enthusiast programme", carClass: "D" },
  { name: "Rubasu Performance Club",    descriptor: "tuned Rubasu builds with real pace", carClass: "D" },
  { name: "Redline Racing",             descriptor: "always at the limiter, always committed", carClass: "D" },
  { name: "Torque Syndicate",           descriptor: "dyno-obsessed power chasers", carClass: "D" },
  { name: "Canyon Carvers",             descriptor: "touge graduates on a real track", carClass: "D" },
  { name: "Lightfoot Autosport",        descriptor: "low weight, high corner speed", carClass: "D" },
  { name: "Folksbarrow Sport",          descriptor: "tuned hot hatches with factory blessing", carClass: "D" },
  { name: "Hardtop Racing",             descriptor: "no convertibles, no compromise", carClass: "D" },
  { name: "Nodi Sprint Team",           descriptor: "Nodi's lightweight sports car operation", carClass: "D" },
  { name: "Apex Predators",             descriptor: "corner entry is a religion", carClass: "D" },
  { name: "Fuud ST Racing",             descriptor: "Fuud's hot hatch competition arm", carClass: "D" },
  { name: "Ichisan Drift Works",        descriptor: "sideways Ichisan specialists gone circuit", carClass: "D" },
  { name: "Paddock Rats",               descriptor: "they live at the track, literally", carClass: "D" },
  { name: "Slipstream Autosport",       descriptor: "smart racecraft, modest machinery", carClass: "D" },
  { name: "Hairpin Racing",             descriptor: "tight-circuit specialists", carClass: "D" },
  { name: "Bureau Sport Tuning",        descriptor: "Bureau daily drivers made surprisingly fast", carClass: "D" },
  { name: "Heel-Toe Motorsport",        descriptor: "old-school technique, modern ambition", carClass: "D" },
  { name: "Rising Sun Racing",          descriptor: "Japanese-car fanatics with proper setups", carClass: "D" },
  { name: "Chicane Racing Collective",  descriptor: "driver co-op sharing one garage", carClass: "D" },
  { name: "Tonata GT Academy",          descriptor: "Tonata's feeder programme for young talent", carClass: "D" },

  // -----------------------------------------------------------------------
  // CLASS C — 15 teams (professional, competitive, well-resourced)
  // -----------------------------------------------------------------------
  { name: "Maecides AMG Racing",        descriptor: "Maecides' semi-works touring car squad", carClass: "C" },
  { name: "Nodi Performance Team",      descriptor: "factory-supported GT programme", carClass: "C" },
  { name: "Rocher Sport",               descriptor: "Rocher's customer racing division", carClass: "C" },
  { name: "Apex Alliance",              descriptor: "multi-car professional outfit", carClass: "C" },
  { name: "Hando R Performance",        descriptor: "Hando's Type R racing programme", carClass: "C" },
  { name: "Blackflag Motorsport",       descriptor: "aggressive tactics, fast machinery", carClass: "C" },
  { name: "Velocity Works",             descriptor: "engineering-led independent team", carClass: "C" },
  { name: "Ichisan Nismo Racing",       descriptor: "Ichisan's performance brand on track", carClass: "C" },
  { name: "Grid Iron Racing",           descriptor: "disciplined, consistent point scorers", carClass: "C" },
  { name: "Trident Motorsport",         descriptor: "three-car professional entry", carClass: "C" },
  { name: "Carbon Republic",            descriptor: "lightweight obsessives with a real budget", carClass: "C" },
  { name: "Pitwall Racing",             descriptor: "data-driven strategy, solid cars", carClass: "C" },
  { name: "Bureau RS Division",         descriptor: "Bureau's rally-sport pedigree on tarmac", carClass: "C" },
  { name: "Folksbarrow R Motorsport",   descriptor: "Folksbarrow's racing division, properly funded", carClass: "C" },
  { name: "Neutrino Electric Sport",    descriptor: "Neutrino's hybrid tech showcase team", carClass: "C" },

  // -----------------------------------------------------------------------
  // CLASS B — 12 teams (serious racing, sponsor-backed, supercar territory)
  // -----------------------------------------------------------------------
  { name: "Feretti Corse",              descriptor: "Feretti's official competition department", carClass: "B" },
  { name: "McRaven Racing",             descriptor: "McRaven's factory-backed GT programme", carClass: "B" },
  { name: "Maecides GT Team",           descriptor: "Maecides works entry in GT racing", carClass: "B" },
  { name: "Rocher Motorsport",          descriptor: "Rocher's flagship endurance squad", carClass: "B" },
  { name: "Vanguard Racing",            descriptor: "independent supercar racing powerhouse", carClass: "B" },
  { name: "Apex GT",                    descriptor: "professional GT team with title pedigree", carClass: "B" },
  { name: "Fuud Performance Racing",    descriptor: "Fuud's flagship competition programme", carClass: "B" },
  { name: "Tempest Motorsport",         descriptor: "sponsor-rich privateer with factory hardware", carClass: "B" },
  { name: "Ironside Racing",            descriptor: "veteran-run team, built on consistency", carClass: "B" },
  { name: "Silverstone Autosport",      descriptor: "heritage privateer with decades of GT racing", carClass: "B" },
  { name: "Neutrino GT Works",          descriptor: "Neutrino's high-performance competition arm", carClass: "B" },
  { name: "Hando NSX Programme",        descriptor: "Hando's supercar factory effort", carClass: "B" },

  // -----------------------------------------------------------------------
  // CLASS A — 8 teams (factory-backed hypercar programmes, major operations)
  // -----------------------------------------------------------------------
  { name: "Rocher Hypercar Team",       descriptor: "Rocher's flagship hypercar programme", carClass: "A" },
  { name: "Feretti Endurance",          descriptor: "Feretti's Le Mans prototype operation", carClass: "A" },
  { name: "McRaven Hypercar",           descriptor: "McRaven's top-tier factory effort", carClass: "A" },
  { name: "Tonata Gazoo Racing",        descriptor: "Tonata's global endurance racing works team", carClass: "A" },
  { name: "Bureau Hypercar Sport",      descriptor: "Bureau's return to top-level endurance racing", carClass: "A" },
  { name: "Maecides Hypercar Programme", descriptor: "Maecides' full-works prototype entry", carClass: "A" },
  { name: "Neutrino LMP",              descriptor: "Neutrino's electric-hybrid prototype programme", carClass: "A" },
  { name: "Vanguard Endurance",         descriptor: "privateer hypercar team with serious backing", carClass: "A" },

  // -----------------------------------------------------------------------
  // CLASS F1 — 4 teams (elite manufacturer works teams)
  // -----------------------------------------------------------------------
  { name: "Scuderia Feretti",           descriptor: "the most storied name in single-seater racing", carClass: "F1" },
  { name: "McRaven Formula",            descriptor: "McRaven's pinnacle open-wheel programme", carClass: "F1" },
  { name: "Neutrino F1 Team",           descriptor: "Neutrino's cutting-edge formula operation", carClass: "F1" },
  { name: "Maecides Grand Prix",        descriptor: "Maecides' full-works formula entry", carClass: "F1" },
];
