using System.Linq;
using TarmacGauntlet.Career;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// An office screen: fills the docked panel, and the side pane if it needs one, when its screen opens, and again
    /// whenever the game changes while it is open.
    /// </summary>
    public abstract class OfficePanel : MonoBehaviour
    {
        protected Menus Menus { get; private set; }

        /// <summary>The docked panel's body while this screen is open, otherwise null.</summary>
        protected VisualElement Body { get; private set; }

        protected abstract string ScreenSlug { get; }

        protected static PlayerTeam Player => GameSession.State?.Teams.OfType<PlayerTeam>().FirstOrDefault();

        private void Awake()
        {
            Menus = GetComponent<Menus>();
            Menus.ScreenOpened += Opened;
            GameSession.Changed += OnChanged;
        }

        private void OnDestroy()
        {
            Menus.ScreenOpened -= Opened;
            GameSession.Changed -= OnChanged;
        }

        private void Opened(string slug, VisualElement panelBody)
        {
            Body = slug == ScreenSlug ? panelBody : null;
            if (Body == null) return;
            OnOpened();
            Render();
        }

        private void OnChanged(GameState _)
        {
            if (Body != null && Body.panel != null) Render();
        }

        /// <summary>Called each time the screen opens, before it renders.</summary>
        protected virtual void OnOpened() { }

        /// <summary>Rebuilds the panel and the side pane from the game.</summary>
        protected abstract void Render();
    }
}
