// Stand-ins for the audio the menus' UI sounds use. Nothing plays; the preview only draws.
namespace UnityEngine
{
    public class AudioClip : Object { }

    public class AudioSource : Behaviour
    {
        public bool playOnAwake { get; set; }
        public bool ignoreListenerPause { get; set; }
        public bool bypassListenerEffects { get; set; }
        public float volume { get; set; } = 1;
        public float spatialBlend { get; set; }
        public float pitch { get; set; } = 1;
        public void PlayOneShot(AudioClip clip) { }
    }
}
