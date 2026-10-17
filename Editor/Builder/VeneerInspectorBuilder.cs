using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// The fluent layout API of the kit. A system describes an Inspector (or any bound page) in a few lines: header, tabs, cards and
    /// fields. Every method that makes content adds it to the <em>current container</em> (the root at first; change it with
    /// <see cref="Into"/> or with the <c>Card(title, icon, content)</c> overload) and returns the element it made, so it can be
    /// customised or given a rule with <see cref="VisibleWhen(VisualElement, string, object, bool)"/>. One shared 200 ms scheduler drives the state pills, the live
    /// strips and the polled rules; it is paused while the root is not on a panel and never leaks. Value changes (Undo and Redo
    /// included) re-evaluate the rules through a single tracker. Created by <see cref="VeneerInspector"/>; also usable on its own with any
    /// <see cref="SerializedObject"/> and root (settings pages, windows).
    /// </summary>
    public sealed class VeneerInspectorBuilder
    {
        private const long TickMilliseconds = 200;
        private const double RevealGraceSeconds = 0.3;

        private readonly SerializedObject serializedObject;
        private readonly VisualElement root;
        private readonly string sessionPrefix;
        private readonly List<VisualElement> containers = new List<VisualElement>(8);
        private readonly List<PillBinding> pills = new List<PillBinding>(2);
        private readonly List<LiveBinding> lives = new List<LiveBinding>(2);
        private readonly List<Rule> rules = new List<Rule>(8);
        private readonly List<Action> polls = new List<Action>(2);

        private VeneerBrand brand;
        private VisualElement headerRight;
        private Label headerPill;
        private IVisualElementScheduledItem tick;
        private int tabCounter;
        private bool finished;
        private double attachedAt = double.PositiveInfinity; // EditorApplication.timeSinceStartup of the last attach of the root (never: infinity)

        /// <summary>Creates a builder.</summary>
        /// <param name="serializedObject">The data the fields edit.</param>
        /// <param name="root">The element content is added to (normally made by <see cref="VeneerStyle.Root"/>).</param>
        /// <param name="brand">The brand used for icon lookup and the header; may be null.</param>
        /// <param name="sessionPrefix">Prefix of the <see cref="SessionState"/> keys of the tabs; null uses the target's type name.</param>
        public VeneerInspectorBuilder(SerializedObject serializedObject, VisualElement root, VeneerBrand brand = null, string sessionPrefix = null)
        {
            if (serializedObject == null)
            {
                throw new ArgumentNullException(nameof(serializedObject));
            }

            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            this.serializedObject = serializedObject;
            this.root = root;
            this.brand = brand;
            UnityEngine.Object target = serializedObject.targetObject;
            this.sessionPrefix = sessionPrefix ?? "Veneer." + (target != null ? target.GetType().FullName : "Object");
        }

        /// <summary>The data the fields edit.</summary>
        public SerializedObject SerializedObject => serializedObject;

        /// <summary>The root element.</summary>
        public VisualElement Root => root;

        /// <summary>The brand in use (never null; the default brand when none was set).</summary>
        public VeneerBrand CurrentBrand => brand ?? VeneerBrand.Default;

        /// <summary>The right end of the header card's top line, where extra icon buttons can be added; null before <see cref="Header(string, string, string)"/> was called.</summary>
        public VisualElement HeaderRight => headerRight;

        /// <summary>The container new content is added to.</summary>
        public VisualElement Current => containers.Count > 0 ? containers[containers.Count - 1] : root;

        /// <summary>Sets (or changes) the brand: its accent preset class and extra sheet are applied to the root, and later icons look in its folder.</summary>
        public VeneerInspectorBuilder Brand(VeneerBrand newBrand)
        {
            brand = newBrand;
            VeneerStyle.ApplyBrand(root, newBrand);
            return this;
        }

        // ------------------------------------------------------------------ containers

        /// <summary>Makes <paramref name="container"/> the current container until the returned scope is disposed (use it with <c>using</c>).</summary>
        public Scope Into(VisualElement container)
        {
            containers.Add(container);
            return new Scope(this);
        }

        /// <summary>The scope returned by <see cref="Into"/>; disposing it restores the previous container. A struct, so it allocates nothing.</summary>
        public readonly struct Scope : IDisposable
        {
            private readonly VeneerInspectorBuilder owner;

            internal Scope(VeneerInspectorBuilder owner)
            {
                this.owner = owner;
            }

            /// <summary>Restores the container that was current before <see cref="Into"/>.</summary>
            public void Dispose()
            {
                if (owner != null && owner.containers.Count > 0)
                {
                    owner.containers.RemoveAt(owner.containers.Count - 1);
                }
            }
        }

        /// <summary>Adds any element to the current container and returns it.</summary>
        public T Add<T>(T element) where T : VisualElement
        {
            Current.Add(element);
            return element;
        }

        /// <summary>
        /// Adds the header card: an icon (or the brand's logo), the title with an optional subtitle, and an empty right end for a state pill
        /// and icon buttons (<see cref="HeaderRight"/>). Add it first.
        /// </summary>
        /// <param name="title">The title.</param>
        /// <param name="subtitle">A muted line under the title; may be null.</param>
        /// <param name="iconName">A kit or brand icon name; null shows the brand's logo when it has one.</param>
        public VisualElement Header(string title, string subtitle = null, string iconName = null)
        {
            var card = VeneerStyle.Card();
            var top = VeneerStyle.Row();
            if (!string.IsNullOrEmpty(iconName))
            {
                top.Add(VeneerStyle.IconElement(iconName, "vn-icon--accent vn-icon--lg", brand));
            }
            else if (brand != null && brand.Logo != null)
            {
                var logo = new VisualElement();
                logo.AddToClassList("vn-titlebar__logo");
                logo.pickingMode = PickingMode.Ignore;
                logo.style.backgroundImage = new StyleBackground(brand.Logo);
                top.Add(logo);
            }

            var text = new VisualElement();
            text.AddToClassList("vn-header__text");
            text.AddToClassList("vn-grow");
            Label titleLabel = VeneerStyle.Title(title);
            titleLabel.AddToClassList("vn-ellipsis");
            text.Add(titleLabel);
            if (!string.IsNullOrEmpty(subtitle))
            {
                text.Add(VeneerStyle.Subtitle(subtitle));
            }

            top.Add(text);
            headerRight = VeneerStyle.Row();
            top.Add(headerRight);
            card.Add(top);
            Current.Add(card);
            return card;
        }

        /// <summary>The same as <see cref="Header(string, string, string)"/>, with a state pill at the right end that the caller updates.</summary>
        public VisualElement Header(string title, string subtitle, string iconName, out Label pill)
        {
            VisualElement card = Header(title, subtitle, iconName);
            headerPill = VeneerStyle.Pill(string.Empty, "info");
            headerRight.Add(headerPill);
            pill = headerPill;
            return card;
        }

        /// <summary>
        /// Adds a state pill (to the header's right end when there is a header) that the shared 200 ms scheduler keeps in step with
        /// <paramref name="provider"/>: its text and its kind (<c>info</c>, <c>ok</c>, <c>idle</c>, <c>warn</c>, <c>error</c>, <c>special</c>). Nothing is touched when neither changed.
        /// </summary>
        public Label StatePill(Func<(string text, string kind)> provider)
        {
            Label pill = headerPill;
            if (pill == null)
            {
                pill = VeneerStyle.Pill(string.Empty, "info");
                if (headerRight != null)
                {
                    headerRight.Add(pill);
                    headerPill = pill;
                }
                else
                {
                    Current.Add(pill);
                }
            }

            pills.Add(new PillBinding { Pill = pill, Provider = provider });
            return pill;
        }

        /// <summary>
        /// Adds a tab bar and one page per tab to the current container; the selected tab is remembered for the editor session under
        /// <paramref name="sessionKey"/>. Returns the page containers in tab order: fill them with <see cref="Into"/>.
        /// </summary>
        public VisualElement[] Tabs(string sessionKey, params (string label, string icon)[] tabs)
        {
            var labels = new string[tabs.Length];
            var icons = new string[tabs.Length];
            for (int i = 0; i < tabs.Length; i++)
            {
                labels[i] = tabs[i].label;
                icons[i] = tabs[i].icon;
            }

            Current.Add(VeneerStyle.TabView(sessionKey, labels, icons, null, brand, out VisualElement[] pages, out _));
            return pages;
        }

        /// <summary>The same as <see cref="Tabs(string, ValueTuple{string, string}[])"/> with a session key made from the target's type and the number of tab bars so far.</summary>
        public VisualElement[] Tabs(params (string label, string icon)[] tabs)
        {
            return Tabs(sessionPrefix + ".tab" + tabCounter++, tabs);
        }

        /// <summary>Adds a card (title row with an optional icon) to the current container and returns it; fill it with <see cref="Into"/>.</summary>
        public VisualElement Card(string title = null, string icon = null)
        {
            VisualElement card = VeneerStyle.Card(title, icon, brand);
            Current.Add(card);
            return card;
        }

        /// <summary>Adds a card and runs <paramref name="content"/> with the card as the current container.</summary>
        public VisualElement Card(string title, string icon, Action content)
        {
            VisualElement card = Card(title, icon);
            using (Into(card))
            {
                content();
            }

            return card;
        }

        /// <summary>Adds a section heading (small upper-case muted label, no border) to the current container.</summary>
        public VisualElement Section(string title)
        {
            VisualElement section = VeneerStyle.Section(title);
            Current.Add(section);
            return section;
        }

        /// <summary>Adds a horizontal row (6 px between children) to the current container; fill it with <see cref="Into"/>.</summary>
        public VisualElement Row(bool wrap = false)
        {
            VisualElement row = VeneerStyle.Row(wrap);
            Current.Add(row);
            return row;
        }

        /// <summary>Adds empty vertical space.</summary>
        public VisualElement Space(float height = 6f)
        {
            var space = new VisualElement();
            space.style.height = height;
            space.style.flexShrink = 0f;
            Current.Add(space);
            return space;
        }

        // ------------------------------------------------------------------ fields

        /// <summary>
        /// Adds a plain bound <see cref="PropertyField"/> (the tooltip, Range/Min and prefab marks come from Unity). A <c>[Header]</c> or
        /// <c>[Space]</c> on the field is not drawn again unless <paramref name="keepDecorators"/> is true.
        /// </summary>
        /// <param name="path">The property path, for example <c>maxSpeed</c> or <c>stats.health</c>.</param>
        /// <param name="label">The label; null keeps the property's own.</param>
        public VisualElement Field(string path, string label = null, bool keepDecorators = false)
        {
            SerializedProperty property = Find(path, out VisualElement problem);
            if (property == null)
            {
                return problem;
            }

            // A plain enum (no flags, no drawer, no attribute that needs one) is drawn as the animated dropdown; anything else stays native.
            if (VeneerDropdown.IsPlainEnum(property, keepDecorators))
            {
                return Add(VeneerControls.EnumDropdown(property, label, brand));
            }

            var field = label == null ? new PropertyField(property) : new PropertyField(property, label);
            if (!keepDecorators)
            {
                field.AddToClassList("vn-field--no-decorators");
            }

            Current.Add(field);
            return field;
        }

        /// <summary>Adds a number row (label, clamped number, unit); see <see cref="VeneerControls.NumberRow"/>.</summary>
        public VisualElement Number(string path, string label = null, string unit = null)
        {
            SerializedProperty property = Find(path, out VisualElement problem);
            return property == null ? problem : Add(VeneerControls.NumberRow(property, label, unit));
        }

        /// <summary>Adds a slider with a number field and end captions; see <see cref="VeneerControls.SliderRow"/>.</summary>
        public VisualElement Slider(
            string path, string label = null, string leftEnd = null, string rightEnd = null, float low = float.NaN, float high = float.NaN, string unit = null)
        {
            SerializedProperty property = Find(path, out VisualElement problem);
            return property == null ? problem : Add(VeneerControls.SliderRow(property, label, leftEnd, rightEnd, low, high, unit));
        }

        /// <summary>Adds one toggle chip for a bool property; see <see cref="VeneerControls.ToggleChip"/>.</summary>
        public VisualElement Chip(string path, string label = null, string icon = null)
        {
            SerializedProperty property = Find(path, out VisualElement problem);
            return property == null ? problem : Add(VeneerControls.ToggleChip(property, label, icon, brand));
        }

        /// <summary>Adds a wrapping row of toggle chips, one per (path, label, icon) entry.</summary>
        public VisualElement Chips(params (string path, string label, string icon)[] chips)
        {
            VisualElement row = Row(true);
            for (int i = 0; i < chips.Length; i++)
            {
                SerializedProperty property = Find(chips[i].path, out VisualElement problem);
                row.Add(property == null ? problem : VeneerControls.ToggleChip(property, chips[i].label, chips[i].icon, brand));
            }

            return row;
        }

        /// <summary>Adds chips for a flags enum; see <see cref="VeneerControls.FlagChips"/>.</summary>
        public VisualElement Flags(string path, string[] names = null, string[] icons = null)
        {
            SerializedProperty property = Find(path, out VisualElement problem);
            return property == null ? problem : Add(VeneerControls.FlagChips(property, names, icons, brand));
        }

        /// <summary>Adds a segmented icon group for an enum; see <see cref="VeneerControls.EnumSegmented"/>.</summary>
        public VisualElement Enum(string path, string[] icons = null, string[] tooltips = null, string label = null)
        {
            SerializedProperty property = Find(path, out VisualElement problem);
            return property == null ? problem : Add(VeneerControls.EnumSegmented(property, icons, tooltips, label, brand));
        }

        /// <summary>
        /// Adds an animated dropdown row for an enum; see <see cref="VeneerControls.EnumDropdown"/>. A property that is not an enum is
        /// added with <see cref="Field"/>, and a flags enum as chips (<see cref="Flags"/>).
        /// </summary>
        public VisualElement Dropdown(string path, string label = null)
        {
            SerializedProperty property = Find(path, out VisualElement problem);
            if (property == null)
            {
                return problem;
            }

            if (property.propertyType != SerializedPropertyType.Enum)
            {
                return Field(path, label);
            }

            Type enumType = VeneerFieldInfo.GetValueType(property);
            if (enumType != null && Attribute.IsDefined(enumType, typeof(FlagsAttribute), false))
            {
                return Flags(path);
            }

            return Add(VeneerControls.EnumDropdown(property, label, brand));
        }

        /// <summary>Adds an object slot with a thumbnail and a ping button; see <see cref="VeneerControls.ObjectSlot"/>.</summary>
        public VisualElement Object(string path, string label = null, Type objectType = null)
        {
            SerializedProperty property = Find(path, out VisualElement problem);
            return property == null ? problem : Add(VeneerControls.ObjectSlot(property, label, objectType, brand));
        }

        /// <summary>Adds a styled reorderable list for an array or List property; see <see cref="VeneerControls.ListEditor"/>.</summary>
        public VisualElement List(string path, string label = null)
        {
            SerializedProperty property = Find(path, out VisualElement problem);
            return property == null ? problem : Add(VeneerControls.ListEditor(property, label, brand));
        }

        /// <summary>Adds a vector row; see <see cref="VeneerControls.Vector3Row"/>.</summary>
        public VisualElement Vector3(string path, string label = null)
        {
            SerializedProperty property = Find(path, out VisualElement problem);
            return property == null ? problem : Add(VeneerControls.Vector3Row(property, label));
        }

        /// <summary>Adds a colour row; see <see cref="VeneerControls.ColorRow"/>.</summary>
        public VisualElement Color(string path, string label = null)
        {
            SerializedProperty property = Find(path, out VisualElement problem);
            return property == null ? problem : Add(VeneerControls.ColorRow(property, label));
        }

        /// <summary>Adds a curve row; see <see cref="VeneerControls.CurveRow"/>.</summary>
        public VisualElement Curve(string path, string label = null)
        {
            SerializedProperty property = Find(path, out VisualElement problem);
            return property == null ? problem : Add(VeneerControls.CurveRow(property, label));
        }

        // ------------------------------------------------------------------ messages, buttons, live

        /// <summary>Adds a message row. <paramref name="kind"/> is <c>info</c>, <c>warn</c> or <c>error</c>.</summary>
        public VisualElement Hint(string text, string kind = "info")
        {
            VisualElement hint = VeneerStyle.Hint(text, kind, brand);
            Current.Add(hint);
            return hint;
        }

        /// <summary>
        /// Adds a message row that shows only while <paramref name="text"/> returns a non-empty string, and shows that string. It is
        /// re-evaluated whenever the serialized data changes (and on the 200 ms tick when <paramref name="poll"/> is true, for conditions
        /// that depend on other components or the scene).
        /// </summary>
        public VisualElement ConditionalHint(Func<string> text, string kind = "warn", bool poll = false)
        {
            VisualElement hint = VeneerStyle.Hint(string.Empty, kind, out Label label, brand);
            VeneerStyle.SetDisplay(hint, false);
            Current.Add(hint);
            rules.Add(new Rule { Element = hint, Text = text, TextLabel = label, Poll = poll });
            return hint;
        }

        /// <summary>Adds a button (an accent-filled one when <paramref name="primary"/> is true).</summary>
        public Button Button(string label, string icon, Action action, string tooltip = null, bool primary = false)
        {
            Button button = VeneerStyle.Button(label, icon, tooltip, action, primary, brand);
            Current.Add(button);
            return button;
        }

        /// <summary>
        /// Adds a strip that exists only while the game is playing and shows the text <paramref name="provider"/> returns, refreshed by the
        /// 200 ms scheduler (the label is written only when the text changed).
        /// </summary>
        public VisualElement Live(Func<string> provider)
        {
            var strip = new VisualElement();
            strip.AddToClassList("vn-live");
            var label = new Label(string.Empty);
            label.AddToClassList("vn-live__text");
            strip.Add(label);
            VeneerStyle.SetDisplay(strip, false);
            Current.Add(strip);
            lives.Add(new LiveBinding { Strip = strip, Label = label, Provider = provider });
            return strip;
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the shared 200 ms scheduler, with the pills and polled rules (paused while the root is not on a
        /// panel, like them). The first run is on the first tick, not at once. An action that throws is logged once and removed.
        /// Allocate nothing inside it: it runs five times a second.
        /// </summary>
        public void Poll(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            polls.Add(action);
            if (finished && tick == null)
            {
                StartTick();
            }
        }

        // ------------------------------------------------------------------ rules

        /// <summary>
        /// Shows <paramref name="element"/> only while the property at <paramref name="path"/> equals <paramref name="value"/> (a bool, an
        /// int or enum value, a string, a float, or null for an empty object reference); <paramref name="invert"/> shows it while the
        /// property does NOT equal it. When the selected objects differ the element stays visible. Re-evaluated on every data change.
        /// </summary>
        public VisualElement VisibleWhen(VisualElement element, string path, object value, bool invert = false)
        {
            SerializedProperty property = Find(path, out _);
            if (property == null)
            {
                return element;
            }

            SerializedProperty watched = property.Copy();
            return VisibleWhen(element, () => watched.hasMultipleDifferentValues || (Matches(watched, value) != invert));
        }

        /// <summary>
        /// Shows <paramref name="element"/> only while <paramref name="visible"/> returns true; re-evaluated on every data change (and on the
        /// 200 ms tick when <paramref name="poll"/> is true).
        /// </summary>
        public VisualElement VisibleWhen(VisualElement element, Func<bool> visible, bool poll = false)
        {
            rules.Add(new Rule { Element = element, Visible = visible, Poll = poll });
            return element;
        }

        /// <summary>
        /// Called once after <c>Build</c>: binds the root, evaluates every rule, and starts the tracker and the scheduler when something
        /// needs them. <see cref="VeneerInspector"/> calls it; call it yourself when you use the builder on its own.
        /// </summary>
        public void Finish()
        {
            if (finished)
            {
                return;
            }

            finished = true;
            root.RegisterCallback<AttachToPanelEvent>(OnRootAttachedForMotion);
            if (root.panel != null)
            {
                attachedAt = EditorApplication.timeSinceStartup;
            }

            root.Bind(serializedObject);
            if (rules.Count > 0)
            {
                root.TrackSerializedObjectValue(serializedObject, changed => EvaluateRules(false));
            }

            bool needsTick = pills.Count > 0 || lives.Count > 0 || polls.Count > 0 || HasPolledRule();
            if (needsTick)
            {
                StartTick();
            }

            EvaluateRules(false);
            RefreshPills();
        }

        private void StartTick()
        {
            tick = root.schedule.Execute(Tick).Every(TickMilliseconds);
            root.RegisterCallback<AttachToPanelEvent>(OnAttached);
            root.RegisterCallback<DetachFromPanelEvent>(OnDetached);
        }

        // ------------------------------------------------------------------ internals

        private SerializedProperty Find(string path, out VisualElement problem)
        {
            SerializedProperty property = serializedObject.FindProperty(path);
            if (property != null)
            {
                problem = null;
                return property;
            }

            // A missing path is a bug of the layout code: show it where it is instead of leaving a silent hole.
            problem = VeneerStyle.Hint("Missing property: " + path, "error", brand);
            Current.Add(problem);
            return null;
        }

        private bool HasPolledRule()
        {
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i].Poll)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnAttached(AttachToPanelEvent evt)
        {
            if (tick != null)
            {
                tick.Resume();
            }
        }

        private void OnDetached(DetachFromPanelEvent evt)
        {
            if (tick != null)
            {
                tick.Pause();
            }
        }

        private void Tick()
        {
            if (root.panel == null || serializedObject.targetObject == null)
            {
                if (tick != null)
                {
                    tick.Pause();
                }

                return;
            }

            RefreshPills();
            bool playing = Application.isPlaying;
            for (int i = 0; i < lives.Count; i++)
            {
                LiveBinding live = lives[i];
                VeneerStyle.SetDisplay(live.Strip, playing);
                if (playing)
                {
                    string text = live.Provider();
                    if (!string.Equals(live.Label.text, text, StringComparison.Ordinal))
                    {
                        live.Label.text = text;
                    }
                }
            }

            EvaluateRules(true);
            RunPolls();
        }

        private void RunPolls()
        {
            for (int i = 0; i < polls.Count; i++)
            {
                try
                {
                    polls[i]();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    polls.RemoveAt(i);
                    i--;
                }
            }
        }

        private void RefreshPills()
        {
            for (int i = 0; i < pills.Count; i++)
            {
                (string text, string kind) state = pills[i].Provider();
                VeneerStyle.SetPill(pills[i].Pill, state.kind, state.text);
            }
        }

        private void EvaluateRules(bool polledOnly)
        {
            if (rules.Count == 0 || serializedObject.targetObject == null)
            {
                return;
            }

            serializedObject.UpdateIfRequiredOrScript();
            for (int i = 0; i < rules.Count; i++)
            {
                Rule rule = rules[i];
                if (polledOnly && !rule.Poll)
                {
                    continue;
                }

                if (rule.Text != null)
                {
                    string text = rule.Text();
                    bool show = !string.IsNullOrEmpty(text);
                    ApplyRuleDisplay(rule.Element, show);
                    if (show && !string.Equals(rule.TextLabel.text, text, StringComparison.Ordinal))
                    {
                        rule.TextLabel.text = text;
                    }
                }
                else
                {
                    ApplyRuleDisplay(rule.Element, rule.Visible());
                }
            }
        }

        // Shows or hides a rule's element; an element that goes from hidden to visible after the first 300 ms on a panel fades in
        // (VeneerStyle.Reveal does nothing when motion is off). Hiding is instant. Allocation free unless a reveal starts.
        private void ApplyRuleDisplay(VisualElement element, bool show)
        {
            bool wasHidden = show && element.style.display.value == DisplayStyle.None;
            VeneerStyle.SetDisplay(element, show);
            if (wasHidden && EditorApplication.timeSinceStartup - attachedAt > RevealGraceSeconds)
            {
                VeneerStyle.Reveal(element);
            }
        }

        private void OnRootAttachedForMotion(AttachToPanelEvent evt)
        {
            attachedAt = EditorApplication.timeSinceStartup;
        }

        private static bool Matches(SerializedProperty property, object value)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    return value is bool flag && property.boolValue == flag;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.LayerMask:
                    return value != null && property.intValue == Convert.ToInt32(value);
                case SerializedPropertyType.Float:
                    return value != null && Mathf.Approximately(property.floatValue, Convert.ToSingle(value));
                case SerializedPropertyType.String:
                    return string.Equals(property.stringValue, value as string, StringComparison.Ordinal);
                case SerializedPropertyType.ObjectReference:
                    return value == null ? property.objectReferenceValue == null : property.objectReferenceValue != null;
                default:
                    return false;
            }
        }

        private sealed class Rule
        {
            public VisualElement Element;
            public Func<bool> Visible;
            public Func<string> Text;
            public Label TextLabel;
            public bool Poll;
        }

        private struct PillBinding
        {
            public Label Pill;
            public Func<(string text, string kind)> Provider;
        }

        private struct LiveBinding
        {
            public VisualElement Strip;
            public Label Label;
            public Func<string> Provider;
        }
    }
}
