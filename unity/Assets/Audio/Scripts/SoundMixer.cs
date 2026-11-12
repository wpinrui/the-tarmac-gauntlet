using UnityEngine;
using UnityEngine.SceneManagement;

namespace TarmacGauntlet.Audio
{
    /// <summary>
    /// Runs the <see cref="Mix"/> in the game: ticks its ducking and pause muffle each frame and muffles the race behind
    /// its pause menu with a low-pass on the listener. The music and UI sources bypass the listener's effects, so they
    /// stay clear over it. It opens Unity's voices to <see cref="Mix.RealVoices"/> as the game starts, before the settings
    /// apply the master volume and before anything plays, and lives across scenes.
    /// </summary>
    [DefaultExecutionOrder(1000)]   // after every source has reported its engine this frame
    public sealed class SoundMixer : MonoBehaviour
    {
        private AudioListener listener;
        private AudioLowPassFilter muffle;

        /// <summary>Before the settings load (<see cref="RuntimeInitializeLoadType.BeforeSceneLoad"/>), as resetting the
        /// audio puts the listener back to full volume.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Voices()
        {
            AudioConfiguration config = AudioSettings.GetConfiguration();
            if (config.numRealVoices >= Mix.RealVoices) return;
            config.numRealVoices = Mix.RealVoices;
            config.numVirtualVoices = Mathf.Max(config.numVirtualVoices, 4 * Mix.RealVoices);
            AudioSettings.Reset(config);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            Mix.Settle();
            var go = new GameObject("Sound mixer");
            DontDestroyOnLoad(go);
            go.AddComponent<SoundMixer>();
            SceneManager.sceneLoaded -= Loaded;
            SceneManager.sceneLoaded += Loaded;
        }

        private static void Loaded(Scene scene, LoadSceneMode mode) => Mix.Settle();

        private void LateUpdate()
        {
            Mix.Tick(Time.deltaTime, Time.unscaledDeltaTime);

            if (listener == null)
            {
                listener = FindFirstObjectByType<AudioListener>();
                muffle = null;
                if (listener == null) return;
            }
            if (muffle == null && !listener.TryGetComponent(out muffle))
            {
                muffle = listener.gameObject.AddComponent<AudioLowPassFilter>();
                muffle.lowpassResonanceQ = 1f;
            }
            // Off while open: the filter costs a little on every sample.
            muffle.enabled = Mix.Muffle > 0.001f;
            muffle.cutoffFrequency = Mix.Cutoff;
        }
    }
}
