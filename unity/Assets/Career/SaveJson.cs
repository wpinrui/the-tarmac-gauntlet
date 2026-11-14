#nullable enable
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using TarmacGauntlet.Simulation;

namespace TarmacGauntlet.Career
{
    /// <summary>Turns a whole <see cref="GameState"/> into JSON and back.</summary>
    public static class SaveJson
    {
        /// <summary>The save format written today. Raise it when the format changes and teach <see cref="Migrate"/> the old one.</summary>
        public const int Version = 3;

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter(), new TeamConverter() },
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            FloatParseHandling = FloatParseHandling.Double,
            // Save times are ISO strings in the game state; keep them strings.
            DateParseHandling = DateParseHandling.None,
        };

        public static string Serialize(GameState game) => JsonConvert.SerializeObject(game, Settings);

        public static GameState Deserialize(string json)
        {
            JObject root;
            using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
                root = JObject.Load(reader);
            int version = root["Meta"]?["Version"]?.Value<int>() ?? 0;
            if (version > Version)
                throw new InvalidOperationException($"The save is format {version}, newer than this game's format {Version}.");
            Migrate(root, version);
            GameState game = root.ToObject<GameState>(JsonSerializer.Create(Settings))
                ?? throw new InvalidOperationException("The save is empty.");
            return WithRaceNumbers(game);
        }

        /// <summary>A race saved part way. Race saves are not migrated: an old one that no longer reads is thrown away.</summary>
        public static string SerializeRace(RaceSave race) => JsonConvert.SerializeObject(race, Settings);

        public static RaceSave DeserializeRace(string json) =>
            JsonConvert.DeserializeObject<RaceSave>(json, Settings) ?? throw new InvalidOperationException("The race save is empty.");

        /// <summary>Saves made before race numbers existed get this season's numbers on load.</summary>
        private static GameState WithRaceNumbers(GameState game) =>
            game.Teams.All(t => t.RaceNumber > 0)
                ? game
                : game with { Teams = RaceNumbers.Assign(game.Teams, RaceNumbers.WinnerOf(game.RaceHistory, game.CurrentYear - 1)) };

        /// <summary>Brings an older save up to <see cref="Version"/> in place.</summary>
        private static void Migrate(JObject root, int version)
        {
            if (version < 2) DropCutModels(root);
            if (version < 3) AddTeamStatus(root);
        }

        /// <summary>Format 3 gave AI teams a factory, satellite or privateer status. Older saves take it from the roster.</summary>
        private static void AddTeamStatus(JObject root)
        {
            foreach (JObject team in (root["Teams"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                string name = team["Name"]?.Value<string>() ?? "";
                AiTeamEntry? entry = AiTeamRoster.All.FirstOrDefault(e => e.Name == name);
                if (entry != null && team["Kind"]?.Value<string>() != "player") team["Status"] = entry.Status.ToString();
            }
        }

        /// <summary>
        /// Format 2 cut the Kaiser Sceptre, Tora Zero and Liberty Fortress. Each one's stand-in keeps its brand and class.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<string, string> CutModels = new System.Collections.Generic.Dictionary<string, string>
        {
            { "e-09", "e-07" },
            { "c-18", "c-20" },
            { "a-26", "a-28" },
        };

        /// <summary>
        /// Takes the cut models out of an older save. An AI team that ran one moves to its car from today's roster,
        /// and so do its cars and its past results. Any other car on a cut model becomes its stand-in.
        /// Market listings for a cut model are dropped.
        /// </summary>
        private static void DropCutModels(JObject root)
        {
            var teamModel = new System.Collections.Generic.Dictionary<string, string>();
            foreach (JObject team in (root["Teams"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                string? modelId = team["ModelId"]?.Value<string>();
                if (modelId == null || !CutModels.TryGetValue(modelId, out string? standIn)) continue;
                string name = team["Name"]?.Value<string>() ?? "";
                string replacement = AiTeamRoster.All.FirstOrDefault(e => e.Name == name)?.ModelId ?? standIn;
                if (CutModels.ContainsKey(replacement)) replacement = standIn;
                team["ModelId"] = replacement;
                string? id = team["Id"]?.Value<string>();
                if (id != null) teamModel[id] = replacement;
                foreach (JObject car in (team["Cars"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                    if (CutModels.ContainsKey(car["ModelId"]?.Value<string>() ?? "")) car["ModelId"] = replacement;
            }

            if (root["CarModels"] is JArray models)
                foreach (JToken m in models.Where(m => CutModels.ContainsKey(m["Id"]?.Value<string>() ?? "")).ToList()) m.Remove();
            if (root["CarMarket"] is JObject market)
                foreach (JProperty list in market.Properties().ToList())
                    foreach (JToken listing in list.Value.SelectTokens("..ModelId").Where(t => CutModels.ContainsKey(t.Value<string>() ?? "")).ToList())
                        ListingOf(listing)?.Remove();

            foreach (JToken modelId in root.SelectTokens("..ModelId").Where(t => CutModels.ContainsKey(t.Value<string>() ?? "")).ToList())
            {
                string? teamId = modelId.Parent?.Parent?["TeamId"]?.Value<string>();
                modelId.Replace(teamId != null && teamModel.TryGetValue(teamId, out string? own) ? own : CutModels[modelId.Value<string>()!]);
            }
        }

        /// <summary>The listing a ModelId belongs to, when that listing sits in an array.</summary>
        private static JToken? ListingOf(JToken modelId)
        {
            JToken? listing = modelId.Parent?.Parent;
            return listing?.Parent is JArray ? listing : null;
        }

        /// <summary>Teams are player or AI; the JSON names which with a "Kind" field.</summary>
        private sealed class TeamConverter : JsonConverter
        {
            private const string Player = "player", Ai = "ai";

            /// <summary>Writes a team's own fields, without this converter, so it is not asked again.</summary>
            private static readonly JsonSerializer Plain = JsonSerializer.Create(new JsonSerializerSettings
            {
                Converters = { new StringEnumConverter() },
                NullValueHandling = NullValueHandling.Ignore,
                DateParseHandling = DateParseHandling.None,
            });

            // Lists hold BaseTeam, but writing sees each team's concrete type.
            public override bool CanConvert(Type objectType) => typeof(BaseTeam).IsAssignableFrom(objectType);

            public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
            {
                if (value == null) { writer.WriteNull(); return; }
                JObject team = JObject.FromObject(value, Plain);
                team.AddFirst(new JProperty("Kind", value is PlayerTeam ? Player : Ai));
                team.WriteTo(writer);
            }

            public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return null;
                JObject team = JObject.Load(reader);
                string kind = team["Kind"]?.Value<string>() ?? "";
                Type type = kind switch
                {
                    Player => typeof(PlayerTeam),
                    Ai => typeof(AITeam),
                    _ => throw new JsonSerializationException($"Unknown team kind \"{kind}\"."),
                };
                team.Remove("Kind");
                return team.ToObject(type, Plain);
            }
        }
    }
}
