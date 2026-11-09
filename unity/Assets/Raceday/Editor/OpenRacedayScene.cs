using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace TarmacGauntlet.Raceday.Editor
{
    /// <summary>Opens the raceday scene when the editor starts on an unsaved empty scene.</summary>
    [InitializeOnLoad]
    internal static class OpenRacedayScene
    {
        private const string ScenePath = "Assets/Raceday/Raceday.unity";

        static OpenRacedayScene()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                Scene active = SceneManager.GetActiveScene();
                if (!string.IsNullOrEmpty(active.path) || active.isDirty) return;
                EditorSceneManager.OpenScene(ScenePath);
            };
        }
    }
}
