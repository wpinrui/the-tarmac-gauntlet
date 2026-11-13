using System;
using System.Globalization;
using UnityEngine;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// When the game was built, in the player's local time. Each player build writes the BuildStamp resource
    /// (Editor/BuildStampWriter.cs). The editor has no build of its own, so it shows nothing.
    /// </summary>
    internal static class BuildStamp
    {
        public const string ResourceName = "BuildStamp";

        public static string Text()
        {
            if (Application.isEditor) return null;
            string utc = Resources.Load<TextAsset>(ResourceName)?.text;
            if (!DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime built)) return null;
            return built.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture);
        }
    }
}
