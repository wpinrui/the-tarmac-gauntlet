// Stand-ins for UI Toolkit: an element tree with classes, inline styles, text and callbacks, and no layout.
// Html.cs writes the tree out, and the browser lays it out with the converted Menus.uss.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
}

namespace UnityEngine.UIElements
{
    public enum PickingMode { Position, Ignore }
    public enum DisplayStyle { Flex, None }
    public enum Visibility { Visible, Hidden }
    public enum StyleKeyword { Undefined, Null, Auto, None, Initial }
    public enum ScrollViewMode { Vertical, Horizontal, VerticalAndHorizontal }
    public enum ScrollerVisibility { Auto, AlwaysVisible, Hidden }
    public enum CollectionVirtualizationMethod { FixedHeight, DynamicHeight }
    public enum SelectionType { None, Single, Multiple }
    public enum TrickleDown { NoTrickleDown, TrickleDown }
    public enum AngleUnit { Degree }
    public enum LengthUnit { Pixel, Percent }
    public enum PanelScaleMode { ConstantPixelSize, ScaleWithScreenSize }
    public enum PanelScreenMatchMode { MatchWidthOrHeight }

    public struct Length
    {
        public float value;
        public LengthUnit unit;
        public Length(float v, LengthUnit u = LengthUnit.Pixel) { value = v; unit = u; }
        public static Length Percent(float v) => new Length(v, LengthUnit.Percent);
        public static implicit operator Length(float v) => new Length(v);
        public string Css => unit == LengthUnit.Percent ? Fmt(value) + "%" : Fmt(value) + "px";
        internal static string Fmt(float v) => Math.Round(v, 3).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public struct Angle
    {
        public float value;
        public Angle(float v, AngleUnit u = AngleUnit.Degree) { value = v; }
    }

    public struct Translate
    {
        public Length x, y;
        public Translate(Length x, Length y) { this.x = x; this.y = y; }
    }

    public struct Rotate
    {
        public Angle angle;
        public Rotate(Angle a) { angle = a; }
    }

    public struct Scale
    {
        public Vector3 value;
        public Scale(Vector3 v) { value = v; }
    }

    public struct TransformOrigin
    {
        public Length x, y;
        public TransformOrigin(Length x, Length y) { this.x = x; this.y = y; }
    }

    public struct FontDefinition
    {
        public Font font;
        public static FontDefinition FromFont(Font f) => new FontDefinition { font = f };
    }

    public struct Background
    {
        public Texture texture;
        public static Background FromRenderTexture(RenderTexture t) => new Background { texture = t };
        public static Background FromTexture2D(Texture2D t) => new Background { texture = t };
    }

    public struct StyleBackground
    {
        public object value;
        public StyleBackground(StyleKeyword k) { value = k; }
        public StyleBackground(Texture2D t) { value = t; }
        public static implicit operator StyleBackground(Texture2D t) => new StyleBackground(t);
    }

    /// <summary>One inline style value, whatever its kind. Every style property takes one.</summary>
    public struct Sv : IEquatable<Sv>
    {
        public object value;
        public Sv(object v) { value = v; }
        public static implicit operator Sv(float v) => new Sv(new Length(v));
        public static implicit operator Sv(int v) => new Sv(new Length(v));
        public static implicit operator Sv(double v) => new Sv(new Length((float)v));
        public static implicit operator Sv(Length v) => new Sv(v);
        public static implicit operator Sv(Color v) => new Sv(v);
        public static implicit operator Sv(Color32 v) => new Sv((Color)v);
        public static implicit operator Sv(StyleKeyword v) => new Sv(v);
        public static implicit operator Sv(Texture2D v) => new Sv(v);
        public static implicit operator Sv(Background v) => new Sv(v.texture);
        public static implicit operator Sv(StyleBackground v) => new Sv(v.value);
        public static implicit operator Sv(DisplayStyle v) => new Sv(v);
        public static implicit operator Sv(Visibility v) => new Sv(v);
        public static implicit operator Sv(TextAnchor v) => new Sv(v);
        public static implicit operator Sv(FontDefinition v) => new Sv(v);
        public static implicit operator Sv(Translate v) => new Sv(v);
        public static implicit operator Sv(Rotate v) => new Sv(v);
        public static implicit operator Sv(Scale v) => new Sv(v);
        public static implicit operator Sv(TransformOrigin v) => new Sv(v);
        public static bool operator ==(Sv a, Sv b) => a.Equals(b);
        public static bool operator !=(Sv a, Sv b) => !a.Equals(b);
        public bool Equals(Sv o) => Equals(value, o.value);
        public override bool Equals(object o) => o is Sv s && Equals(s);
        public override int GetHashCode() => value?.GetHashCode() ?? 0;
    }

    /// <summary>Inline styles by property name, in the order they were set.</summary>
    public sealed class Style
    {
        internal readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        private Sv Get(string n) => Values.TryGetValue(n, out object v) ? new Sv(v) : default;
        private void Set(string n, Sv v) { if (v.value == null || v.value is StyleKeyword k && k == StyleKeyword.Null) Values.Remove(n); else Values[n] = v.value; }

        public Sv backgroundColor { get => Get("background-color"); set => Set("background-color", value); }
        public Sv backgroundImage { get => Get("background-image"); set => Set("background-image", value); }
        public Sv borderTopColor { get => Get("border-top-color"); set => Set("border-top-color", value); }
        public Sv borderBottomColor { get => Get("border-bottom-color"); set => Set("border-bottom-color", value); }
        public Sv borderLeftColor { get => Get("border-left-color"); set => Set("border-left-color", value); }
        public Sv borderRightColor { get => Get("border-right-color"); set => Set("border-right-color", value); }
        public Sv color { get => Get("color"); set => Set("color", value); }
        public Sv display { get => Get("display"); set => Set("display", value); }
        public Sv visibility { get => Get("visibility"); set => Set("visibility", value); }
        public Sv left { get => Get("left"); set => Set("left", value); }
        public Sv top { get => Get("top"); set => Set("top", value); }
        public Sv right { get => Get("right"); set => Set("right", value); }
        public Sv bottom { get => Get("bottom"); set => Set("bottom", value); }
        public Sv width { get => Get("width"); set => Set("width", value); }
        public Sv height { get => Get("height"); set => Set("height", value); }
        public Sv minWidth { get => Get("min-width"); set => Set("min-width", value); }
        public Sv maxHeight { get => Get("max-height"); set => Set("max-height", value); }
        public Sv flexBasis { get => Get("flex-basis"); set => Set("flex-basis", value); }
        public Sv flexGrow { get => Get("flex-grow"); set => Set("flex-grow", value); }
        public Sv opacity { get => Get("opacity"); set => Set("opacity", value); }
        public Sv rotate { get => Get("rotate"); set => Set("rotate", value); }
        public Sv scale { get => Get("scale"); set => Set("scale", value); }
        public Sv translate { get => Get("translate"); set => Set("translate", value); }
        public Sv transformOrigin { get => Get("transform-origin"); set => Set("transform-origin", value); }
        public Sv unityBackgroundImageTintColor { get => Get("--tint"); set => Set("--tint", value); }
        public Sv unityFontDefinition { get => Get("font"); set => Set("font", value); }
        public Sv unityTextAlign { get => Get("text-align"); set => Set("text-align", value); }
        public Sv marginLeft { get => Get("margin-left"); set => Set("margin-left", value); }
        public Sv marginTop { get => Get("margin-top"); set => Set("margin-top", value); }
    }

    public sealed class ResolvedStyle
    {
        public DisplayStyle display => DisplayStyle.Flex;
        public float borderLeftWidth => 0;
        public float borderTopWidth => 0;
    }

    public delegate void EventCallback<in T>(T evt);

    public abstract class EventBase
    {
        public object target { get; set; }
        public void StopPropagation() { }
        public void PreventDefault() { }
    }

    public class PointerEventBase : EventBase
    {
        public int button { get; set; }
        public int pointerId { get; set; }
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
    }

    public sealed class ClickEvent : PointerEventBase { }
    public sealed class PointerDownEvent : PointerEventBase { }
    public sealed class PointerUpEvent : PointerEventBase { }
    public sealed class PointerMoveEvent : PointerEventBase { }
    public sealed class PointerEnterEvent : PointerEventBase { }
    public sealed class PointerLeaveEvent : PointerEventBase { }
    public sealed class PointerCaptureOutEvent : EventBase { }
    public sealed class GeometryChangedEvent : EventBase
    {
        public Rect oldRect { get; set; }
        public Rect newRect { get; set; }
    }
    public sealed class DetachFromPanelEvent : EventBase { }
    public sealed class FocusInEvent : EventBase { }
    public sealed class FocusOutEvent : EventBase { }
    public sealed class KeyDownEvent : EventBase
    {
        public KeyCode keyCode { get; set; }
    }

    public sealed class ChangeEvent<T> : EventBase
    {
        public T previousValue { get; set; }
        public T newValue { get; set; }
    }

    public interface IVisualElementScheduledItem
    {
        IVisualElementScheduledItem StartingIn(long ms);
        IVisualElementScheduledItem Every(long ms);
        void Pause();
        void Resume();
    }

    /// <summary>Scheduled work waits until the harness calls <see cref="Flush"/>; a delayed item never runs.</summary>
    public sealed class Scheduler
    {
        internal static readonly List<Item> Queue = new List<Item>();

        internal sealed class Item : IVisualElementScheduledItem
        {
            public Action Run;
            public bool Delayed, Paused;
            public IVisualElementScheduledItem StartingIn(long ms) { Delayed = ms > 0; return this; }
            public IVisualElementScheduledItem Every(long ms) { Delayed = true; return this; }
            public void Pause() => Paused = true;
            public void Resume() => Paused = false;
        }

        public IVisualElementScheduledItem Execute(Action a)
        {
            var item = new Item { Run = a };
            Queue.Add(item);
            return item;
        }

        public static void Flush()
        {
            for (int round = 0; round < 10 && Queue.Count > 0; round++)
            {
                Item[] items = Queue.ToArray();
                Queue.Clear();
                foreach (Item i in items)
                    if (!i.Delayed && !i.Paused) i.Run();
            }
            Queue.Clear();
        }
    }

    public interface IPanel
    {
        FocusController focusController { get; }
    }

    public sealed class FocusController
    {
        public VisualElement focusedElement => null;
    }

    internal sealed class Panel : IPanel
    {
        public FocusController focusController { get; } = new FocusController();
    }

    public class VisualElement
    {
        internal readonly List<VisualElement> kids = new List<VisualElement>();
        internal readonly List<string> classes = new List<string>();
        internal readonly Dictionary<Type, List<(Delegate Callback, TrickleDown Phase)>> callbacks = new Dictionary<Type, List<(Delegate, TrickleDown)>>();
        internal IPanel panelRoot;

        /// <summary>The tag Html.cs writes for this element.</summary>
        internal virtual string Tag => "ve";

        public string name { get; set; } = "";
        public object userData { get; set; }
        public PickingMode pickingMode { get; set; }
        public string tooltip { get; set; }
        public Style style { get; } = new Style();
        public ResolvedStyle resolvedStyle { get; } = new ResolvedStyle();
        public VisualElement parent { get; internal set; }
        public virtual VisualElement contentContainer => this;
        public VisualElement hierarchy => this;
        public Scheduler schedule { get; } = new Scheduler();
        /// <summary>
        /// Where a check puts an element, in reference px, since there is no layout here: measured in the browser and
        /// fed back. Unset, every element is at 0, 0 with no size.
        /// </summary>
        public static Func<VisualElement, Rect> Measure;
        public Rect layout => Measure?.Invoke(this) ?? new Rect(0, 0, 0, 0);
        public Rect worldBound => Measure?.Invoke(this) ?? new Rect(0, 0, 0, 0);
        public Rect contentRect => new Rect(0, 0, 0, 0);
        public List<StyleSheet> styleSheets { get; } = new List<StyleSheet>();
        public bool enabledSelf { get; private set; } = true;
        public Action<MeshGenerationContext> generateVisualContent;

        public IPanel panel
        {
            get
            {
                VisualElement e = this;
                while (e.parent != null) e = e.parent;
                return e.panelRoot;
            }
        }

        public void Add(VisualElement child)
        {
            VisualElement into = contentContainer;
            child.parent?.Remove(child);
            child.parent = into;
            into.kids.Add(child);
        }

        public void Insert(int index, VisualElement child)
        {
            VisualElement into = contentContainer;
            child.parent?.Remove(child);
            child.parent = into;
            into.kids.Insert(Math.Min(index, into.kids.Count), child);
        }

        public void Remove(VisualElement child)
        {
            if (kids.Remove(child)) child.parent = null;
            else if (contentContainer != this) contentContainer.Remove(child);
        }

        public void RemoveFromHierarchy() => parent?.Remove(this);

        public void Clear()
        {
            foreach (VisualElement k in contentContainer.kids) k.parent = null;
            contentContainer.kids.Clear();
        }

        public int childCount => contentContainer.kids.Count;
        public VisualElement this[int i] => contentContainer.kids[i];
        public int IndexOf(VisualElement e) => contentContainer.kids.IndexOf(e);
        public IEnumerable<VisualElement> Children() => contentContainer.kids.ToArray();
        public bool Contains(VisualElement e)
        {
            for (VisualElement p = e; p != null; p = p.parent)
                if (p == this) return true;
            return false;
        }

        public void AddToClassList(string c)
        {
            if (string.IsNullOrEmpty(c)) return;
            foreach (string one in c.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (!classes.Contains(one)) classes.Add(one);
        }

        public void RemoveFromClassList(string c) => classes.Remove(c);
        public bool ClassListContains(string c) => classes.Contains(c);
        public void EnableInClassList(string c, bool on) { if (on) AddToClassList(c); else RemoveFromClassList(c); }
        public void SetEnabled(bool on) => enabledSelf = on;

        public void RegisterCallback<T>(EventCallback<T> callback, TrickleDown phase = TrickleDown.NoTrickleDown) where T : EventBase
        {
            if (!callbacks.TryGetValue(typeof(T), out var list)) callbacks[typeof(T)] = list = new List<(Delegate, TrickleDown)>();
            list.Add((callback, phase));
        }

        public void UnregisterCallback<T>(EventCallback<T> callback, TrickleDown phase = TrickleDown.NoTrickleDown) where T : EventBase
        {
            if (callbacks.TryGetValue(typeof(T), out var list)) list.RemoveAll(c => c.Callback.Equals(callback));
        }

        /// <summary>Runs this element's callbacks for an event, for the harness to click things.</summary>
        public void Send<T>(T e) where T : EventBase
        {
            e.target = this;
            if (callbacks.TryGetValue(typeof(T), out var list))
                foreach ((Delegate cb, _) in list.ToArray()) ((EventCallback<T>)cb)(e);
        }

        public void CapturePointer(int id) { }
        public void ReleasePointer(int id) { }
        public bool HasPointerCapture(int id) => false;
        public void Focus() { }
        public void Blur() { }
        public void MarkDirtyRepaint() { }
        public void BringToFront() { }

        public Vector2 WorldToLocal(Vector2 p) => p;
        public Vector2 LocalToWorld(Vector2 p) => p;
        public Rect ChangeCoordinatesTo(VisualElement dest, Rect r) => r;

        public IEnumerable<VisualElement> Descendants()
        {
            foreach (VisualElement k in kids)
            {
                yield return k;
                foreach (VisualElement d in k.Descendants()) yield return d;
            }
        }

        public T Q<T>(string name = null, string className = null) where T : VisualElement =>
            Descendants().OfType<T>().FirstOrDefault(e => (name == null || e.name == name) && (className == null || e.classes.Contains(className)));

        public VisualElement Q(string name = null, string className = null) => Q<VisualElement>(name, className);

        public UQueryBuilder<T> Query<T>(string name = null, string className = null) where T : VisualElement =>
            new UQueryBuilder<T>(Descendants().OfType<T>().Where(e => (name == null || e.name == name) && (className == null || e.classes.Contains(className))).ToList());

        public T GetFirstAncestorOfType<T>() where T : VisualElement
        {
            for (VisualElement p = parent; p != null; p = p.parent)
                if (p is T t) return t;
            return null;
        }
    }

    public sealed class UQueryBuilder<T> where T : VisualElement
    {
        private readonly List<T> items;
        internal UQueryBuilder(List<T> items) { this.items = items; }
        public void ForEach(Action<T> a) { foreach (T t in items) a(t); }
        public List<T> ToList() => items;
    }

    public sealed class MeshGenerationContext { }

    public class TextElement : VisualElement
    {
        public virtual string text { get; set; } = "";
        public bool enableRichText { get; set; } = true;
    }

    public class Label : TextElement
    {
        internal override string Tag => "lbl";
        public Label() { }
        public Label(string text) { this.text = text ?? ""; }
    }

    public sealed class Scroller : VisualElement
    {
        public event Action<float> valueChanged { add { } remove { } }
    }

    public class ScrollView : VisualElement
    {
        internal override string Tag => "sv";
        private readonly VisualElement viewport = new VisualElement(), content = new VisualElement();

        public ScrollView() : this(ScrollViewMode.Vertical) { }

        public ScrollView(ScrollViewMode mode)
        {
            AddToClassList("unity-scroll-view");
            viewport.AddToClassList("unity-scroll-view__content-viewport");
            content.AddToClassList("unity-scroll-view__content-container");
            kids.Add(viewport);
            SetParent(viewport, this);
            viewport.kids.Add(content);
            SetParent(content, viewport);
        }

        private static void SetParent(VisualElement child, VisualElement parent) =>
            child.parent = parent;

        public override VisualElement contentContainer => content;
        public VisualElement contentViewport => viewport;
        public Vector2 scrollOffset { get; set; }
        public ScrollerVisibility horizontalScrollerVisibility { get; set; }
        public ScrollerVisibility verticalScrollerVisibility { get; set; }
        public Scroller verticalScroller { get; } = new Scroller();
        public void ScrollTo(VisualElement e) { }
    }

    public class ListView : VisualElement
    {
        /// <summary>Rows the preview writes out; the rest are left off as the real list would be scrolled away.</summary>
        public static int PreviewRows = 30;

        private readonly ScrollView scroll = new ScrollView();

        public ListView()
        {
            AddToClassList("unity-list-view");
            kids.Add(scroll);
            scroll.parent = this;
        }

        public float fixedItemHeight { get; set; }
        public CollectionVirtualizationMethod virtualizationMethod { get; set; }
        public SelectionType selectionType { get; set; }
        public IList itemsSource { get; set; }
        public Func<VisualElement> makeItem { get; set; }
        public Action<VisualElement, int> bindItem { get; set; }

        public void RefreshItems()
        {
            scroll.Clear();
            if (itemsSource == null || makeItem == null) return;
            for (int i = 0; i < itemsSource.Count && i < PreviewRows; i++)
            {
                VisualElement row = makeItem();
                row.style.height = fixedItemHeight;
                row.style.minWidth = 0;
                bindItem?.Invoke(row, i);
                scroll.Add(row);
            }
        }

        public void Rebuild() => RefreshItems();
        public void ScrollToItem(int i) { }
    }

    public sealed class TextEdition
    {
        public string placeholder { get; set; } = "";
    }

    public class TextField : VisualElement
    {
        internal override string Tag => "tf";
        private string current = "";
        private readonly List<EventCallback<ChangeEvent<string>>> changed = new List<EventCallback<ChangeEvent<string>>>();

        public TextField()
        {
            AddToClassList("unity-base-text-field");
        }

        public int maxLength { get; set; }
        public TextEdition textEdition { get; } = new TextEdition();

        public string value
        {
            get => current;
            set
            {
                string old = current;
                current = value ?? "";
                if (old == current) return;
                foreach (var cb in changed.ToArray()) cb(new ChangeEvent<string> { previousValue = old, newValue = current, target = this });
            }
        }

        public void SetValueWithoutNotify(string v) => current = v ?? "";
        public void SelectAll() { }
        internal void OnChanged(EventCallback<ChangeEvent<string>> cb) => changed.Add(cb);
    }

    public static class NotifyValueChangedExtensions
    {
        public static bool RegisterValueChangedCallback(this TextField field, EventCallback<ChangeEvent<string>> cb)
        {
            field.OnChanged(cb);
            return true;
        }
    }

    public static class RuntimePanelUtils
    {
        public static Vector2 ScreenToPanel(IPanel p, Vector2 v) => v;
    }

    public class StyleSheet : ScriptableObject { }
    public class ThemeStyleSheet : StyleSheet { }

    public class PanelSettings : ScriptableObject
    {
        public PanelScaleMode scaleMode { get; set; }
        public Vector2Int referenceResolution { get; set; }
        public PanelScreenMatchMode screenMatchMode { get; set; }
        public float match { get; set; }
        public int sortingOrder { get; set; }
        public ThemeStyleSheet themeStyleSheet { get; set; }
    }

    public sealed class UIDocument : MonoBehaviour
    {
        /// <summary>Every document made, in order, for the harness to write out.</summary>
        public static readonly List<UIDocument> All = new List<UIDocument>();

        public UIDocument()
        {
            rootVisualElement = new VisualElement { panelRoot = new Panel() };
            All.Add(this);
        }

        public PanelSettings panelSettings { get; set; }
        public VisualElement rootVisualElement { get; }
    }
}
