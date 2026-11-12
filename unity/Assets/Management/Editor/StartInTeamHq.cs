using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace TarmacGauntlet.Management.Editor
{
    /// <summary>
    /// Play always starts the game at launch in the Team HQ, whatever scene is open. The editor also opens the HQ
    /// when it starts on an unsaved empty scene. "Play race only" starts one run straight in the race instead.
    /// </summary>
    [InitializeOnLoad]
    internal static class StartInTeamHq
    {
        private const string HqPath = "Assets/TeamHq/TeamHq.unity";
        private const string RacedayPath = "Assets/Raceday/Raceday.unity";
        private const string RaceOnlyKey = "TarmacGauntlet.PlayRaceOnly";

        static StartInTeamHq()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            // The asset database can still be loading here, so the start scene is set a moment later.
            EditorApplication.delayCall += () =>
            {
                if (!SessionState.GetBool(RaceOnlyKey, false)) StartAt(HqPath);
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                Scene active = SceneManager.GetActiveScene();
                if (!string.IsNullOrEmpty(active.path) || active.isDirty) return;
                EditorSceneManager.OpenScene(HqPath);
            };
        }

        [MenuItem("Tarmac Gauntlet/Play race only")]
        private static void PlayRaceOnly()
        {
            if (EditorApplication.isPlaying) return;
            SessionState.SetBool(RaceOnlyKey, true);
            StartAt(RacedayPath);
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("Tarmac Gauntlet/Play race only", true)]
        private static bool CanPlayRaceOnly() => !EditorApplication.isPlaying;

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            SessionState.EraseBool(RaceOnlyKey);
            StartAt(HqPath);
        }

        private static void StartAt(string path) =>
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
    }
}
