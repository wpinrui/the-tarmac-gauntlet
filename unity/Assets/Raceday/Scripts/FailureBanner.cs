using TarmacGauntlet.Rendering;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// What broke, at the top of the screen, from the moment a car breaks down until it is back in its garage
    /// (<see cref="RaceDirector.BrokenDown"/>): a slash in the team's colour and the failure's name, as "Gearbox seizure".
    /// It shows for your own car whatever the cameras are on, and for the car the cameras are on. Another team's car has
    /// its team's name before the failure. Drawn as the tower's chequered flag banner is, in the same place, and laid out
    /// in the tower's design units.
    /// </summary>
    public sealed class FailureBanner : MonoBehaviour
    {
        public RaceDirector Director;
        public RaceCameras Cameras;
        public CarController[] Cars;
        public string[] Teams;
        public Color[] Colours;
        /// <summary>Your car's index, or -1 with none.</summary>
        public int Player = -1;

        /// <summary>Real seconds the banner takes to fade in.</summary>
        private const float FadeIn = 0.25f;
        /// <summary>Layout, in the tower's design units.</summary>
        private const float Margin = 1.6f, Height = 4.2f, Pad = 1.2f, Gap = 0.8f, Radius = 0.45f, Row = 4.6f;
        private const float BarWidth = 0.42f, BarHeight = 2.4f, BarSkew = 24f;

        private static readonly Color Back = new Color(18f / 255f, 18f / 255f, 20f / 255f, 0.82f);
        private static readonly Color TeamText = new Color(1f, 1f, 1f, 0.6f);

        private Font bold, semiBold;
        private GUIStyle nameStyle, teamStyle;
        private Texture2D slash;
        private int styledFor = -1;
        private float[] shownAt;

        private void OnGUI()
        {
            using (GammaGui.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            if (Event.current.type != EventType.Repaint || DebugMenu.Bare || Director == null || Cars == null) return;
            if (shownAt == null || shownAt.Length != Cars.Length)
            {
                shownAt = new float[Cars.Length];
                for (int i = 0; i < shownAt.Length; i++) shownAt[i] = -1f;
            }
            int watched = -1;
            if (Cameras != null)
                for (int i = 0; i < Cars.Length; i++)
                    if (Cars[i] == Cameras.Car) watched = i;
            for (int i = 0; i < Cars.Length; i++)
                if (Director.BrokenDown(i) == null) shownAt[i] = -1f;

            float u = Screen.height / 1080f * 19.2f * 0.7f;
            Style(u);
            float y = Margin * u;
            if (Player >= 0 && Draw(Player, false, y, u)) y += Row * u;
            if (watched >= 0 && watched != Player) Draw(watched, true, y, u);
        }

        /// <summary>Car i's banner at height y, if it has broken down. Returns true if it drew one.</summary>
        private bool Draw(int i, bool named, float y, float u)
        {
            IssueTemplate failure = Director.BrokenDown(i);
            if (failure == null) return false;
            if (shownAt[i] < 0f) shownAt[i] = Time.unscaledTime;
            float alpha = Mathf.Clamp01((Time.unscaledTime - shownAt[i]) / FadeIn);

            string teamName = named && Teams != null && i < Teams.Length ? Teams[i].ToUpperInvariant() : null;
            float barSpan = (BarWidth + BarHeight * Mathf.Tan(BarSkew * Mathf.Deg2Rad)) * u;
            float nameWidth = nameStyle.CalcSize(new GUIContent(failure.Description)).x;
            float teamWidth = teamName != null ? teamStyle.CalcSize(new GUIContent(teamName)).x + Gap * u : 0f;
            float h = Height * u, pad = Pad * u;
            float width = pad + barSpan + Gap * u + teamWidth + nameWidth + pad;
            var r = new Rect((Screen.width - width) / 2f, y, width, h);

            Color back = Back;
            back.a *= alpha;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, back, Vector4.zero, Vector4.one * Radius * u);
            Color colour = Colours != null && i < Colours.Length ? Colours[i] : Color.white;
            colour.a *= alpha;
            float x = r.x + pad;
            GUI.DrawTexture(new Rect(x, r.center.y - BarHeight * u / 2f, barSpan, BarHeight * u), slash, ScaleMode.StretchToFill, true, 0f, colour, 0f, 0f);
            x += barSpan + Gap * u;
            if (teamName != null)
            {
                teamStyle.normal.textColor = new Color(TeamText.r, TeamText.g, TeamText.b, TeamText.a * alpha);
                GUI.Label(new Rect(x, r.y, teamWidth, h), teamName, teamStyle);
                x += teamWidth;
            }
            nameStyle.normal.textColor = new Color(1f, 1f, 1f, alpha);
            GUI.Label(new Rect(x, r.y, nameWidth, h), failure.Description, nameStyle);
            return true;
        }

        private void Style(float u)
        {
            int key = Mathf.RoundToInt(u * 100f);
            if (key == styledFor && nameStyle != null) return;
            styledFor = key;
            if (bold == null)
            {
                bold = Resources.Load<Font>("Fonts/TitilliumWeb-Bold");
                semiBold = Resources.Load<Font>("Fonts/TitilliumWeb-SemiBold");
                float shift = BarHeight * Mathf.Tan(BarSkew * Mathf.Deg2Rad);
                slash = RaceTower.Shape(BarWidth + shift, BarHeight, (sx, sy) => sx >= sy / BarHeight * shift && sx <= sy / BarHeight * shift + BarWidth);
            }
            nameStyle = Make(bold, 2.4f * u);
            teamStyle = Make(semiBold, 1.6f * u);
        }

        private static GUIStyle Make(Font font, float size) => new GUIStyle(GUI.skin.label)
        {
            font = font,
            fontSize = Mathf.Max(1, Mathf.RoundToInt(size)),
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Overflow,
            padding = new RectOffset(0, 0, 0, 0),
            margin = new RectOffset(0, 0, 0, 0),
            wordWrap = false,
        };

        private void OnDestroy()
        {
            if (slash != null) Destroy(slash);
        }
    }
}
