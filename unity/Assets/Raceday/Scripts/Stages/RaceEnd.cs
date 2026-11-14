using System.Collections.Generic;
using TarmacGauntlet.Simulation;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The rules for how the race ends, the same whatever you are looking at: driving, the broadcast or live timing.
    /// Pure logic on the <see cref="RaceDay"/> and the seconds since the race time ran out, so it is checked outside
    /// Unity (tools/race-end-check). Your race is over once the time is up and your car has taken the flag or is out,
    /// whoever drove it. From then on live timing is closed, so the cooldown lap and the podiums are seen. The cooldown
    /// lap ends once your car is parked and its crew have received it, with every car still running past the flag.
    /// With your car out of the race, or none, there is nothing to receive: it ends <see cref="RetiredHold"/> after the
    /// last car takes the flag, and in the meantime the camera follows the race leader home.
    /// </summary>
    public static class RaceEnd
    {
        /// <summary>The race stops waiting for a car to come round to the flag this long after the time runs out, s.</summary>
        public const float FlagWait = 400f;
        /// <summary>With your car out of the race, the cooldown lap goes on this long after the last car takes the flag, s.</summary>
        public const float RetiredHold = 8f;

        /// <summary>Your car, or null with none in the race.</summary>
        public static CarRaceLog Mine(RaceDay day)
        {
            if (day == null) return null;
            foreach (CarRaceLog log in day.Cars) if (log.Car.Player) return log;
            return null;
        }

        /// <summary>
        /// True once the time is up and your race is over: your car has taken the flag or is out. With no car of
        /// yours in the race, once the first car has taken the flag.
        /// </summary>
        public static bool PlayerDone(RaceDay day, float? sinceTimeUp)
        {
            if (day == null || sinceTimeUp == null) return false;
            CarRaceLog mine = Mine(day);
            return mine != null ? mine.Flagged || mine.Retired : day.FlagOut;
        }

        /// <summary>
        /// True once every car still in the race has taken the flag, or been given up on <see cref="FlagWait"/> after
        /// the time ran out: the results stand.
        /// </summary>
        public static bool FieldFlagged(RaceDay day, float? sinceTimeUp)
        {
            if (day == null || sinceTimeUp == null) return false;
            return sinceTimeUp.Value > FlagWait || day.AllFlagged;
        }

        /// <summary>True while live timing may be open: not once your race is over after the time ran out.</summary>
        public static bool TimingAllowed(RaceDay day, float? sinceTimeUp) => !PlayerDone(day, sinceTimeUp);

        /// <summary>
        /// True once the cooldown lap is over and the race can be settled. <paramref name="received"/>: your car is
        /// parked in its box and its crew have waved it home. <paramref name="sinceFieldFlagged"/>: seconds since
        /// <see cref="FieldFlagged"/> first held, or null while it doesn't.
        /// </summary>
        public static bool CooldownOver(RaceDay day, float? sinceTimeUp, bool received, float? sinceFieldFlagged)
        {
            if (!FieldFlagged(day, sinceTimeUp) || sinceFieldFlagged == null) return false;
            CarRaceLog mine = Mine(day);
            if (mine == null || mine.Retired) return sinceFieldFlagged.Value >= RetiredHold;
            return received;
        }

        /// <summary>
        /// The car to show once the time is up: yours while it is in the race, else the race leader, the winner once
        /// the flag is out. -1 before the time is up.
        /// </summary>
        public static int ShowAfterFlag(RaceDay day, float? sinceTimeUp)
        {
            if (day == null || sinceTimeUp == null) return -1;
            CarRaceLog mine = Mine(day);
            if (mine != null && !mine.Retired) return mine.Index;
            IReadOnlyList<CarRaceLog> order = day.Classify();
            foreach (CarRaceLog log in order) if (!log.Retired) return log.Index;
            return order.Count > 0 ? order[0].Index : -1;
        }
    }
}
