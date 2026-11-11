#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>TS <c>NewGameOptions</c>.</summary>
    public sealed record NewGameOptions
    {
        public string PlayerName { get; init; } = "";
        public string Nationality { get; init; } = ""; // ISO 3166-1 alpha-2 country code
        public string TeamName { get; init; } = "";
        public string? Logo { get; init; }
        public string TeamColour { get; init; } = ""; // #rrggbb
        public PlayerSkills Skills { get; init; } = new PlayerSkills();
    }

    /// <summary>Port of simulation/gameInit.ts.</summary>
    public static class GameInit
    {
        // ---------------------------------------------------------------------------
        // Tunable constants
        // ---------------------------------------------------------------------------

        /// <summary>Total drivers in the global pool.</summary>
        private const int DriverPoolSize = 310;
        /// <summary>Drivers per AI team.</summary>
        private const int DriversPerAiTeam = 3;
        /// <summary>Skill points the player spends at new game.</summary>
        public const int StartingSkillPoints = 15;
        /// <summary>Player's starting budget.</summary>
        private const double PlayerStartingBudget = 1_000;
        /// <summary>Youngest driver in the seeded pool.</summary>
        private const int MinDriverAge = 18;
        /// <summary>Oldest driver in the seeded pool.</summary>
        private const int MaxDriverAge = 42;
        /// <summary>Each driver has up to this many years of phase offset per stat.</summary>
        private const double PhaseOffsetRange = 2;
        /// <summary>Minimum peak stat value for seeded drivers.</summary>
        private const double PeakStatMin = 30;
        /// <summary>Range of peak stat values (added to min).</summary>
        private const double PeakStatRange = 70;
        /// <summary>Peak age minimum for seeded drivers.</summary>
        private const int PeakAgeMin = 28;
        /// <summary>Range of peak ages (added to min).</summary>
        private const int PeakAgeRange = 4;

        // ---------------------------------------------------------------------------
        // AI team tier distribution
        // ---------------------------------------------------------------------------

        /// <summary>
        /// AI teams are spread across the performance spectrum. Each tier entry defines
        /// how many AI teams start with a car from that tier and their starting budget range.
        /// </summary>
        private sealed record AiTierSpec
        {
            public IReadOnlyList<string> ModelIds { get; init; } = Array.Empty<string>();
            public int Count { get; init; }
            public double BudgetMin { get; init; }
            public double BudgetMax { get; init; }
            public double PrestigeMin { get; init; }
            public double PrestigeMax { get; init; }
        }

        private static readonly IReadOnlyList<AiTierSpec> AiTierSpecs = new[]
        {
            new AiTierSpec { ModelIds = new[] { "f-01", "f-02", "f-03", "f-04", "f-05", "f-06" }, Count = 20, BudgetMin = 500,     BudgetMax = 4_000,   PrestigeMin = 5,  PrestigeMax = 15 },
            new AiTierSpec { ModelIds = new[] { "e-07", "e-08", "e-09", "e-10", "e-11" },         Count = 20, BudgetMin = 4_000,   BudgetMax = 10_000,  PrestigeMin = 15, PrestigeMax = 30 },
            new AiTierSpec { ModelIds = new[] { "d-12", "d-13", "d-14", "d-15" },                 Count = 20, BudgetMin = 10_000,  BudgetMax = 25_000,  PrestigeMin = 30, PrestigeMax = 50 },
            new AiTierSpec { ModelIds = new[] { "c-16", "c-17", "c-18", "c-19", "c-20" },         Count = 15, BudgetMin = 25_000,  BudgetMax = 60_000,  PrestigeMin = 50, PrestigeMax = 65 },
            new AiTierSpec { ModelIds = new[] { "b-21", "b-22", "b-23", "b-24", "b-25" },         Count = 12, BudgetMin = 60_000,  BudgetMax = 150_000, PrestigeMin = 65, PrestigeMax = 80 },
            new AiTierSpec { ModelIds = new[] { "a-26", "a-27", "a-28", "a-29", "a-30" },         Count = 12, BudgetMin = 150_000, BudgetMax = 400_000, PrestigeMin = 80, PrestigeMax = 95 },
        };

        // ---------------------------------------------------------------------------
        // First-name pools for procedural driver names
        // ---------------------------------------------------------------------------

        private static readonly string[] FirstNames =
        {
            "Alex", "Ben", "Carlos", "Daniel", "Erik", "Felix", "George", "Hugo",
            "Ivan", "James", "Kevin", "Liam", "Marco", "Nico", "Oscar", "Pedro",
            "Quinn", "Rafael", "Stefan", "Tom", "Uwe", "Victor", "Will", "Xavier",
            "Yuki", "Zane", "Adrian", "Boris", "Chris", "David", "Emil", "Fabio",
            "Goran", "Hans", "Igor", "Jan", "Klaus", "Lars", "Max", "Nathan",
            "Oliver", "Paul", "Raul", "Seb", "Theo", "Uri", "Vince", "Wes",
        };

        private static readonly string[] LastNames =
        {
            "Alonso", "Berg", "Costa", "Dumas", "Evans", "Fischer", "Garcia", "Hill",
            "Ivanov", "Jensen", "Kim", "Laurent", "Müller", "Nielsen", "Olsen", "Perez",
            "Quinn", "Rossi", "Santos", "Torres", "Ueda", "Varga", "Weber", "Xu",
            "Yang", "Zhou", "Arnault", "Blake", "Chen", "De Vries", "Esteban", "Falk",
            "Greco", "Hart", "Ito", "Johansson", "Khan", "Lopez", "Mori", "Novak",
            "Ortiz", "Park", "Rivera", "Silva", "Tanaka", "Uribe", "Volkov", "Walsh",
        };

        public static readonly string[] Nationalities =
        {
            "br", "jp", "mx", "gb", "se", "fr", "gh", "ru", "in", "de",
            "es", "it", "kr", "nl", "au", "ca", "ar", "cl", "no", "fi",
            "dk", "be", "at", "ch", "pt", "pl", "cz", "hu", "nz", "za",
            "my", "sg", "th", "ph", "ie", "us", "cn", "tw", "co", "ve",
        };

        // ---------------------------------------------------------------------------
        // Driver generation helpers
        // ---------------------------------------------------------------------------

        private static string GenerateDriverName(int index, Func<double> random)
        {
            var first = FirstNames[(int)Math.Floor(random() * FirstNames.Length)];
            var last = LastNames[(int)Math.Floor(random() * LastNames.Length)];
            return $"{first} {last}";
        }

        /// <summary>The rookies who join the driver pool at the end of a year, with ids unique to that year.</summary>
        public static IReadOnlyList<RookieSpec> RookieSpecs(int year, int count, Func<double> random) =>
            Enumerable.Range(0, count).Select(i => new RookieSpec
            {
                Id = $"rookie-y{year}-{i}",
                Name = GenerateDriverName(i, random),
                Nationality = Nationalities[(int)Math.Floor(random() * Nationalities.Length)],
            }).ToArray();

        private static DriverStats GeneratePeakStats(Func<double> random) => new DriverStats
        {
            Pace        = JsMath.Round(PeakStatMin + random() * PeakStatRange),
            Consistency = JsMath.Round(PeakStatMin + random() * PeakStatRange),
            Stamina     = JsMath.Round(PeakStatMin + random() * PeakStatRange),
            Safety      = JsMath.Round(PeakStatMin + random() * PeakStatRange),
            Smoothness  = JsMath.Round(PeakStatMin + random() * PeakStatRange),
        };

        private static DriverStats GeneratePhaseOffsets(Func<double> random)
        {
            double Offset() => (random() * 2 - 1) * PhaseOffsetRange;
            return new DriverStats
            {
                Pace        = Offset(),
                Consistency = Offset(),
                Stamina     = Offset(),
                Safety      = Offset(),
                Smoothness  = Offset(),
            };
        }

        /// <summary>
        /// Seeds the full 310-driver pool with a spread of ages across the career curve.
        /// Age distribution is roughly uniform across [MinDriverAge, MaxDriverAge].
        /// </summary>
        public static IReadOnlyList<Driver> SeedDriverPool(Func<double>? random = null)
        {
            var rng = random ?? DefaultRandom.Next;
            var drivers = new List<Driver>();
            for (var i = 0; i < DriverPoolSize; i++)
            {
                var age = MinDriverAge + (int)Math.Floor(rng() * (MaxDriverAge - MinDriverAge + 1));
                var peakAge = PeakAgeMin + (int)Math.Floor(rng() * (PeakAgeRange + 1));
                var peakStats = GeneratePeakStats(rng);
                var phaseOffsets = GeneratePhaseOffsets(rng);

                var driver = new Driver
                {
                    Id = $"driver-{i}",
                    Name = GenerateDriverName(i, rng),
                    Nationality = Nationalities[(int)Math.Floor(rng() * Nationalities.Length)],
                    Age = age,
                    CurveParams = new DriverCurveParams { PeakAge = peakAge, PeakStats = peakStats, PhaseOffsets = phaseOffsets },
                    MarketValue = 0,
                };
                // Calculate market value based on current stats
                driver = driver with { MarketValue = DriverLifecycle.CalculateMarketValue(DriverLifecycle.CalculateDriverStats(driver)) };
                drivers.Add(driver);
            }
            return drivers;
        }

        // ---------------------------------------------------------------------------
        // AI team generation
        // ---------------------------------------------------------------------------

        private static int globalCarId = 0;
        private static string NextCarId() => $"car-{++globalCarId}";

        private static (AITeam Team, IReadOnlyList<Contract> Contracts) CreateAiTeam(
            int index,
            string teamName,
            AiTierSpec spec,
            IReadOnlyList<Driver> drivers,
            int driverOffset,
            Func<double> random)
        {
            var teamId = $"ai-{index}";
            var modelId = spec.ModelIds[(int)Math.Floor(random() * spec.ModelIds.Count)];
            var car = new CarInstance
            {
                Id = NextCarId(),
                ModelId = modelId,
                Age = 0,
                Condition = 100,
                InstalledUpgrades = new InstalledUpgrades { Power = false, Handling = false, Comfort = false },
            };

            var budget = JsMath.Round(spec.BudgetMin + random() * (spec.BudgetMax - spec.BudgetMin));

            // Assign 3 drivers from the pool
            var contracts = new List<Contract>();
            for (var d = 0; d < DriversPerAiTeam; d++)
            {
                var driver = drivers[driverOffset + d];
                var stats = DriverLifecycle.CalculateDriverStats(driver);
                var annualSalary = DriverLifecycle.CalculateAnnualSalary(stats);
                var length = (int)Math.Floor(random() * 3) + 1;
                contracts.Add(new Contract
                {
                    DriverId = driver.Id,
                    TeamId = teamId,
                    Length = length,
                    RemainingYears = length,
                    AnnualSalary = annualSalary,
                });
            }

            var prestige = JsMath.Round(spec.PrestigeMin + random() * (spec.PrestigeMax - spec.PrestigeMin));

            var team = new AITeam
            {
                Id = teamId,
                Name = teamName,
                Budget = budget,
                Prestige = prestige,
                PrestigeHistory = new[] { prestige },
                Cars = new[] { car },
                Contracts = contracts,
                EnteredCarId = car.Id,
                SpareParts = 5,
                TyreSets = 5,
            };

            return (team, contracts);
        }

        // ---------------------------------------------------------------------------
        // Player team creation
        // ---------------------------------------------------------------------------

        private static PlayerTeam CreatePlayerTeam(
            string playerName,
            string nationality,
            string teamName,
            string? logo,
            string teamColour,
            PlayerSkills skills)
        {
            // Player starts with no car. Their first action is visiting the dealers.
            // Plot armour lives at the dealer level: if the player has no car and can't
            // afford the cheapest one, it becomes purchasable anyway.
            return new PlayerTeam
            {
                Id = "player",
                Name = teamName,
                Budget = PlayerStartingBudget,
                Prestige = 0,
                PrestigeHistory = new double[] { 0 },
                CrewSize = 0,
                Cars = Array.Empty<CarInstance>(),
                Contracts = Array.Empty<Contract>(),
                EnteredCarId = null,
                PlayerName = playerName,
                Nationality = nationality,
                Logo = logo,
                TeamColour = teamColour,
                Skills = skills,
                ForfeitedSkillPoints = Math.Max(0, StartingSkillPoints - skills.Driver - skills.Engineer - skills.Business),
                SpareParts = 0,
                TyreSets = 0,
                Transactions = Array.Empty<TransactionRecord>(),
            };
        }

        // ---------------------------------------------------------------------------
        // Placeholder track (one fixed circuit)
        // ---------------------------------------------------------------------------

        private static readonly Track PlaceholderTrack = new Track
        {
            Version = 1,
            Scale = new TrackScale { MetresPerGridSquare = 10 },
            Path = new TrackPath { Curves = Array.Empty<BezierCurve>(), Closed = true, LengthMetres = 5000 },
            PitLane = null,
            StartFinish = null,
        };

        // ---------------------------------------------------------------------------
        // Full game initialization
        // ---------------------------------------------------------------------------

        /// <summary>JS <c>new Date().toISOString()</c>.</summary>
        private static string IsoNow() =>
            DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        /// <summary>
        /// Creates a complete initial GameState.
        ///
        /// - Seeds 310 drivers with age distribution
        /// - Creates 99 AI teams across all tiers, each with 1 car, 3 contracted drivers and a crew sized by prestige (AiCrew)
        /// - Creates the player team: $1,000 budget, plot armour car, no drivers, 0 crew
        /// - Generates the car market (new + used)
        /// - Year 1, newGame phase
        /// </summary>
        public static GameState InitializeGame(NewGameOptions options, Func<double>? random = null)
        {
            var rng = random ?? DefaultRandom.Next;

            // Reset car ID counter for determinism
            globalCarId = 0;

            // 1. Seed driver pool
            var drivers = SeedDriverPool(rng);

            // 2. Create AI teams: assign drivers in order from the pool
            //    First 99 x 3 = 297 drivers go to AI teams, remainder are free agents
            var allContracts = new List<Contract>();
            var aiTeams = new List<AITeam>();
            var driverOffset = 0;
            var teamIndex = 0;

            foreach (var spec in AiTierSpecs)
            {
                for (var i = 0; i < spec.Count; i++)
                {
                    var (team, contracts) = CreateAiTeam(
                        teamIndex,
                        AiTeamRoster.All[teamIndex].Name,
                        spec,
                        drivers,
                        driverOffset,
                        rng);
                    aiTeams.Add(team);
                    allContracts.AddRange(contracts);
                    driverOffset += DriversPerAiTeam;
                    teamIndex++;
                }
            }

            // 3. Create player team
            var playerTeam = CreatePlayerTeam(
                options.PlayerName,
                options.Nationality,
                options.TeamName,
                options.Logo,
                options.TeamColour,
                options.Skills);

            // 4. All teams, AI crews sized by prestige
            var teams = AiCrew.Assign(new BaseTeam[] { playerTeam }.Concat(aiTeams));

            // 5. Car market
            var carModels = CarModels.All;
            var newListings = CarMarketLogic.GetNewCarListings(carModels);
            var usedListings = CarMarketLogic.GenerateUsedInventoryByClass(carModels, false, rng);

            return new GameState
            {
                Phase = GamePhase.PreRace,
                CurrentYear = 1,
                Meta = new SaveMetadata
                {
                    CreatedAt = IsoNow(),
                    LastSavedAt = IsoNow(),
                    Version = 1,
                },
                CarModels = carModels,
                Drivers = drivers,
                Contracts = allContracts,
                Teams = teams,
                EconomyConfig = new EconomyConfig
                {
                    TierCosts = Array.Empty<TierCosts>(), // Placeholder: will be defined during economy tuning
                    FuelConfig = new FuelConfig { CostPerLitre = 2 },
                    CrewCostPerMember = 2_000,
                    ContractDiscounts = new ContractDiscounts { TwoYear = 0.10, ThreeYear = 0.20 },
                    BuyoutMultiplier = 1.5,
                },
                CarMarket = new CarMarket { NewListings = newListings, UsedListings = usedListings },
                Track = PlaceholderTrack,
                RaceHistory = Array.Empty<RaceHistoryEntry>(),
            };
        }
    }
}
