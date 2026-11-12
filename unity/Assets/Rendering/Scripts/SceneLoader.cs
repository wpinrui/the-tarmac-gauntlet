using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace TarmacGauntlet.Rendering
{
    /// <summary>
    /// Every scene change in the game goes through <see cref="Load"/>, behind a loading screen: the game's wordmark
    /// centred on #0c0e0e with a thin red bar sliding under it. The screen is drawn before the old scene goes, the new one
    /// loads in the background, and a scene that builds itself over several frames holds the screen up until it is done
    /// (<see cref="Hold"/>). The screen fades once the new scene has drawn its first frame underneath it.
    /// </summary>
    public sealed class SceneLoader : MonoBehaviour
    {
        private const float FadeSeconds = 0.3f;
        /// <summary>One pass of the bar's segment across its track, s.</summary>
        private const float SweepSeconds = 1.1f;
        /// <summary>A frame moves the bar on by at most this long, s, so a slow frame doesn't make it jump.</summary>
        private const float MaxStep = 1 / 30f;
        private const float BarWidth = 220f, BarHeight = 3f, SegmentShare = 0.32f, BarGap = 44f;

        // The main menu's wordmark, parked: in its pixels on its 358 x 209.32 canvas (Menus.BuildWordmark).
        private const float MarkWidth = 358f, MarkHeight = 209.32f;
        private const float CarLeft = 195f, CarTop = 19.7f, CarWidth = 169.61f, CarHeight = 43f;
        private const float RimSize = 14.14f, RimY = 50.07f;
        private static readonly float[] RimX = { 233.05f, 333.38f };

        private static readonly Color Background = new Color32(0x0c, 0x0e, 0x0e, 0xff);
        private static readonly Color Red = new Color32(0xe1, 0x06, 0x00, 0xff);
        private static readonly Color Track = new Color(1f, 1f, 1f, 0.08f);

        private static SceneLoader current;
        private static int holds;

        private VisualElement root, segment;
        private float sweep;

        /// <summary>True while a load is under way, from the call to the end of the fade.</summary>
        public static bool Loading => current != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            current = null;
            holds = 0;
        }

        /// <summary>Shows the loading screen and changes to a scene. A second call while one is loading does nothing.</summary>
        public static void Load(string scene)
        {
            if (current != null) return;
            var go = new GameObject("Loading screen");
            DontDestroyOnLoad(go);
            current = go.AddComponent<SceneLoader>();
            current.Build();
            current.StartCoroutine(current.Run(scene));
        }

        /// <summary>A scene still building itself keeps the loading screen up; call <see cref="Release"/> when done.</summary>
        public static void Hold() => holds++;

        public static void Release() => holds = Mathf.Max(0, holds - 1);

        private IEnumerator Run(string scene)
        {
            // Draw the screen over the old scene before anything heavy starts.
            yield return null;
            yield return new WaitForEndOfFrame();
            holds = 0;
            AsyncOperation load = SceneManager.LoadSceneAsync(scene);
            while (!load.isDone) yield return null;
            while (holds > 0) yield return null;
            // The new scene's first frame is drawn underneath before the screen goes.
            yield return null;
            yield return new WaitForEndOfFrame();
            for (float t = 0f; t < FadeSeconds; t += Mathf.Min(Time.unscaledDeltaTime, MaxStep))
            {
                root.style.opacity = 1f - t / FadeSeconds;
                yield return null;
            }
            current = null;
            Destroy(gameObject);
        }

        private void Update()
        {
            sweep = (sweep + Mathf.Min(Time.unscaledDeltaTime, MaxStep) / SweepSeconds) % 1f;
            float eased = sweep * sweep * (3f - 2f * sweep);
            float travel = BarWidth * (1f + SegmentShare);
            segment.style.left = -BarWidth * SegmentShare + travel * eased;
        }

        private void OnDestroy()
        {
            if (current == this) current = null;
        }

        private void Build()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1600, 900);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.sortingOrder = 1000;
            settings.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            var doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = settings;

            root = doc.rootVisualElement;
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            root.style.backgroundColor = Background;
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.pickingMode = PickingMode.Position;   // nothing underneath takes a click while it loads

            var mark = Box(MarkWidth, MarkHeight);
            mark.style.overflow = Overflow.Hidden;
            mark.style.flexShrink = 0;
            mark.Add(Picture("wordmark-text", 0, 0, MarkWidth, MarkHeight));
            var car = Picture("car-body", CarLeft, CarTop, CarWidth, CarHeight);
            foreach (float x in RimX)
                car.Add(Picture("car-rim", x - CarLeft - RimSize / 2, RimY - CarTop - RimSize / 2, RimSize, RimSize));
            mark.Add(car);
            root.Add(mark);

            var bar = Box(BarWidth, BarHeight);
            bar.style.marginTop = BarGap;
            bar.style.overflow = Overflow.Hidden;
            bar.style.flexShrink = 0;
            bar.style.backgroundColor = Track;
            segment = Box(BarWidth * SegmentShare, BarHeight);
            segment.style.position = Position.Absolute;
            segment.style.top = 0;
            segment.style.backgroundColor = Red;
            bar.Add(segment);
            root.Add(bar);
            Update();
        }

        private static VisualElement Box(float width, float height)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.width = width;
            e.style.height = height;
            return e;
        }

        private static VisualElement Picture(string file, float left, float top, float width, float height)
        {
            VisualElement e = Box(width, height);
            e.style.position = Position.Absolute;
            e.style.left = left;
            e.style.top = top;
            e.style.backgroundImage = Resources.Load<Texture2D>("Wordmark/" + file);
            e.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            return e;
        }
    }
}
