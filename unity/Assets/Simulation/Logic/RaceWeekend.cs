#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// The handoff between the career and the driven race. The menus call <see cref="Begin"/>
    /// before loading the race; the race reads <see cref="Setup"/>, and at the flag calls
    /// <see cref="Finish"/>, which settles the race into the career: <see cref="Settled"/> is the
    /// new game state and <see cref="Summary"/> what the race summary shows. No file IO here: the
    /// menus save <see cref="Settled"/> when <see cref="Finished"/> fires.
    /// </summary>
    public static class RaceWeekend
    {
        public static RaceSetup? Setup { get; private set; }
        public static GameState? Settled { get; private set; }
        public static RaceSummary? Summary { get; private set; }

        /// <summary>Fires once the race is settled, with <see cref="Settled"/> and <see cref="Summary"/> set.</summary>
        public static event Action? Finished;

        /// <summary>True while the race is paused from its pause menu: the race scene holds its clock and ignores keys.</summary>
        public static bool Paused { get; set; }

        /// <summary>Fires when the player asks to withdraw from the race (<see cref="Withdraw"/>).</summary>
        public static event Action? Withdrawing;

        /// <summary>
        /// Takes the player's car out of the race now (<see cref="FailureType.Withdrawn"/>) and settles the race from where
        /// every car is. The race scene does it on <see cref="Withdrawing"/>.
        /// </summary>
        public static void Withdraw() => Withdrawing?.Invoke();

        /// <summary>
        /// True while the race waits only on the cooldown laps: the time is up and the player's race is over.
        /// The race scene keeps it up to date; the pause menu's key skips to the race summary while it holds.
        /// </summary>
        public static bool CanSkipToEnd { get; set; }

        /// <summary>Fires when the player asks to skip the cooldown laps (<see cref="SkipToEnd"/>).</summary>
        public static event Action? SkippingToEnd;

        /// <summary>
        /// Fires once the race summary should open: straight after <see cref="Finished"/>, or after the podium when the
        /// race ran to its end. The race scene calls <see cref="ShowSummary"/>.
        /// </summary>
        public static event Action? SummaryDue;

        /// <summary>Opens the race summary of the race just settled.</summary>
        public static void ShowSummary() => SummaryDue?.Invoke();

        /// <summary>Settles the race now, without waiting for the cooldown laps. The race scene does it on <see cref="SkippingToEnd"/>.</summary>
        public static void SkipToEnd() => SkippingToEnd?.Invoke();

        /// <summary>The race save the race scene carries on from, or null for a race from its start.</summary>
        public static RaceSave? Resuming { get; private set; }

        /// <summary>The race scene's way to save the race as it stands, or null outside a race.</summary>
        public static Func<RaceSave>? Snapshot { get; set; }

        /// <summary>Gets a saved race ready to carry on from: the menus call it before loading the race.</summary>
        public static void Resume(RaceSave save)
        {
            Setup = save.Setup;
            Resuming = save;
            Paused = false;
            CanSkipToEnd = false;
            Settled = null;
            Summary = null;
        }

        /// <summary>
        /// Gets the race ready from the career. <paramref name="lineup"/> is the player's driver ids
        /// in stint order, starter first, with <see cref="RaceEntry.PlayerCharacterDriverId"/> for the
        /// owner-driver; null uses the order stored in the career (<see cref="DriverLineup.Of"/>).
        /// </summary>
        public static RaceSetup Begin(GameState game, IReadOnlyList<string>? lineup = null)
        {
            Setup = BuildSetup(game, lineup);
            Resuming = null;
            Paused = false;
            CanSkipToEnd = false;
            Settled = null;
            Summary = null;
            return Setup;
        }

        /// <summary>Settles the race just run into the career, then fires <see cref="Finished"/>.</summary>
        public static void Finish(RaceDay race)
        {
            (GameState game, RaceSummary summary) = Settle(race);
            Settled = game;
            Summary = summary;
            Setup = null;
            Resuming = null;
            Paused = false;
            CanSkipToEnd = false;
            Finished?.Invoke();
        }

        // ---------------------------------------------------------------------
        // Grid
        // ---------------------------------------------------------------------

        /// <summary>
        /// Every team with a car and a driver, ordered by prestige, highest on pole; ties go to the
        /// quicker class, then the team list order. Every car starts on a full tank.
        /// </summary>
        public static RaceSetup BuildSetup(GameState game, IReadOnlyList<string>? lineup = null)
        {
            lineup ??= DriverLineup.Of(game);
            var teams = game.Teams.Select((t, i) => (Team: t, Order: i)).ToDictionary(x => x.Team.Id);
            var grid = new List<(GridCar Car, int Order)>();
            foreach (CarEntry entry in RaceEntry.BuildCarEntries(game))
            {
                (BaseTeam team, int order) = teams[entry.TeamId];
                IReadOnlyList<RaceDriver> drivers = team is PlayerTeam ? Ordered(entry.Drivers, lineup) : entry.Drivers;
                grid.Add((new GridCar
                {
                    TeamId = team.Id,
                    TeamName = team.Name,
                    Player = team is PlayerTeam,
                    Prestige = team.Prestige,
                    RaceNumber = team.RaceNumber,
                    Car = entry.Instance,
                    Model = entry.Model,
                    Lineup = drivers,
                    CrewSize = entry.CrewSize,
                    EngineerSkill = entry.EngineerSkill,
                    TyreSets = entry.TyreSetsAvailable,
                    SpareParts = entry.SparePartsAvailable,
                    StartingFuel = entry.StartingFuel,
                }, order));
            }
            GridCar[] ordered = grid
                .OrderByDescending(g => g.Car.Prestige)
                .ThenByDescending(g => g.Car.Model.CarClass == CarClass.F1 ? -1 : (int)g.Car.Model.CarClass)
                .ThenBy(g => g.Order)
                .Select(g => g.Car)
                .ToArray();
            return new RaceSetup { Game = game, Grid = ordered };
        }

        /// <summary>The player's drivers in lineup order; any not named follow in their usual order.</summary>
        private static IReadOnlyList<RaceDriver> Ordered(IReadOnlyList<RaceDriver> drivers, IReadOnlyList<string>? lineup)
        {
            if (lineup == null) return drivers;
            var named = lineup.Select(id => drivers.FirstOrDefault(d => d.Id == id)).Where(d => d != null).Select(d => d!).Distinct();
            return named.Concat(drivers.Where(d => !lineup.Contains(d.Id))).ToArray();
        }

        // ---------------------------------------------------------------------
        // Settling
        // ---------------------------------------------------------------------

        /// <summary>
        /// Writes a finished race into the career: prize money for every team, the player's fuel
        /// bill, tyres and spares used, each car's condition, the race history entry, and prestige
        /// recalculated for every team, and the paper. The game moves to the post-race phase.
        /// </summary>
        public static (GameState Game, RaceSummary Summary) Settle(RaceDay race)
        {
            GameState game = race.Setup.Game;
            IReadOnlyList<CarRaceLog> order = race.Classify();
            CarRaceLog? player = order.FirstOrDefault(c => c.Car.Player);
            double fuelUsed = player != null ? FuelUsed(player) : 0;

            PostRaceFinancialOutput money = PostRaceFinancials.ProcessPostRaceFinancials(new PostRaceFinancialInput
            {
                Results = order.Select((c, i) => new PostRaceCarResult
                {
                    TeamId = c.Car.TeamId,
                    CarId = c.Car.Car.Id,
                    CarClass = c.Car.Model.CarClass,
                    Position = i + 1,
                    LapsCompleted = c.Laps,
                    Retired = c.Retired,
                }).ToArray(),
                PlayerFuelConsumed = fuelUsed,
                FuelCostPerLitre = game.EconomyConfig.FuelConfig.CostPerLitre,
                Year = game.CurrentYear,
            });

            RaceHistoryEntry history = money.RaceHistoryEntry with
            {
                FastestLap = race.FastestLap,
                PositionHistory = race.PositionHistory,
                CarIndexById = race.Cars.ToDictionary(c => c.Car.Car.Id, c => c.Index),
                Stints = race.Cars.ToDictionary(c => c.Car.Car.Id, c => (IReadOnlyList<Stint>)c.Stints.ToArray()),
                ModeCounters = race.Cars.ToDictionary(c => c.Car.Car.Id, c => c.Modes),
                Events = Events(race),
            };

            GameState next = GameActions.PushRaceHistory(game, history);
            foreach (RaceResult r in history.Results)
                if (r.PrizeMoney > 0) next = GameActions.AwardPrizeMoney(next, r.TeamId, r.Position, r.PrizeMoney);
            if (player != null) next = GameActions.DeductFuelCost(next, money.PlayerFuelCost);
            next = WearAndStock(next, race.Cars);

            double prestigeBefore = game.Teams.FirstOrDefault(t => t is PlayerTeam)?.Prestige ?? 0;
            Dictionary<string, double> prestige = PostRace.RecalculateAllPrestige(next.Teams.Select(t => t.Id), next.RaceHistory);
            next = next with
            {
                Phase = GamePhase.PostRace,
                Teams = next.Teams.Select(t =>
                {
                    double p = prestige.TryGetValue(t.Id, out double v) ? v : t.Prestige;
                    return t with { Prestige = p, PrestigeHistory = t.PrestigeHistory.Append(p).ToArray() };
                }).ToArray(),
            };

            RaceSummary summary = Summarise(race, order, history, player, fuelUsed, money.PlayerFuelCost, prestigeBefore, next);
            next = next with { Newspaper = NewspaperWriter.Write(game, next, history, summary) };
            return (next, summary);
        }

        /// <summary>Litres burnt: a full tank at the start plus what went in at the stops, less what's left.</summary>
        private static double FuelUsed(CarRaceLog car) =>
            Math.Max(0, car.Car.StartingFuel + car.FuelAdded - car.FuelLeft);

        /// <summary>Each racing team's car condition, tyre sets and spare parts after the race.</summary>
        private static GameState WearAndStock(GameState game, IReadOnlyList<CarRaceLog> cars)
        {
            var byTeam = cars.ToDictionary(c => c.Car.TeamId);
            return game with
            {
                Teams = game.Teams.Select(t =>
                {
                    if (!byTeam.TryGetValue(t.Id, out CarRaceLog? c)) return t;
                    IReadOnlyList<CarInstance> worn = t.Cars
                        .Select(car => car.Id == c.Car.Car.Id ? car with { Condition = c.ConditionAfterRace } : car)
                        .ToArray();
                    return t switch
                    {
                        PlayerTeam p => p with { Cars = worn, TyreSets = c.TyreSets, SpareParts = c.SpareParts },
                        AITeam a => a with { Cars = worn, TyreSets = c.TyreSets, SpareParts = c.SpareParts },
                        _ => t with { Cars = worn },
                    };
                }).ToArray(),
            };
        }

        private static IReadOnlyList<RaceEvent> Events(RaceDay race)
        {
            var events = new List<RaceEvent>();
            foreach (CarRaceLog c in race.Cars)
            {
                string car = c.Car.Car.Id, team = c.Car.TeamId;
                foreach (IssueRecord i in c.Issues)
                    events.Add(new RaceEvent { Lap = i.Lap, Type = RaceEventType.Issue, Text = $"{car} — {Describe(i.TemplateId)}", CarId = car, TeamId = team });
                foreach (PitStopRecord s in c.PitStops)
                    events.Add(new RaceEvent
                    {
                        Lap = s.Lap,
                        Type = RaceEventType.PitStop,
                        Text = $"{car} pits{(s.DriverIn != s.DriverOut ? $" — {s.DriverIn} takes over" : "")}",
                        CarId = car,
                        TeamId = team,
                    });
                if (c.Retired)
                    events.Add(new RaceEvent
                    {
                        Lap = c.Laps,
                        Type = RaceEventType.Retirement,
                        Text = $"{car} retired — {(c.Failure?.Description ?? c.RetirementReason.ToString()!).ToLowerInvariant()}",
                        CarId = car,
                        TeamId = team,
                    });
            }
            return events.OrderBy(e => e.Lap).ToArray();
        }

        private static string Describe(string templateId) =>
            IssueTemplates.AllIssueTemplates.FirstOrDefault(t => t.Id == templateId)?.Description ?? "mechanical issue";

        private static RaceSummary Summarise(
            RaceDay race,
            IReadOnlyList<CarRaceLog> order,
            RaceHistoryEntry history,
            CarRaceLog? player,
            double fuelUsed,
            double fuelCost,
            double prestigeBefore,
            GameState after)
        {
            var inClass = new Dictionary<CarClass, int>();
            ClassifiedCar[] classified = order.Select((c, i) =>
            {
                inClass.TryGetValue(c.Car.Model.CarClass, out int n);
                inClass[c.Car.Model.CarClass] = n + 1;
                return new ClassifiedCar
                {
                    Position = i + 1,
                    ClassPosition = n + 1,
                    TeamId = c.Car.TeamId,
                    TeamName = c.Car.TeamName,
                    CarId = c.Car.Car.Id,
                    ModelId = c.Car.Model.Id,
                    CarClass = c.Car.Model.CarClass,
                    Laps = c.Laps,
                    Retired = c.Retired,
                    RetirementReason = c.RetirementReason,
                    FailureId = c.FailureId,
                    BestLap = c.BestLap,
                    PrizeMoney = history.Results[i].PrizeMoney,
                };
            }).ToArray();
            if (player == null) return new RaceSummary { Year = history.Year, Classification = classified, FastestLap = race.FastestLap };

            PlayerTeam? team = after.Teams.OfType<PlayerTeam>().FirstOrDefault();
            return new RaceSummary
            {
                Year = history.Year,
                Classification = classified,
                Player = classified.First(c => c.TeamId == player.Car.TeamId),
                Stints = player.Stints.Where(s => s.EndLap >= s.StartLap).Select(s => new StintRecord
                {
                    DriverId = s.DriverId,
                    DriverName = DriverName(s.DriverId, race.Setup.Game),
                    StartLap = s.StartLap,
                    EndLap = s.EndLap,
                    BestLap = Best(player.LapTimes, s.StartLap, s.EndLap),
                }).ToArray(),
                PitStops = player.PitStops,
                Issues = player.Issues,
                PositionByLap = race.PositionHistory.Select(row => row[player.Index]).ToArray(),
                FuelUsed = fuelUsed,
                FuelCost = fuelCost,
                TyreSetsUsed = player.Car.TyreSets - player.TyreSets,
                SparePartsUsed = player.Car.SpareParts - player.SpareParts,
                Condition = player.ConditionAfterRace,
                PrestigeBefore = prestigeBefore,
                PrestigeAfter = team?.Prestige ?? 0,
                FastestLap = race.FastestLap,
            };
        }

        /// <summary>Quickest of laps first to last (1-indexed), or null if none.</summary>
        private static double? Best(IReadOnlyList<double> laps, int first, int last)
        {
            double? best = null;
            for (int lap = Math.Max(first, 1); lap <= Math.Min(last, laps.Count); lap++)
                if (laps[lap - 1] > 0 && (best == null || laps[lap - 1] < best)) best = laps[lap - 1];
            return best;
        }

        /// <summary>A driver's name: the player's own for the player character.</summary>
        public static string DriverName(string id, GameState game)
        {
            if (id == RaceEntry.PlayerCharacterDriverId)
                return game.Teams.OfType<PlayerTeam>().FirstOrDefault()?.PlayerName ?? "";
            return game.Drivers.FirstOrDefault(d => d.Id == id)?.Name ?? id;
        }
    }
}
