using TarmacGauntlet.Career;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// A screen in the menus: fills the docked panel, and the side pane if it needs one, when its screen opens, and
    /// again whenever the game changes while it is open.
    /// </summary>
    public abstract class MenuPanel : MonoBehaviour
    {
        protected Menus Menus { get; private set; }

        /// <summary>The panel body while this screen is open, otherwise null.</summary>
        protected VisualElement Body { get; private set; }

        /// <summary>The slug this screen opened as.</summary>
        protected string OpenSlug { get; private set; }

        private protected Tooltips Tips => Menus.Tips;

        /// <summary>Whether this screen fills the panel for <paramref name="slug"/>.</summary>
        protected abstract bool Shows(string slug);

        /// <summary>False for a screen that doesn't redraw when the game changes.</summary>
        protected virtual bool FollowsGame => true;

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
            Body = Shows(slug) ? panelBody : null;
            if (Body == null) return;
            OpenSlug = slug;
            OnOpened();
            Render();
        }

        private void OnChanged(GameState _)
        {
            if (FollowsGame && Body != null && Body.panel != null) Render();
        }

        /// <summary>Called each time the screen opens, before it renders.</summary>
        protected virtual void OnOpened() { }

        /// <summary>Draws the panel and the side pane from the game.</summary>
        protected abstract void Render();
    }
}
