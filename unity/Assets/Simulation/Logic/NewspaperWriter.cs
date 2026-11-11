#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>
    /// Writes the paper printed after a race (GDD §9, Newspaper): the winner's story, the class podiums, one feature
    /// picked from the template pool, and the player's story, which grows with prestige. The same race always
    /// prints the same paper.
    /// </summary>
    public static class NewspaperWriter
    {
        /// <summary>Prestige at which the player's story moves up from a footnote, and then to the front page.</summary>
        public const double ColumnPrestige = 40, FrontPrestige = 70;

        /// <summary>Classes on the podium table, quickest first.</summary>
        public static readonly IReadOnlyList<CarClass> ClassOrder = new[]
        {
            CarClass.F1, CarClass.A, CarClass.B, CarClass.C, CarClass.D, CarClass.E, CarClass.F,
        };

        /// <param name="before">The career as the race started.</param>
        /// <param name="after">The career with the race settled into it.</param>
        /// <param name="race">This race's history entry.</param>
        /// <param name="summary">This race's summary.</param>
        public static Newspaper Write(GameState before, GameState after, RaceHistoryEntry race, RaceSummary summary)
        {
            var desk = new Desk(before, after, race, summary);
            return desk.Print();
        }

        /// <summary>Which share of the paper the player gets at a prestige, before a strong result lifts it.</summary>
        public static PlayerCoverage CoverageFor(double prestige) =>
            prestige >= FrontPrestige ? PlayerCoverage.Front : prestige >= ColumnPrestige ? PlayerCoverage.Column : PlayerCoverage.Footnote;

        /// <summary>Everything one race's paper is written from.</summary>
        private sealed class Desk
        {
            private readonly GameState before, after;
            private readonly RaceHistoryEntry race;
            private readonly RaceSummary summary;
            private readonly Rng rng;
            private readonly Dictionary<string, Driver> drivers;
            private readonly Dictionary<string, BaseTeam> teamsBefore;
            private readonly Dictionary<string, string> descriptors;
            private readonly ClassifiedCar? winner;

            public Desk(GameState before, GameState after, RaceHistoryEntry race, RaceSummary summary)
            {
                this.before = before;
                this.after = after;
                this.race = race;
                this.summary = summary;
                winner = summary.Classification.FirstOrDefault();
                rng = new Rng(Seed(summary));
                drivers = before.Drivers.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
                teamsBefore = before.Teams.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
                descriptors = AiTeamRoster.All
                    .Where(e => !e.Descriptor.Contains("—"))
                    .GroupBy(e => e.Name)
                    .ToDictionary(g => g.Key, g => g.First().Descriptor);
            }

            private int WinnerLaps => winner?.Laps ?? 0;

            public Newspaper Print()
            {
                ClassifiedCar? player = summary.Player;
                bool playerWon = player != null && player.Position == 1;
                PlayerCoverage coverage = player == null ? PlayerCoverage.None : playerWon ? PlayerCoverage.Lead : Coverage(player);
                (FeatureKind Kind, Story Story)? feature = Feature();
                return new Newspaper
                {
                    Year = summary.Year,
                    Lead = winner == null ? new Story() : Lead(winner),
                    Podiums = Podiums(),
                    FeatureKind = feature?.Kind,
                    Feature = feature?.Story,
                    Coverage = coverage,
                    PlayerStory = player == null || playerWon ? null : PlayerStory(player, coverage),
                };
            }

            // ------------------------------------------------------------------ the lead

            private Story Lead(ClassifiedCar w)
            {
                ClassifiedCar? second = summary.Classification.Skip(1).FirstOrDefault();
                int margin = second == null ? 0 : w.Laps - second.Laps;
                int wins = Math.Max(1, WinsFor(w.TeamId));
                string model = ModelName(w.ModelId);

                var headlines = new List<string> { $"{w.TeamName} win the Tarmac Gauntlet", $"{w.TeamName} conquer the Gauntlet" };
                if (margin >= 2) headlines.Add($"{w.TeamName} win by {Count(margin, "lap")}");
                if (second != null && margin == 0) headlines = new List<string> { $"{w.TeamName} win on the final lap", $"{w.TeamName} hold on to win" };
                if (wins > 1) headlines = new List<string> { wins == 2 ? $"{w.TeamName} win again" : $"{w.TeamName} make it {Count(wins, "win")}" };

                IReadOnlyList<string> crew = DriverNames(w.CarId, w.TeamId);
                string standfirst = crew.Count > 0
                    ? $"{List(crew)} {(crew.Count == 1 ? "brings" : "bring")} the {model} home after {w.Laps} laps."
                    : $"The {model} completes {w.Laps} laps.";

                var paragraphs = new List<string>();
                string opening = $"{w.TeamName} won the {Nth(summary.Year)} 24h Tarmac Gauntlet with {w.Laps} laps";
                if (second != null)
                {
                    string gap = margin == 0 ? "on the same lap as" : margin == 1 ? "one lap clear of" : $"{Count(margin, "lap")} clear of";
                    opening += $", {gap} {second.TeamName} in the {ModelName(second.ModelId)}";
                }
                paragraphs.Add(opening + ".");

                int grid = GridPosition(w.CarId);
                int led = LapsLed(w.CarId);
                int rows = race.PositionHistory?.Count ?? 0;
                if (grid > 0 && rows > 0)
                {
                    string start = grid == 1 ? "from pole" : $"{Ordinal(grid)} on the grid";
                    paragraphs.Add(led >= rows
                        ? $"The {model} started {start} and led every lap."
                        : led > 0
                            ? $"The {model} started {start} and led {led} of the {rows} laps."
                            : $"The {model} started {start} and only took the lead at the very end.");
                }

                (string Name, int Laps, int Stints)? anchor = TopDriver(w.CarId, w.TeamId);
                if (anchor != null && crew.Count > 1)
                    paragraphs.Add($"{anchor.Value.Name} drove the most, {anchor.Value.Laps} laps across {Count(anchor.Value.Stints, "stint")}.");

                if (race.FastestLap != null)
                {
                    string time = LapTimeText(race.FastestLap.Time);
                    paragraphs.Add(race.FastestLap.TeamId == w.TeamId
                        ? $"The team also set the fastest lap of the race, a {time}."
                        : $"{TeamName(race.FastestLap.TeamId)} set the fastest lap of the race, a {time}.");
                }

                if (wins > 1) paragraphs.Add($"It is the team's {Ordinal(wins)} win.");

                return new Story { Headline = Pick(headlines), Standfirst = standfirst, Paragraphs = paragraphs, TeamId = w.TeamId };
            }

            private int WinsFor(string teamId) =>
                after.RaceHistory.Count(h => h.Results.Any(r => r.Position == 1 && r.TeamId == teamId));

            // ------------------------------------------------------------------ podiums

            private IReadOnlyList<ClassPodium> Podiums() => ClassOrder
                .Select(cls => new ClassPodium
                {
                    CarClass = cls,
                    Places = summary.Classification
                        .Where(c => c.CarClass == cls && c.Finished())
                        .Take(3)
                        .Select(c => new PodiumPlace { TeamId = c.TeamId, TeamName = c.TeamName, ModelName = ModelName(c.ModelId), Laps = c.Laps })
                        .ToArray(),
                })
                .Where(p => p.Places.Count > 0)
                .ToArray();

            // ------------------------------------------------------------------ features

            /// <summary>One story picked at random from the templates that found a team. The winner and the player are left out.</summary>
            private (FeatureKind, Story)? Feature()
            {
                var found = new List<(FeatureKind, Story)>();
                void Try(FeatureKind kind, Story? story)
                {
                    if (story != null) found.Add((kind, story));
                }
                Try(FeatureKind.MostUnlucky, MostUnlucky());
                Try(FeatureKind.MostSurprising, MostSurprising());
                Try(FeatureKind.BiggestMover, BiggestMover());
                Try(FeatureKind.BiggestDisappointment, BiggestDisappointment());
                Try(FeatureKind.VeteranPerformance, VeteranPerformance());
                Try(FeatureKind.RookieDebut, RookieDebut());
                if (found.Count == 0) return null;
                return found[rng.Below(found.Count)];
            }

            private IEnumerable<ClassifiedCar> Field => summary.Classification
                .Where(c => c.TeamId != GameActions.PlayerTeamId && c.TeamId != winner?.TeamId);

            private Story? MostUnlucky()
            {
                ClassifiedCar? c = Field
                    .Where(x => x.Retired && x.Laps * 4 >= WinnerLaps && x.Laps > 0)
                    .OrderByDescending(x => x.Laps).ThenBy(x => x.Position)
                    .FirstOrDefault();
                if (c == null) return null;
                string model = ModelName(c.ModelId);
                int running = ClassPositionAtLap(c, c.Laps);
                string how = c.RetirementReason == FailureType.Crash ? $"their {model} went off on lap {c.Laps}" : $"their {model} broke down on lap {c.Laps}";
                var paragraphs = new List<string>
                {
                    running > 0
                        ? $"{Introduce(c)} were running {Ordinal(running)} in class {ClassName(c.CarClass)} when {how}."
                        : $"{Introduce(c)} were out when {how}.",
                };
                int hours = WinnerLaps == 0 ? 0 : (int)Math.Round(24.0 * c.Laps / WinnerLaps);
                if (hours >= 23) paragraphs.Add("There was barely an hour left to run.");
                else if (hours >= 20) paragraphs.Add($"There were barely {Count(24 - hours, "hour")} left to run.");
                else if (hours > 0) paragraphs.Add($"It came {Count(hours, "hour")} into the race.");
                int issues = Issues(c.CarId);
                paragraphs.Add(issues > 0 ? $"The car had already fought through {Count(issues, "issue")}." : "Until then the car had not missed a beat.");
                AddCrew(paragraphs, c);
                return Make(c, new[] { $"Heartbreak for {c.TeamName}", $"Cruel end for {c.TeamName}", $"{c.TeamName} out on lap {c.Laps}" }, paragraphs);
            }

            private Story? MostSurprising()
            {
                var best = Field
                    .Where(x => x.Finished() && x.ClassPosition <= 3)
                    .Select(x => (Car: x, Rank: PrestigeRank(x)))
                    .Where(x => x.Rank.Rank - x.Car.ClassPosition >= 3)
                    .OrderByDescending(x => x.Rank.Rank - x.Car.ClassPosition).ThenBy(x => x.Car.Position)
                    .FirstOrDefault();
                if (best.Car == null) return null;
                ClassifiedCar c = best.Car;
                string cls = ClassName(c.CarClass);
                var paragraphs = new List<string>
                {
                    $"{Introduce(c)} came into the race {Ordinal(best.Rank.Rank)} of {best.Rank.Of} class {cls} teams on prestige. They left it {Ordinal(c.ClassPosition)} in class.",
                    $"The {ModelName(c.ModelId)} finished {Ordinal(c.Position)} overall, with {c.Laps} laps.",
                };
                AddCrew(paragraphs, c);
                return Make(c, new[] { $"{c.TeamName} stun class {cls}", $"Giant-killing from {c.TeamName}", $"Nobody saw {c.TeamName} coming" }, paragraphs);
            }

            private Story? BiggestMover()
            {
                var best = Field
                    .Where(x => x.Finished())
                    .Select(x => (Car: x, Gain: GridPosition(x.CarId) - x.Position))
                    .Where(x => x.Gain >= 10)
                    .OrderByDescending(x => x.Gain).ThenBy(x => x.Car.Position)
                    .FirstOrDefault();
                if (best.Car == null) return null;
                ClassifiedCar c = best.Car;
                int grid = GridPosition(c.CarId);
                var paragraphs = new List<string>
                {
                    $"{Introduce(c)} started {Ordinal(grid)} on the grid and finished {Ordinal(c.Position)}, a gain of {best.Gain} places.",
                };
                int six = PositionAtShare(c.CarId, 0.25), twelve = PositionAtShare(c.CarId, 0.5);
                if (six > 0 && twelve > 0) paragraphs.Add($"Six hours in they were {Ordinal(six)}. By half distance they were {Ordinal(twelve)}.");
                AddCrew(paragraphs, c);
                return Make(c, new[] { $"{c.TeamName} climb {best.Gain} places", $"{c.TeamName} carve through the field", $"From {Ordinal(grid)} to {Ordinal(c.Position)} for {c.TeamName}" }, paragraphs);
            }

            private Story? BiggestDisappointment()
            {
                var favourites = summary.Classification
                    .GroupBy(x => x.CarClass)
                    .Select(g => g.OrderByDescending(x => PrestigeBefore(x.TeamId)).ThenBy(x => GridPosition(x.CarId)).First())
                    .Where(x => x.TeamId != GameActions.PlayerTeamId && x.TeamId != winner?.TeamId)
                    .Where(x => !x.Finished() || x.ClassPosition >= 4)
                    .OrderByDescending(x => !x.Finished() ? int.MaxValue : x.ClassPosition).ThenByDescending(x => PrestigeBefore(x.TeamId))
                    .FirstOrDefault();
                if (favourites == null) return null;
                ClassifiedCar c = favourites;
                string cls = ClassName(c.CarClass), model = ModelName(c.ModelId);
                var paragraphs = new List<string> { $"{Introduce(c)} arrived as the top team in class {cls} on prestige." };
                if (c.Retired)
                    paragraphs.Add($"Their {model} lasted {c.Laps} laps before {(c.RetirementReason == FailureType.Crash ? "a crash" : "a mechanical failure")} ended it.");
                else if (c.Unclassified)
                    paragraphs.Add($"Their {model} ran {c.Laps} laps but was not classified.");
                else
                {
                    ClassifiedCar? classWinner = summary.Classification.FirstOrDefault(x => x.CarClass == c.CarClass && x.Finished());
                    int behind = classWinner == null ? 0 : classWinner.Laps - c.Laps;
                    string gap = classWinner == null || behind <= 0 ? "" : $", {Count(behind, "lap")} behind class winners {classWinner.TeamName}";
                    paragraphs.Add($"They finished {Ordinal(c.ClassPosition)} in class and {Ordinal(c.Position)} overall{gap}.");
                }
                int issues = Issues(c.CarId);
                if (issues > 0) paragraphs.Add($"{Capital(Count(issues, "issue"))} slowed the car.");
                return Make(c, new[] { $"Misery for {c.TeamName}", $"{c.TeamName} fall short", $"A day to forget for {c.TeamName}" }, paragraphs);
            }

            private Story? VeteranPerformance()
            {
                var best = Field
                    .Where(x => x.Finished() && x.ClassPosition <= 3)
                    .SelectMany(x => DriversOf(x.CarId).Select(d => (Car: x, Driver: d)))
                    .Where(x => x.Driver.Age >= 36)
                    .OrderByDescending(x => x.Driver.Age).ThenBy(x => x.Car.Position)
                    .FirstOrDefault();
                if (best.Car == null) return null;
                ClassifiedCar c = best.Car;
                Driver d = best.Driver;
                int laps = LapsDriven(c.CarId, d.Id);
                var paragraphs = new List<string>
                {
                    $"{d.Name}, {d.Age}, helped {c.TeamName} to {Ordinal(c.ClassPosition)} in class {ClassName(c.CarClass)}.",
                    $"{d.Name} drove {laps} of the {ModelName(c.ModelId)}'s {c.Laps} laps.",
                };
                IReadOnlyList<string> others = DriverNames(c.CarId, c.TeamId).Where(n => n != d.Name).ToArray();
                if (others.Count > 0) paragraphs.Add($"{List(others)} shared the rest.");
                return Make(c, new[] { $"{d.Name} still has it at {d.Age}", $"Age no barrier for {d.Name}", $"{d.Age}-year-old {d.Name} on the podium" }, paragraphs);
            }

            private Story? RookieDebut()
            {
                var best = Field
                    .Where(x => x.Finished() && x.ClassPosition <= 5)
                    .SelectMany(x => DriversOf(x.CarId).Select(d => (Car: x, Driver: d)))
                    .Where(x => x.Driver.Age <= 22 && FirstRace(x.Driver.Id))
                    .OrderBy(x => x.Car.ClassPosition).ThenBy(x => x.Driver.Age).ThenBy(x => x.Car.Position)
                    .FirstOrDefault();
                if (best.Car == null) return null;
                ClassifiedCar c = best.Car;
                Driver d = best.Driver;
                var paragraphs = new List<string>
                {
                    $"{d.Age}-year-old {d.Name} made a Gauntlet debut with {c.TeamName}, finishing {Ordinal(c.ClassPosition)} in class {ClassName(c.CarClass)}.",
                    $"{d.Name} drove {LapsDriven(c.CarId, d.Id)} of the {ModelName(c.ModelId)}'s {c.Laps} laps.",
                };
                return Make(c, new[] { $"Dream debut for {d.Name}", $"Rookie {d.Name} shines", $"{d.Name} arrives" }, paragraphs);
            }

            /// <summary>A feature story, with the team's roster descriptor as its standfirst.</summary>
            private Story Make(ClassifiedCar c, IReadOnlyList<string> headlines, IReadOnlyList<string> paragraphs) => new Story
            {
                Headline = Pick(headlines),
                Standfirst = descriptors.TryGetValue(c.TeamName, out string? d) ? Capital(d) : "",
                Paragraphs = paragraphs,
                TeamId = c.TeamId,
            };

            private static string Introduce(ClassifiedCar c) => c.TeamName;

            private void AddCrew(List<string> paragraphs, ClassifiedCar c)
            {
                IReadOnlyList<string> crew = DriverNames(c.CarId, c.TeamId);
                if (crew.Count > 1) paragraphs.Add($"{List(crew)} shared the {ModelName(c.ModelId)}.");
                else if (crew.Count == 1) paragraphs.Add($"{crew[0]} drove the {ModelName(c.ModelId)}.");
            }

            // ------------------------------------------------------------------ the player

            private PlayerCoverage Coverage(ClassifiedCar p)
            {
                PlayerCoverage tier = CoverageFor(summary.PrestigeAfter);
                bool strong = p.Finished() && (p.ClassPosition == 1 || p.Position <= 3);
                if (strong && tier < PlayerCoverage.Front) tier++;
                return tier;
            }

            private Story PlayerStory(ClassifiedCar p, PlayerCoverage coverage)
            {
                string team = p.TeamName.Length > 0 ? p.TeamName : "The newcomers";
                string cls = ClassName(p.CarClass), model = ModelName(p.ModelId);
                int grid = GridPosition(p.CarId);
                int? lastYear = LastYearPosition();
                string reason = p.RetirementReason == FailureType.Crash ? "a crash" : "a mechanical failure";

                if (coverage == PlayerCoverage.Footnote)
                {
                    string line = p.Retired
                        ? $"{team} retired from class {cls} after {p.Laps} laps with {reason}."
                        : p.Unclassified
                        ? $"{team} were not classified in class {cls} after {p.Laps} laps."
                        : $"{team} were {Ordinal(p.ClassPosition)} in class {cls} and {Ordinal(p.Position)} overall after {p.Laps} laps.";
                    return new Story { Paragraphs = new[] { line }, TeamId = p.TeamId };
                }

                string headline;
                if (p.Retired) headline = p.RetirementReason == FailureType.Crash ? $"{team} crash out" : $"{team} retire on lap {p.Laps}";
                else if (p.Unclassified) headline = $"{team} not classified";
                else if (p.ClassPosition == 1) headline = $"{team} win class {cls}";
                else if (p.ClassPosition <= 3) headline = $"{team} take {Ordinal(p.ClassPosition)} in class {cls}";
                else if (lastYear != null && lastYear.Value - p.Position >= 5) headline = $"{team} climb to {Ordinal(p.Position)}";
                else headline = $"{team} finish {Ordinal(p.Position)}";

                var paragraphs = new List<string>();
                string start = grid > 0 ? $"Starting {Ordinal(grid)}, " : "";
                paragraphs.Add(p.Retired
                    ? $"{team} started {(grid > 0 ? Ordinal(grid) : "the race")} and ran {p.Laps} laps before {reason} ended their race."
                    : p.Unclassified
                    ? $"{start}{team} ran {p.Laps} laps but were not classified as finishers."
                    : $"{start}{team} finished {Ordinal(p.Position)} overall and {Ordinal(p.ClassPosition)} in class {cls} with {p.Laps} laps.");

                IReadOnlyList<string> crew = DriverNames(p.CarId, p.TeamId);
                int issues = summary.Issues.Count;
                string second = crew.Count > 1 ? $"{List(crew)} shared the {model}." : crew.Count == 1 ? $"{crew[0]} drove the {model}." : $"The team ran the {model}.";
                second += issues > 0 ? $" It picked up {Count(issues, "issue")} along the way." : " It ran without an issue.";
                paragraphs.Add(second);

                if (lastYear != null && p.Finished())
                {
                    int change = lastYear.Value - p.Position;
                    if (change > 0) paragraphs.Add($"That is {Count(change, "place")} better than last year.");
                    else if (change < 0) paragraphs.Add($"Last year they were {Ordinal(lastYear.Value)}.");
                }

                string standfirst = "";
                if (coverage == PlayerCoverage.Front)
                {
                    (string Name, int Laps, int Stints)? anchor = TopDriver(p.CarId, p.TeamId);
                    if (anchor != null && crew.Count > 1)
                        paragraphs.Add($"{anchor.Value.Name} did the most, {anchor.Value.Laps} laps across {Count(anchor.Value.Stints, "stint")}, and the car stopped {Count(summary.PitStops.Count, "time")}.");
                    standfirst = p.Retired
                        ? $"The {model} is out after {p.Laps} laps."
                        : p.Unclassified
                        ? $"The {model} ran {p.Laps} laps but is not classified."
                        : crew.Count > 0 ? $"{List(crew)} {(crew.Count == 1 ? "brings" : "bring")} the {model} home {Ordinal(p.Position)}." : $"The {model} comes home {Ordinal(p.Position)}.";
                }
                return new Story { Headline = headline, Standfirst = standfirst, Paragraphs = paragraphs, TeamId = p.TeamId };
            }

            private int? LastYearPosition()
            {
                RaceHistoryEntry? last = after.RaceHistory.LastOrDefault(h => h.Year < summary.Year);
                RaceResult? r = last?.Results.FirstOrDefault(x => x.TeamId == GameActions.PlayerTeamId);
                return r?.Position;
            }

            // ------------------------------------------------------------------ race facts

            /// <summary>Grid slot, from 1, or 0 if unknown.</summary>
            private int GridPosition(string carId) =>
                race.CarIndexById != null && race.CarIndexById.TryGetValue(carId, out int i) ? i + 1 : 0;

            private int Slot(string carId) => race.CarIndexById != null && race.CarIndexById.TryGetValue(carId, out int i) ? i : -1;

            private int LapsLed(string carId)
            {
                int slot = Slot(carId);
                if (slot < 0 || race.PositionHistory == null) return 0;
                return race.PositionHistory.Count(row => slot < row.Count && row[slot] == 1);
            }

            /// <summary>Overall position a share of the way through the leader's laps, or 0.</summary>
            private int PositionAtShare(string carId, double share)
            {
                int slot = Slot(carId);
                IReadOnlyList<IReadOnlyList<int>>? rows = race.PositionHistory;
                if (slot < 0 || rows == null || rows.Count == 0) return 0;
                IReadOnlyList<int> row = rows[Math.Min(rows.Count - 1, (int)(rows.Count * share))];
                return slot < row.Count ? row[slot] : 0;
            }

            /// <summary>Where a car stood in its class as the leader finished lap <paramref name="lap"/>, or 0.</summary>
            private int ClassPositionAtLap(ClassifiedCar car, int lap)
            {
                int slot = Slot(car.CarId);
                IReadOnlyList<IReadOnlyList<int>>? rows = race.PositionHistory;
                if (slot < 0 || rows == null || rows.Count == 0 || lap <= 0) return 0;
                IReadOnlyList<int> row = rows[Math.Min(rows.Count, lap) - 1];
                if (slot >= row.Count) return 0;
                int ahead = summary.Classification.Count(o =>
                    o.CarClass == car.CarClass && o.CarId != car.CarId && Slot(o.CarId) is int s && s >= 0 && s < row.Count && row[s] < row[slot]);
                return ahead + 1;
            }

            private int Issues(string carId) => race.Events?.Count(e => e.CarId == carId && e.Type == RaceEventType.Issue) ?? 0;

            private double PrestigeBefore(string teamId) => teamsBefore.TryGetValue(teamId, out BaseTeam? t) ? t.Prestige : 0;

            /// <summary>The car's rank by prestige among its class, from 1, and how many raced in the class.</summary>
            private (int Rank, int Of) PrestigeRank(ClassifiedCar car)
            {
                ClassifiedCar[] cls = summary.Classification.Where(c => c.CarClass == car.CarClass)
                    .OrderByDescending(c => PrestigeBefore(c.TeamId)).ThenBy(c => GridPosition(c.CarId)).ToArray();
                return (Array.FindIndex(cls, c => c.CarId == car.CarId) + 1, cls.Length);
            }

            private IReadOnlyList<Stint> StintsOf(string carId) =>
                race.Stints != null && race.Stints.TryGetValue(carId, out IReadOnlyList<Stint>? s) ? s : Array.Empty<Stint>();

            private static int Length(Stint s) => Math.Max(0, s.EndLap - s.StartLap + 1);

            private int LapsDriven(string carId, string driverId) => StintsOf(carId).Where(s => s.DriverId == driverId).Sum(Length);

            /// <summary>Drivers who drove the car, in the order they first got in. The owner-driver is left out.</summary>
            private IReadOnlyList<Driver> DriversOf(string carId) => StintsOf(carId)
                .Where(s => Length(s) > 0)
                .Select(s => s.DriverId).Distinct()
                .Select(id => drivers.TryGetValue(id, out Driver? d) ? d : null)
                .Where(d => d != null).Select(d => d!).ToArray();

            private string DriverName(string id, string teamId)
            {
                if (id == RaceEntry.PlayerCharacterDriverId)
                    return teamsBefore.TryGetValue(teamId, out BaseTeam? t) && t is PlayerTeam p ? p.PlayerName : "";
                return drivers.TryGetValue(id, out Driver? d) ? d.Name : "";
            }

            private IReadOnlyList<string> DriverNames(string carId, string teamId) => StintsOf(carId)
                .Where(s => Length(s) > 0)
                .Select(s => s.DriverId).Distinct()
                .Select(id => DriverName(id, teamId))
                .Where(n => n.Length > 0).ToArray();

            /// <summary>The driver with the most laps in the car, with their laps and stints.</summary>
            private (string Name, int Laps, int Stints)? TopDriver(string carId, string teamId)
            {
                var top = StintsOf(carId).Where(s => Length(s) > 0).GroupBy(s => s.DriverId)
                    .Select(g => (Id: g.Key, Laps: g.Sum(Length), Stints: g.Count()))
                    .OrderByDescending(x => x.Laps).FirstOrDefault();
                if (top.Id == null) return null;
                string name = DriverName(top.Id, teamId);
                return name.Length == 0 ? ((string, int, int)?)null : (name, top.Laps, top.Stints);
            }

            /// <summary>True when the driver has no stint in any earlier race on record.</summary>
            private bool FirstRace(string driverId) => after.RaceHistory
                .Where(h => h.Year < summary.Year && h.Stints != null)
                .All(h => !h.Stints!.Values.Any(list => list.Any(s => s.DriverId == driverId)));

            private string TeamName(string teamId) =>
                summary.Classification.FirstOrDefault(c => c.TeamId == teamId)?.TeamName
                ?? (teamsBefore.TryGetValue(teamId, out BaseTeam? t) ? t.Name : teamId);

            private string ModelName(string modelId) =>
                (before.CarModels.FirstOrDefault(m => m.Id == modelId) ?? CarModels.All.FirstOrDefault(m => m.Id == modelId))?.Name ?? modelId;

            private string Pick(IReadOnlyList<string> options) => options[rng.Below(options.Count)];
        }

        // ------------------------------------------------------------------ words

        public static string ClassName(CarClass cls) => cls.ToString();

        public static string Ordinal(int n)
        {
            int tens = n % 100;
            string suffix = tens >= 11 && tens <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
            return n.ToString(CultureInfo.InvariantCulture) + suffix;
        }

        private static readonly string[] Nths = { "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth" };

        /// <summary>"first" to "tenth", then "11th".</summary>
        public static string Nth(int n) => n >= 1 && n <= Nths.Length ? Nths[n - 1] : Ordinal(n);

        private static readonly string[] Small = { "no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" };

        /// <summary>"one lap", "three laps", "12 laps".</summary>
        public static string Count(int n, string noun)
        {
            string number = n >= 0 && n < Small.Length ? Small[n] : n.ToString(CultureInfo.InvariantCulture);
            return $"{number} {noun}{(n == 1 ? "" : "s")}";
        }

        /// <summary>"A", "A and B", "A, B and C".</summary>
        public static string List(IReadOnlyList<string> items) =>
            items.Count <= 1 ? string.Concat(items) : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[items.Count - 1];

        /// <summary>Race seconds as m:ss.fff.</summary>
        public static string LapTimeText(double seconds)
        {
            int ms = (int)Math.Round(seconds * 1000);
            return $"{ms / 60000}:{ms / 1000 % 60:00}.{ms % 1000:000}";
        }

        private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>A seed from the race itself, so the same race prints the same paper.</summary>
        private static uint Seed(RaceSummary summary)
        {
            uint h = 2166136261;
            void Mix(string s)
            {
                foreach (char c in s) h = (h ^ c) * 16777619;
            }
            Mix(summary.Year.ToString(CultureInfo.InvariantCulture));
            foreach (ClassifiedCar c in summary.Classification.Take(10))
            {
                Mix(c.CarId);
                Mix(c.Laps.ToString(CultureInfo.InvariantCulture));
            }
            return h == 0 ? 1 : h;
        }

        /// <summary>xorshift32: the same numbers on every runtime.</summary>
        private sealed class Rng
        {
            private uint state;

            public Rng(uint seed)
            {
                state = seed;
            }

            public int Below(int n)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return (int)(state % (uint)n);
            }
        }
    }
}
