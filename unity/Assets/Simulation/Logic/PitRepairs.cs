#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>Which active issues a stop fixes.</summary>
    public enum RepairMode
    {
        /// <summary>The crew decide (<see cref="PitRepairs.Choose"/>).</summary>
        Crew,
        All,
        None,
    }

    /// <summary>What the car does after the stop, for the crew to weigh a repair against.</summary>
    public sealed record StintOutlook
    {
        /// <summary>Laps the car will run before its next stop.</summary>
        public double Laps { get; init; }
        /// <summary>Seconds a lap takes.</summary>
        public double LapSeconds { get; init; }
        /// <summary>Litres a lap uses.</summary>
        public double LitresPerLap { get; init; }
        /// <summary>Seconds in the box each extra litre costs at the next stop.</summary>
        public double SecondsPerLitre { get; init; }
        /// <summary>How much the crew and the Engineer skill scale the work (<see cref="PitStop.CrewMultiplier"/>).</summary>
        public double CrewMultiplier { get; init; } = 1;
    }

    /// <summary>
    /// The crew's call on repairs. They always fix an issue that cuts Reliability, as it could end the race. Any
    /// other issue they fix only if it costs less time in the box than it loses over the stint to come: lap time
    /// from Power and Handling, and the extra fuel a Fuel Efficiency cut means at the next stop.
    /// </summary>
    public static class PitRepairs
    {
        /// <summary>The template ids a stop fixes, in the order the spares go to them: the race-ending risks first.</summary>
        public static IReadOnlyList<string> Choose(RepairMode mode, IReadOnlyList<ActiveIssue> active, CarStats stats, StintOutlook stint)
        {
            if (mode == RepairMode.None) return Array.Empty<string>();
            if (mode == RepairMode.All) return active.Select(i => i.TemplateId).ToArray();
            var risks = new List<string>();
            var worth = new List<string>();
            foreach (ActiveIssue issue in active)
            {
                IssueTemplate? t = IssueTemplates.AllIssueTemplates.FirstOrDefault(x => x.Id == issue.TemplateId);
                if (t == null) continue;
                if (t.StatDebuffs.ContainsKey(CarStatKey.Reliability)) risks.Add(t.Id);
                else if (Lost(t, stats, stint) > t.WorkUnits * stint.CrewMultiplier) worth.Add(t.Id);
            }
            return risks.Concat(worth).ToArray();
        }

        /// <summary>Seconds an issue costs over the stint if it is left on the car.</summary>
        public static double Lost(IssueTemplate t, CarStats stats, StintOutlook stint)
        {
            CarStats hurt = stats;
            foreach (KeyValuePair<CarStatKey, double> kv in t.StatDebuffs)
                hurt = hurt.With(kv.Key, hurt.Get(kv.Key) * (1 - kv.Value));
            double slower = LapTime.BaseLapTime(hurt) / LapTime.BaseLapTime(stats) - 1;
            double pace = stint.Laps * stint.LapSeconds * Math.Max(slower, 0);
            double fuel = 0;
            if (t.StatDebuffs.TryGetValue(CarStatKey.FuelEfficiency, out double cut) && cut < 1)
                fuel = stint.Laps * stint.LitresPerLap * (1 / (1 - cut) - 1) * stint.SecondsPerLitre;
            return pace + fuel;
        }
    }
}
