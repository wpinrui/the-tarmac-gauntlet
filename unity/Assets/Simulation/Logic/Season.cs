#nullable enable
using System;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>Moves the career from one year's race to the next.</summary>
    public static class Season
    {
        /// <summary>
        /// Ends the year after its race: runs <see cref="YearAdvance.AdvanceYear"/>, moves to the next year and back to
        /// the pre-race phase. It only acts in the post-race phase, so a year advances exactly once per race. Contracts
        /// of drivers who retire are dropped.
        /// </summary>
        public static GameState AdvanceYear(GameState game, Func<double>? random = null)
        {
            if (game.Phase != GamePhase.PostRace) return game;
            random ??= DefaultRandom.Next;

            bool playerHasWon = game.RaceHistory.Any(r => r.Results.Any(x => x.TeamId == GameActions.PlayerTeamId && x.Position == 1));
            YearAdvanceResult next = YearAdvance.AdvanceYear(new YearAdvanceInput
            {
                Drivers = game.Drivers,
                Contracts = game.Contracts,
                Teams = game.Teams,
                CarModels = game.CarModels,
                RookieSpecs = GameInit.RookieSpecs(game.CurrentYear, DriverLifecycle.AnnualRetirements, random),
                PlayerHasWon = playerHasWon,
                CurrentYear = game.CurrentYear,
                RaceHistory = game.RaceHistory,
            }, random);

            var retired = next.RetiredDriverIds.ToHashSet();
            return game with
            {
                CurrentYear = game.CurrentYear + 1,
                Phase = GamePhase.PreRace,
                Drivers = next.Drivers,
                Contracts = next.Contracts.Where(c => !retired.Contains(c.DriverId)).ToArray(),
                Teams = next.Teams,
                CarMarket = game.CarMarket with { UsedListings = next.UsedListings },
                RaceHistory = next.RaceHistory,
            };
        }
    }
}
