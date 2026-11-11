#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TarmacGauntlet.Simulation;

namespace TarmacGauntlet.Career
{
    /// <summary>What a career's save looks like from the outside, read without loading the whole game.</summary>
    public sealed record CareerInfo
    {
        public string CareerId { get; init; } = "";
        public string TeamName { get; init; } = "";
        public string PlayerName { get; init; } = "";
        public string? Logo { get; init; }
        public string TeamColour { get; init; } = "";
        public int Year { get; init; }
        public string LastSavedAt { get; init; } = ""; // ISO 8601, UTC
    }

    /// <summary>
    /// Saves on disk, one folder per career: <c>root/&lt;careerId&gt;/autosave.json</c> holds the game and
    /// <c>info.json</c> the <see cref="CareerInfo"/>. Every career has its own autosave, so careers never share one.
    /// </summary>
    public sealed class CareerStore
    {
        private const string SaveFile = "autosave.json", InfoFile = "info.json";

        public string Root { get; }

        public CareerStore(string root)
        {
            Root = root;
        }

        public static string NewCareerId() => Guid.NewGuid().ToString("N");

        /// <summary>Every career with a readable save, the most recently saved first.</summary>
        public IReadOnlyList<CareerInfo> List()
        {
            if (!Directory.Exists(Root)) return Array.Empty<CareerInfo>();
            var careers = new List<CareerInfo>();
            foreach (string dir in Directory.GetDirectories(Root))
            {
                string infoPath = Path.Combine(dir, InfoFile);
                if (!File.Exists(infoPath) || !File.Exists(Path.Combine(dir, SaveFile))) continue;
                try
                {
                    CareerInfo? info = JsonConvert.DeserializeObject<CareerInfo>(File.ReadAllText(infoPath));
                    if (info != null) careers.Add(info with { CareerId = Path.GetFileName(dir) });
                }
                catch (JsonException)
                {
                    // A damaged info file hides that career rather than breaking the menu.
                }
            }
            return careers.OrderByDescending(c => c.LastSavedAt, StringComparer.Ordinal).ToList();
        }

        public CareerInfo? Latest() => List().FirstOrDefault();

        public GameState Load(string careerId) => SaveJson.Deserialize(File.ReadAllText(Path.Combine(Folder(careerId), SaveFile)));

        /// <summary>Writes the career's autosave, stamped with the save time. A crash mid-write leaves the previous save whole.</summary>
        public void Save(string careerId, GameState game, DateTime? now = null)
        {
            string stamp = (now ?? DateTime.UtcNow).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            GameState stamped = game with
            {
                Meta = game.Meta with
                {
                    Version = SaveJson.Version,
                    CreatedAt = string.IsNullOrEmpty(game.Meta.CreatedAt) ? stamp : game.Meta.CreatedAt,
                    LastSavedAt = stamp,
                },
            };
            PlayerTeam? player = game.Teams.OfType<PlayerTeam>().FirstOrDefault();
            var info = new CareerInfo
            {
                CareerId = careerId,
                TeamName = player?.Name ?? "",
                PlayerName = player?.PlayerName ?? "",
                Logo = player?.Logo,
                TeamColour = player?.TeamColour ?? "",
                Year = game.CurrentYear,
                LastSavedAt = stamp,
            };

            string folder = Folder(careerId);
            Directory.CreateDirectory(folder);
            WriteAtomically(Path.Combine(folder, SaveFile), SaveJson.Serialize(stamped));
            WriteAtomically(Path.Combine(folder, InfoFile), JsonConvert.SerializeObject(info, Formatting.Indented));
        }

        public void Delete(string careerId)
        {
            string folder = Folder(careerId);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        private string Folder(string careerId)
        {
            if (string.IsNullOrEmpty(careerId) || careerId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || careerId.Contains(".."))
                throw new ArgumentException($"Bad career id \"{careerId}\".", nameof(careerId));
            return Path.Combine(Root, careerId);
        }

        private static void WriteAtomically(string path, string text)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
    }
}
