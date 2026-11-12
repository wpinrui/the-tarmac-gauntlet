using System;
using System.Linq;
using TarmacGauntlet.Career;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using static TarmacGauntlet.Management.MenuKit;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The race's pause menu, on Escape until the race is settled: the race stops dead (<see cref="RaceWeekend.Paused"/>),
    /// its sound and graphics with it, under the same glass menu as the hub's Escape menu. Save writes the race as it
    /// stands into the career (<see cref="GameSession.SaveRace"/>), and continuing the career carries on from there.
    /// Retire takes the player's car out and settles the race from where every car is (<see cref="RaceWeekend.Withdraw"/>),
    /// on to the race summary. Escape steps back out of settings, then out of the menu.
    /// </summary>
    public sealed class RacePauseMenu : MonoBehaviour
    {
        private const string RacedayScene = "Raceday";
        /// <summary>How long Save stays lit after a save, s.</summary>
        private const long SavedMs = 600;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Hook()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneLoaded += OnLoaded;
        }

        private static void OnLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == RacedayScene && FindAnyObjectByType<RacePauseMenu>() == null)
                new GameObject("Pause menu").AddComponent<RacePauseMenu>();
        }

        private enum Overlay { None, Pause, Settings }

        private Overlay overlay = Overlay.None;
        private MenuGlass glass;
        private VisualElement root, dimmer, pauseLayer, settingsLayer, settingsPanel, save, retire;

        private void Start()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1600, 900);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.sortingOrder = 20;
            settings.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            var doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = settings;

            root = doc.rootVisualElement;
            root.styleSheets.Add(Resources.Load<StyleSheet>("Menus"));
            root.AddToClassList("root");
            root.pickingMode = PickingMode.Ignore;
            root.style.unityFontDefinition = FontDefinition.FromFont(SemiBold);
            glass = new MenuGlass(root);

            dimmer = Layer("dimmer");
            pauseLayer = Layer("centre");
            VisualElement menu = glass.Make("menu");
            VisualElement items = MenuGroup(0);
            items.Add(MenuItem("Resume", () => SetOverlay(Overlay.None)));
            items.Add(save = MenuItem("Save", Save));
            items.Add(MenuItem("Settings", () =>
            {
                SettingsView.Fill(settingsPanel);
                SetOverlay(Overlay.Settings);
            }));
            items.Add(retire = MenuItem("Retire", Retire));
            items.Add(MenuItem("Quit to main menu", QuitToMainMenu));
            items.Add(MenuItem("Quit game", Menus.Quit));
            menu.Add(items);
            pauseLayer.Add(menu);

            settingsLayer = Layer("centre");
            settingsPanel = glass.Make("settings");
            settingsLayer.Add(settingsPanel);
            Render();
        }

        private VisualElement Layer(string cls)
        {
            var layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.AddToClassList("layer");
            layer.AddToClassList(cls);
            root.Add(layer);
            return layer;
        }

        private void LateUpdate()
        {
            if (overlay != Overlay.None) glass?.Refresh();
        }

        private void OnDestroy()
        {
            if (overlay != Overlay.None) Unfreeze();
        }

        // ------------------------------------------------------------------ pausing

        /// <summary>True while the race can be paused: it is on, and not yet settled.</summary>
        private static bool CanPause => RaceWeekend.Snapshot != null && RaceWeekend.Summary == null;

        private void SetOverlay(Overlay next)
        {
            if (next != Overlay.None && overlay == Overlay.None) Freeze();
            else if (next == Overlay.None && overlay != Overlay.None)
            {
                Unfreeze();
                Camera cam = Camera.main;
                GlassBackdrop backdrop = cam != null ? cam.GetComponent<GlassBackdrop>() : null;
                if (backdrop != null) Destroy(backdrop);   // the blur costs a few passes a frame, so only while paused
            }
            overlay = next;
            Render();
        }

        private static void Freeze()
        {
            RaceWeekend.Paused = true;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            Camera cam = Camera.main;
            if (cam != null && cam.GetComponent<GlassBackdrop>() == null) cam.gameObject.AddComponent<GlassBackdrop>();
        }

        /// <summary>The race runs on: it puts back its own speed the next frame.</summary>
        private static void Unfreeze()
        {
            RaceWeekend.Paused = false;
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        private void Render()
        {
            if (root == null) return;
            dimmer.style.display = overlay != Overlay.None ? DisplayStyle.Flex : DisplayStyle.None;
            pauseLayer.style.display = overlay == Overlay.Pause ? DisplayStyle.Flex : DisplayStyle.None;
            settingsLayer.style.display = overlay == Overlay.Settings ? DisplayStyle.Flex : DisplayStyle.None;
            // Saving needs a career to save into, and retiring a car of the player's in the race.
            save.style.display = GameSession.CareerId != null ? DisplayStyle.Flex : DisplayStyle.None;
            bool racing = RaceWeekend.Setup?.Grid.Any(c => c.Player) ?? false;
            retire.style.display = racing ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ------------------------------------------------------------------ the items

        private void Save()
        {
            Func<RaceSave> snapshot = RaceWeekend.Snapshot;
            if (snapshot == null || GameSession.CareerId == null) return;
            try
            {
                GameSession.SaveRace(snapshot());
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return;
            }
            save.AddToClassList("on");
            save.schedule.Execute(() => save.RemoveFromClassList("on")).StartingIn(SavedMs);
        }

        private void Retire()
        {
            overlay = Overlay.None;
            Unfreeze();   // the race summary runs at normal speed, over the blurred scene
            Render();
            RaceWeekend.Withdraw();
        }

        private void QuitToMainMenu()
        {
            overlay = Overlay.None;
            Unfreeze();
            GameSession.Close();
            SceneManager.LoadScene(RaceSummaryScreen.HubScene);
        }

        // Keys come through IMGUI events, so this works with either of Unity's input systems.
        private void OnGUI()
        {
            Event ev = Event.current;
            if (ev.type != EventType.KeyDown || root == null) return;
            if (MenuDropdown.Key(ev.keyCode))
            {
                ev.Use();
                return;
            }
            if (ev.keyCode != KeyCode.Escape) return;
            if (overlay == Overlay.Settings) SetOverlay(Overlay.Pause);
            else if (overlay == Overlay.Pause) SetOverlay(Overlay.None);
            else if (CanPause) SetOverlay(Overlay.Pause);
            else return;
            ev.Use();
        }
    }
}
