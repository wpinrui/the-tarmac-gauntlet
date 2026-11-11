#nullable enable
using System;
using System.Collections.Generic;

namespace TarmacGauntlet.Simulation
{
    /// <summary>How much of the paper the player's team gets, which grows with prestige (GDD §9, Newspaper).</summary>
    public enum PlayerCoverage
    {
        /// <summary>The player didn't race.</summary>
        None,
        /// <summary>One line at the foot of the page.</summary>
        Footnote,
        /// <summary>A short story in a column.</summary>
        Column,
        /// <summary>An article on the front page beside the winner.</summary>
        Front,
        /// <summary>The player won: the lead story is theirs.</summary>
        Lead,
    }

    /// <summary>The kinds of feature story the paper picks one of (GDD §9, Newspaper).</summary>
    public enum FeatureKind
    {
        MostUnlucky,
        MostSurprising,
        BiggestMover,
        BiggestDisappointment,
        VeteranPerformance,
        RookieDebut,
    }

    public sealed record Story
    {
        public string Headline { get; init; } = "";
        /// <summary>The line under the headline, or empty.</summary>
        public string Standfirst { get; init; } = "";
        public IReadOnlyList<string> Paragraphs { get; init; } = Array.Empty<string>();
        /// <summary>The team the story is about.</summary>
        public string TeamId { get; init; } = "";
    }

    public sealed record PodiumPlace
    {
        public string TeamId { get; init; } = "";
        public string TeamName { get; init; } = "";
        public string ModelName { get; init; } = "";
        public int Laps { get; init; }
    }

    public sealed record ClassPodium
    {
        public CarClass CarClass { get; init; }
        /// <summary>The top three finishers, class winner first.</summary>
        public IReadOnlyList<PodiumPlace> Places { get; init; } = Array.Empty<PodiumPlace>();
    }

    /// <summary>The paper printed after a race. It is written once, when the race settles, and kept with the career.</summary>
    public sealed record Newspaper
    {
        public int Year { get; init; }
        /// <summary>The overall winner's story, always the lead.</summary>
        public Story Lead { get; init; } = new Story();
        /// <summary>Every class that raced, quickest class first.</summary>
        public IReadOnlyList<ClassPodium> Podiums { get; init; } = Array.Empty<ClassPodium>();
        public FeatureKind? FeatureKind { get; init; }
        /// <summary>One feature from the template pool, or null if no team fitted any.</summary>
        public Story? Feature { get; init; }
        public PlayerCoverage Coverage { get; init; }
        /// <summary>
        /// The player's story. A footnote is one paragraph and no headline. Null when the player didn't race or won,
        /// since the lead is then theirs.
        /// </summary>
        public Story? PlayerStory { get; init; }
    }
}
