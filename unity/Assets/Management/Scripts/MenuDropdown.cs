using System;
using UnityEngine;
using UnityEngine.UIElements;
using static TarmacGauntlet.Management.MenuKit;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// A value with a down chevron that opens a list of every choice, for a setting with too many to step through.
    /// The list sits on a layer over the whole screen, so no panel or scroll area cuts it off. It opens below the value,
    /// or above it when there is more room there, is never taller than the room it has, and scrolls when it is long.
    /// A press outside it, Escape or a change of screen size closes it. Up, Down and Enter pick from it.
    /// </summary>
    internal static class MenuDropdown
    {
        /// <summary>Room kept between the list and the screen's edge, and between the list and its value, reference px.</summary>
        private const float Edge = 16, Gap = 4;
        /// <summary>One item's height and the list's padding and border round them, as in Menus.uss.</summary>
        private const float ItemHeight = 32, Frame = 2 * 6 + 2 * 1;

        private sealed class Open
        {
            public VisualElement Owner, Catcher;
            public ScrollView List;
            public int Highlight;
            public Action<int> Pick;
        }

        private static Open open;

        /// <summary>True while a list is open.</summary>
        internal static bool IsOpen => open != null && open.Catcher.panel != null;

        /// <param name="pick">Gets the index of the choice picked.</param>
        internal static VisualElement Make(string[] options, int index, string cls, Action<int> pick)
        {
            VisualElement box = Box("dropdown", cls);
            int current = Mathf.Clamp(index, 0, Mathf.Max(0, options.Length - 1));
            box.Add(Box("dropdown-side"));
            Label value = Text(Shown(options, current), "dropdown-value", true);
            box.Add(value);
            VisualElement side = Box("dropdown-side");
            side.Add(Icon("chevron-down", "dropdown-chevron"));
            box.Add(side);
            box.RegisterCallback<ClickEvent>(_ =>
            {
                if (options.Length == 0 || IsOpen) return;
                Show(box, options, current, i =>
                {
                    current = i;
                    value.text = Shown(options, i);
                    pick(i);
                });
            });
            box.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (open != null && open.Owner == box) Close();
            });
            return box;
        }

        private static string Shown(string[] options, int i) => options.Length > 0 ? options[i].ToUpperInvariant() : "";

        /// <summary>Closes the open list. False when none was open.</summary>
        internal static bool Close()
        {
            if (open == null) return false;
            bool was = open.Catcher.panel != null;
            if (was) MenuSounds.Play(UiCue.DropdownClose);
            open.Owner.RemoveFromClassList("open");
            open.Catcher.RemoveFromHierarchy();
            open = null;
            return was;
        }

        /// <summary>
        /// A key while a list is open: Escape closes it, Up and Down move along it and Enter picks. True when a list was
        /// open, so the key does nothing else; the menus ask this first.
        /// </summary>
        internal static bool Key(KeyCode key)
        {
            if (!IsOpen)
            {
                open = null;
                return false;
            }
            switch (key)
            {
                case KeyCode.Escape:
                    Close();
                    break;
                case KeyCode.UpArrow:
                    Highlight(open.Highlight - 1, true);
                    break;
                case KeyCode.DownArrow:
                    Highlight(open.Highlight + 1, true);
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Pick(open.Highlight);
                    break;
            }
            return true;
        }

        private static void Show(VisualElement box, string[] options, int current, Action<int> pick)
        {
            Close();
            VisualElement root = box;
            while (root.parent != null && !root.ClassListContains("root")) root = root.parent;

            var catcher = new VisualElement();
            catcher.AddToClassList("layer");
            catcher.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == catcher) Close();
            });
            // The first change is the layer's own first layout; any after it is the screen changing size.
            catcher.RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (e.oldRect.width > 0 && (e.oldRect.width != e.newRect.width || e.oldRect.height != e.newRect.height)) Close();
            });

            VisualElement popup = Box("popup", "dropdown-list");
            ScrollView list = Scroll("popup-list");
            for (int i = 0; i < options.Length; i++)
            {
                int at = i;
                Label item = Text(options[i].ToUpperInvariant(), "popup-item");
                item.RegisterCallback<ClickEvent>(e =>
                {
                    e.StopPropagation();
                    Pick(at);
                });
                item.RegisterCallback<PointerEnterEvent>(_ => Highlight(at, false));
                list.Add(item);
            }
            popup.Add(list);
            catcher.Add(popup);

            // Placed in the root's own space, which is the 1600 x 900 reference screen scaled to fit.
            Rect r = box.worldBound;
            Vector2 a = root.WorldToLocal(new Vector2(r.xMin, r.yMin)), b = root.WorldToLocal(new Vector2(r.xMax, r.yMax));
            float width = root.layout.width, height = root.layout.height;
            float wanted = options.Length * ItemHeight + Frame;
            float below = height - Edge - (b.y + Gap), above = a.y - Gap - Edge;
            bool down = wanted <= below || below >= above;
            float room = Mathf.Max(down ? below : above, ItemHeight + Frame);
            popup.style.maxHeight = Mathf.Floor(Mathf.Min(wanted, room));
            float w = Mathf.Min(b.x - a.x, width - 2 * Edge);
            popup.style.width = Mathf.Round(w);
            popup.style.left = Mathf.Round(Mathf.Clamp(a.x, Edge, width - Edge - w));
            if (down)
            {
                popup.style.top = Mathf.Round(b.y + Gap);
                popup.style.bottom = StyleKeyword.Auto;
            }
            else
            {
                popup.style.top = StyleKeyword.Auto;
                popup.style.bottom = Mathf.Round(height - a.y + Gap);
            }

            root.Add(catcher);
            box.AddToClassList("open");
            MenuSounds.Play(UiCue.DropdownOpen);
            open = new Open { Owner = box, Catcher = catcher, List = list, Highlight = current, Pick = pick };
            Highlight(current, false);
            // The picked choice in view once the list has its size.
            void InView(GeometryChangedEvent _)
            {
                list.UnregisterCallback<GeometryChangedEvent>(InView);
                if (open != null && open.List == list && current < list.contentContainer.childCount) list.ScrollTo(list.contentContainer[current]);
            }
            list.RegisterCallback<GeometryChangedEvent>(InView);
        }

        /// <param name="scroll">Scrolls the list to it; not for the mouse, so the list never moves under the pointer.</param>
        private static void Highlight(int index, bool scroll)
        {
            if (open == null) return;
            VisualElement items = open.List.contentContainer;
            if (items.childCount == 0) return;
            open.Highlight = Mathf.Clamp(index, 0, items.childCount - 1);
            for (int i = 0; i < items.childCount; i++) items[i].EnableInClassList("on", i == open.Highlight);
            if (scroll) open.List.ScrollTo(items[open.Highlight]);
        }

        private static void Pick(int index)
        {
            if (open == null) return;
            Action<int> pick = open.Pick;
            Close();
            pick(index);
        }
    }
}
