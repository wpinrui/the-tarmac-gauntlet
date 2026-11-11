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
        public const int Version = 1;

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

        /// <summary>Saves made before race numbers existed get this season's numbers on load.</summary>
        private static GameState WithRaceNumbers(GameState game) =>
            game.Teams.All(t => t.RaceNumber > 0)
                ? game
                : game with { Teams = RaceNumbers.Assign(game.Teams, RaceNumbers.WinnerOf(game.RaceHistory, game.CurrentYear - 1)) };

        /// <summary>Brings an older save up to <see cref="Version"/> in place. Version 1 is the first, so nothing to do yet.</summary>
        private static void Migrate(JObject root, int version)
        {
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
