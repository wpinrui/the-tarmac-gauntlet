using System.Linq;
using UnityEditor;

namespace TarmacGauntlet.Management.Editor
{
    /// <summary>
    /// Keeps the game's scenes in the build list, the Team HQ first, so the menus can load the race and the race can
    /// load the hub again.
    /// </summary>
    [InitializeOnLoad]
    internal static class GameScenes
    {
        private static readonly string[] Scenes = { "Assets/TeamHq/TeamHq.unity", "Assets/Raceday/Raceday.unity" };

        static GameScenes()
        {
            EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;
            if (Scenes.All(path => current.Any(s => s.path == path && s.enabled))) return;
            EditorBuildSettings.scenes = Scenes.Select(path => new EditorBuildSettingsScene(path, true))
                .Concat(current.Where(s => !Scenes.Contains(s.path)))
                .ToArray();
        }
    }
}
