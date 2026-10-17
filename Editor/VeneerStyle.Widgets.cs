using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    // The widgets of the kit: plain VisualElement trees that only carry USS classes (see Styles/Veneer.uss).
    public static partial class VeneerStyle
    {
        private const string TabsClass = "vn-tabs";
        private const string SegmentedSelectedClass = "vn-segmented__button--selected";
        private const string TabSelectedClass = "vn-tab--selected";
        private const string ChipOnClass = "vn-chip--on";
        private const float MixedOpacity = 0.55f;

        // Pill kinds in the order of the class list below; an unknown kind falls back to the first one (info).
        // Legacy names (awake, asleep, kinematic) are accepted so code written for the first Optimized Rigidbody sheet migrates unchanged.
        private static readonly string[] PillKinds = { "info", "ok", "idle", "warn", "error", "special", "awake", "asleep", "kinematic" };
        private static readonly int[] PillKindToClass = { 0, 1, 2, 3, 4, 5, 1, 2, 5 };

        private static readonly string[] PillClasses =
        {
            "vn-pill--info", "vn-pill--ok", "vn-pill--idle", "vn-pill--warn", "vn-pill--error", "vn-pill--special",
        };

        private static readonly string[] BarKinds = { "ok", "warn", "error", "special", "awake", "kinematic" };
        private static readonly int[] BarKindToClass = { 0, 1, 2, 3, 0, 3 };
        private static readonly string[] BarClasses = { "vn-bar--ok", "vn-bar--warn", "vn-bar--error", "vn-bar--special" };

        // Boxed small ints made once, so storing a selection in userData allocates nothing (-1 is stored at index 0).
        private static readonly object[] BoxedSelections = BuildBoxedSelections(16);

        // ------------------------------------------------------------------ basic elements

        /// <summary>Shows or hides an element without removing it (display flex or none). Null is ignored.</summary>
        public static void SetDisplay(VisualElement element, bool visible)
        {
            if (element != null)
            {
                element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>A small icon element (<c>vn-icon</c>, 16 px, muted tint) showing the glyph <paramref name="icon"/>; it ignores the mouse.</summary>
        /// <param name="icon">Icon name (see the README list).</param>
        /// <param name="extraClass">Optional extra class, for example <c>vn-icon--accent</c>, <c>vn-icon--lg</c> or a state tint.</param>
        /// <param name="brand">The brand whose icon folder is searched after the kit's; may be null.</param>
        public static VisualElement IconElement(string icon, string extraClass = null, VeneerBrand brand = null)
        {
            var element = new VisualElement();
            element.AddToClassList("vn-icon");
            if (!string.IsNullOrEmpty(extraClass))
            {
                element.AddToClassList(extraClass);
            }

            element.pickingMode = PickingMode.Ignore;
            SetIcon(element, icon, brand);
            return element;
        }

        /// <summary>Shows another icon in an element made by <see cref="IconElement"/> (only the background image changes).</summary>
        public static void SetIcon(VisualElement iconElement, string icon, VeneerBrand brand = null)
        {
            iconElement.style.backgroundImage = new StyleBackground(Icon(icon, brand));
        }

        /// <summary>A horizontal row (<c>vn-row</c>): children are centred vertically and 6 px apart. <paramref name="wrap"/> lets them wrap.</summary>
        public static VisualElement Row(bool wrap = false)
        {
            var row = new VisualElement();
            row.AddToClassList("vn-row");
            if (wrap)
            {
                row.AddToClassList("vn-row--wrap");
            }

            return row;
        }

        /// <summary>A flexible gap that pushes the elements after it to the end of a row.</summary>
        public static VisualElement Spacer()
        {
            var spacer = new VisualElement();
            spacer.AddToClassList("vn-spacer");
            spacer.pickingMode = PickingMode.Ignore;
            return spacer;
        }

        /// <summary>A small muted label (<c>vn-label-muted</c>).</summary>
        public static Label MutedLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("vn-label-muted");
            return label;
        }

        /// <summary>A single-line muted label that is cut with an ellipsis instead of wrapping; it shrinks to make room for its siblings.</summary>
        public static Label EllipsisLabel(string text = null)
        {
            var label = MutedLabel(text ?? string.Empty);
            label.AddToClassList("vn-grow");
            label.AddToClassList("vn-ellipsis");
            return label;
        }

        /// <summary>A bold title label (<c>vn-title</c>).</summary>
        public static Label Title(string text)
        {
            var label = new Label(text);
            label.AddToClassList("vn-title");
            return label;
        }

        /// <summary>A small muted line under a title (<c>vn-subtitle</c>), cut with an ellipsis.</summary>
        public static Label Subtitle(string text)
        {
            var label = new Label(text);
            label.AddToClassList("vn-subtitle");
            label.AddToClassList("vn-ellipsis");
            return label;
        }

        /// <summary>A section heading without a card (<c>vn-section</c>): a small upper-case muted label.</summary>
        public static VisualElement Section(string title)
        {
            var section = new VisualElement();
            section.AddToClassList("vn-section");
            var label = new Label(title.ToUpperInvariant());
            label.AddToClassList("vn-section__label");
            section.Add(label);
            return section;
        }

        /// <summary>
        /// A label column plus a control, in one line with a fixed label width (<c>vn-field-row</c>), so a card's fields line up.
        /// </summary>
        /// <param name="label">The label text.</param>
        /// <param name="control">The control; it takes the rest of the line.</param>
        /// <param name="tooltip">Tooltip of the whole row; may be null.</param>
        /// <param name="labelElement">The label, for the prefab-override mark.</param>
        public static VisualElement FieldRow(string label, VisualElement control, string tooltip, out Label labelElement)
        {
            var row = new VisualElement();
            row.AddToClassList("vn-field-row");
            row.tooltip = tooltip;
            labelElement = new Label(label);
            labelElement.AddToClassList("vn-field-row__label");
            row.Add(labelElement);
            control.AddToClassList("vn-field-row__control");
            row.Add(control);
            return row;
        }

        /// <summary>
        /// A bordered card (<c>vn-card</c>). With a title and/or icon it starts with a title row (<c>vn-card__title</c>: icon plus an
        /// upper-case muted label). Add the content as children of the returned element.
        /// </summary>
        public static VisualElement Card(string title = null, string icon = null, VeneerBrand brand = null)
        {
            var card = new VisualElement();
            card.AddToClassList("vn-card");
            bool hasTitle = !string.IsNullOrEmpty(title);
            bool hasIcon = !string.IsNullOrEmpty(icon);
            if (hasTitle || hasIcon)
            {
                var row = new VisualElement();
                row.AddToClassList("vn-card__title");
                if (hasIcon)
                {
                    row.Add(IconElement(icon, null, brand));
                }

                if (hasTitle)
                {
                    var label = new Label(title.ToUpperInvariant());
                    label.AddToClassList("vn-card__title-label");
                    row.Add(label);
                }

                card.Add(row);
            }

            return card;
        }

        // ------------------------------------------------------------------ pills, buttons

        /// <summary>A rounded state label <c>vn-pill vn-pill--&lt;kind&gt;</c>.</summary>
        /// <param name="text">The text.</param>
        /// <param name="kind"><c>info</c> (accent), <c>ok</c>, <c>idle</c>, <c>warn</c>, <c>error</c> or <c>special</c>; anything else is info.</param>
        public static Label Pill(string text, string kind = "info")
        {
            var pill = new Label(text);
            pill.AddToClassList("vn-pill");
            pill.AddToClassList(PillClasses[PillIndex(kind)]);
            return pill;
        }

        /// <summary>Changes the kind and the text of a pill made by <see cref="Pill"/>; nothing is touched when it already shows them.</summary>
        public static void SetPill(Label pill, string kind, string text)
        {
            int index = PillIndex(kind);
            if (!pill.ClassListContains(PillClasses[index]))
            {
                for (int i = 0; i < PillClasses.Length; i++)
                {
                    pill.EnableInClassList(PillClasses[i], i == index);
                }
            }

            if (!string.Equals(pill.text, text, StringComparison.Ordinal))
            {
                pill.text = text;
            }
        }

        /// <summary>
        /// A 24x24 button (<c>vn-icon-button</c>) that shows only an icon; <paramref name="tooltip"/> says what it does. Add the class
        /// <c>vn-icon-button--on</c> to show a toggled-on look.
        /// </summary>
        public static Button IconButton(string icon, string tooltip, Action clicked, VeneerBrand brand = null)
        {
            var button = new Button();
            button.AddToClassList("vn-icon-button");
            button.tooltip = tooltip;
            button.Add(IconElement(icon, null, brand));
            if (clicked != null)
            {
                button.clicked += clicked;
            }

            return button;
        }

        /// <summary>A text button (<c>vn-button</c>) with an optional icon; <paramref name="primary"/> makes it the accent-filled main action.</summary>
        public static Button Button(string text, string icon, string tooltip, Action clicked, bool primary = false, VeneerBrand brand = null)
        {
            var button = new Button();
            button.AddToClassList("vn-button");
            if (primary)
            {
                button.AddToClassList("vn-button--primary");
            }

            button.tooltip = tooltip;
            if (!string.IsNullOrEmpty(icon))
            {
                button.Add(IconElement(icon, null, brand));
            }

            var label = new Label(text);
            label.AddToClassList("vn-button__label");
            label.pickingMode = PickingMode.Ignore;
            button.Add(label);
            if (clicked != null)
            {
                button.clicked += clicked;
            }

            return button;
        }

        // ------------------------------------------------------------------ segmented, tabs

        /// <summary>
        /// A group of 2-4 buttons of which one is selected (<c>vn-segmented</c>, children <c>vn-segmented__button</c>). A button shows its
        /// icon; an entry with no icon shows its tooltip text instead. Clicking an unselected button selects it and calls
        /// <paramref name="changed"/> with its index. Read and set the selection with <see cref="GetSegmentedSelected"/> and <see cref="SetSegmentedSelected"/>.
        /// </summary>
        /// <param name="icons">Icon name per button (null or empty entries fall back to text).</param>
        /// <param name="tooltips">Tooltip (and fallback text) per button; may be null or shorter.</param>
        /// <param name="selected">Index selected at the start; -1 for none.</param>
        /// <param name="changed">Called after a click changed the selection (not when the selection is set from code).</param>
        /// <param name="brand">Brand for icon lookup; may be null.</param>
        public static VisualElement SegmentedIcons(string[] icons, string[] tooltips, int selected, Action<int> changed, VeneerBrand brand = null)
        {
            var group = new VisualElement();
            group.AddToClassList("vn-segmented");
            int count = icons == null ? 0 : icons.Length;
            for (int i = 0; i < count; i++)
            {
                int index = i;
                var button = new Button();
                button.AddToClassList("vn-segmented__button");
                string tip = tooltips != null && i < tooltips.Length ? tooltips[i] : null;
                button.tooltip = tip;
                if (!string.IsNullOrEmpty(icons[i]))
                {
                    button.Add(IconElement(icons[i], null, brand));
                }
                else if (!string.IsNullOrEmpty(tip))
                {
                    var text = new Label(tip);
                    text.AddToClassList("vn-segmented__label");
                    text.pickingMode = PickingMode.Ignore;
                    button.Add(text);
                }

                button.clicked += () =>
                {
                    if (GetSegmentedSelected(group) == index)
                    {
                        return;
                    }

                    SetSegmentedSelected(group, index);
                    if (changed != null)
                    {
                        changed(index);
                    }
                };
                group.Add(button);
            }

            SetSegmentedSelected(group, selected);
            return group;
        }

        /// <summary>
        /// A tab bar (<c>vn-tabs</c>, children <c>vn-tab</c>: icon plus label) with one selected tab; the selection works like
        /// <see cref="SegmentedIcons"/>. Use <see cref="TabView"/> when the tabs also switch pages and are remembered.
        /// </summary>
        /// <param name="icons">Icon name per tab (null or empty entries show no icon).</param>
        /// <param name="labels">Text per tab (null or empty entries show no text, for icon-only tabs).</param>
        /// <param name="tooltips">Tooltip per tab (may be null or shorter).</param>
        /// <param name="selected">Index selected at the start.</param>
        /// <param name="changed">Called after a click changed the selection.</param>
        /// <param name="brand">Brand for icon lookup; may be null.</param>
        public static VisualElement Tabs(string[] icons, string[] labels, string[] tooltips, int selected, Action<int> changed, VeneerBrand brand = null)
        {
            var group = new VisualElement();
            group.AddToClassList(TabsClass);
            int count = Math.Max(icons == null ? 0 : icons.Length, labels == null ? 0 : labels.Length);
            for (int i = 0; i < count; i++)
            {
                int index = i;
                var tab = new Button();
                tab.AddToClassList("vn-tab");
                if (tooltips != null && i < tooltips.Length)
                {
                    tab.tooltip = tooltips[i];
                }

                if (icons != null && i < icons.Length && !string.IsNullOrEmpty(icons[i]))
                {
                    tab.Add(IconElement(icons[i], null, brand));
                }

                if (labels != null && i < labels.Length && !string.IsNullOrEmpty(labels[i]))
                {
                    var label = new Label(labels[i]);
                    label.AddToClassList("vn-tab__label");
                    label.pickingMode = PickingMode.Ignore;
                    tab.Add(label);
                }

                tab.clicked += () =>
                {
                    if (GetSegmentedSelected(group) == index)
                    {
                        return;
                    }

                    SetSegmentedSelected(group, index);
                    if (changed != null)
                    {
                        changed(index);
                    }
                };
                group.Add(tab);
            }

            SetSegmentedSelected(group, selected);
            return group;
        }

        /// <summary>
        /// A tab bar plus one page per tab in a single container. Only the selected page is shown (the others stay alive and keep their
        /// bindings); the selected tab is remembered for the editor session under <paramref name="sessionKey"/>.
        /// </summary>
        /// <param name="sessionKey">The <see cref="SessionState"/> key; use a unique one per Inspector or page.</param>
        /// <param name="labels">Text per tab.</param>
        /// <param name="icons">Icon name per tab (may be null).</param>
        /// <param name="tooltips">Tooltip per tab (may be null).</param>
        /// <param name="brand">Brand for icon lookup; may be null.</param>
        /// <param name="pages">The page containers, one per tab, already inside the returned container.</param>
        /// <param name="tabBar">The tab bar element.</param>
        public static VisualElement TabView(
            string sessionKey,
            string[] labels,
            string[] icons,
            string[] tooltips,
            VeneerBrand brand,
            out VisualElement[] pages,
            out VisualElement tabBar)
        {
            int count = labels == null ? 0 : labels.Length;
            int selected = count == 0 ? 0 : Mathf.Clamp(SessionState.GetInt(sessionKey, 0), 0, count - 1);
            var view = new VisualElement();
            var pageList = new VisualElement[count];
            tabBar = Tabs(icons, labels, tooltips, selected, index =>
            {
                SessionState.SetInt(sessionKey, index);
                for (int i = 0; i < pageList.Length; i++)
                {
                    SetDisplay(pageList[i], i == index);
                }
            }, brand);
            view.Add(tabBar);
            for (int i = 0; i < count; i++)
            {
                var page = new VisualElement();
                SetDisplay(page, i == selected);
                pageList[i] = page;
                view.Add(page);
            }

            pages = pageList;
            return view;
        }

        /// <summary>
        /// Selects button <paramref name="index"/> of a group made by <see cref="SegmentedIcons"/> or <see cref="Tabs"/> (-1 selects none).
        /// It does not call the group's <c>changed</c> callback, so it is safe for syncing the view with data. Allocation free.
        /// </summary>
        public static void SetSegmentedSelected(VisualElement group, int index)
        {
            string selectedClass = group.ClassListContains(TabsClass) ? TabSelectedClass : SegmentedSelectedClass;
            int count = group.childCount;
            for (int i = 0; i < count; i++)
            {
                group[i].EnableInClassList(selectedClass, i == index);
            }

            group.userData = BoxedSelection(index);
        }

        /// <summary>The selected index of a group made by <see cref="SegmentedIcons"/> or <see cref="Tabs"/>, or -1 when none is selected.</summary>
        public static int GetSegmentedSelected(VisualElement group)
        {
            string selectedClass = group.ClassListContains(TabsClass) ? TabSelectedClass : SegmentedSelectedClass;
            int count = group.childCount;
            for (int i = 0; i < count; i++)
            {
                if (group[i].ClassListContains(selectedClass))
                {
                    return i;
                }
            }

            return -1;
        }

        // ------------------------------------------------------------------ chips

        /// <summary>
        /// A pill-shaped toggle button (<c>vn-chip</c>, <c>vn-chip--on</c> while on) with an optional icon and a text. Clicking flips it and
        /// calls <paramref name="changed"/> with the new value. Read and set it with <see cref="IsChipOn"/> and <see cref="SetChipOn"/>.
        /// </summary>
        public static VisualElement Chip(string text, string icon, bool on, Action<bool> changed, VeneerBrand brand = null)
        {
            var chip = new Button();
            chip.AddToClassList("vn-chip");
            if (!string.IsNullOrEmpty(icon))
            {
                chip.Add(IconElement(icon, null, brand));
            }

            var label = new Label(text);
            label.AddToClassList("vn-chip__label");
            label.pickingMode = PickingMode.Ignore;
            chip.Add(label);
            chip.EnableInClassList(ChipOnClass, on);
            chip.clicked += () =>
            {
                bool next = !chip.ClassListContains(ChipOnClass);
                chip.EnableInClassList(ChipOnClass, next);
                if (changed != null)
                {
                    changed(next);
                }
            };
            return chip;
        }

        /// <summary>Whether a chip made by <see cref="Chip"/> is on.</summary>
        public static bool IsChipOn(VisualElement chip)
        {
            return chip.ClassListContains(ChipOnClass);
        }

        /// <summary>Sets a chip on or off without calling its <c>changed</c> callback.</summary>
        public static void SetChipOn(VisualElement chip, bool on)
        {
            chip.EnableInClassList(ChipOnClass, on);
        }

        /// <summary>Lights a chip, faded when only some of the selected objects have the value (<paramref name="mixed"/>).</summary>
        public static void SetChipState(VisualElement chip, bool on, bool mixed)
        {
            chip.EnableInClassList(ChipOnClass, on);
            chip.style.opacity = mixed ? MixedOpacity : 1f;
        }

        /// <summary>The text label inside a chip made by <see cref="Chip"/>, or null.</summary>
        public static Label ChipLabel(VisualElement chip)
        {
            return chip.Q<Label>(className: "vn-chip__label");
        }

        // ------------------------------------------------------------------ banner, hints, kpi, bars

        /// <summary>
        /// A header banner (<c>vn-banner</c>): the brand's banner picture cropped as the background, its logo at the left, a white title
        /// and a muted subtitle. A brand without pictures gets a plain dark banner. The banner is dark in both editor skins.
        /// </summary>
        /// <param name="brand">The brand (pictures); may be null.</param>
        /// <param name="title">The title.</param>
        /// <param name="subtitle">The subtitle; null or empty leaves it out.</param>
        public static VisualElement Banner(VeneerBrand brand, string title, string subtitle)
        {
            var banner = new VisualElement();
            banner.AddToClassList("vn-banner");
            Texture2D picture = brand != null ? brand.Banner : null;
            if (picture != null)
            {
                banner.style.backgroundImage = new StyleBackground(picture);
            }

            Texture2D logo = brand != null ? brand.Logo : null;
            if (logo != null)
            {
                var logoElement = new VisualElement();
                logoElement.AddToClassList("vn-banner__logo");
                logoElement.pickingMode = PickingMode.Ignore;
                logoElement.style.backgroundImage = new StyleBackground(logo);
                banner.Add(logoElement);
            }

            var text = new VisualElement();
            text.AddToClassList("vn-banner__text");
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("vn-banner__title");
            text.Add(titleLabel);
            if (!string.IsNullOrEmpty(subtitle))
            {
                var subtitleLabel = new Label(subtitle);
                subtitleLabel.AddToClassList("vn-banner__subtitle");
                text.Add(subtitleLabel);
            }

            banner.Add(text);
            return banner;
        }

        /// <summary>
        /// A message row (<c>vn-hint vn-hint--&lt;kind&gt;</c>): a kind icon and a wrapping text. Add a fix <see cref="Button"/> as a
        /// last child if the message has one.
        /// </summary>
        /// <param name="text">The message.</param>
        /// <param name="kind"><c>info</c>, <c>warn</c> or <c>error</c> (anything else is info).</param>
        /// <param name="textLabel">The text label, to change the message later.</param>
        /// <param name="brand">Brand for icon lookup; may be null.</param>
        public static VisualElement Hint(string text, string kind, out Label textLabel, VeneerBrand brand = null)
        {
            string kindClass;
            string icon;
            switch (kind)
            {
                case "warn":
                    kindClass = "vn-hint--warn";
                    icon = "warning";
                    break;
                case "error":
                    kindClass = "vn-hint--error";
                    icon = "error";
                    break;
                default:
                    kindClass = "vn-hint--info";
                    icon = "info";
                    break;
            }

            var hint = new VisualElement();
            hint.AddToClassList("vn-hint");
            hint.AddToClassList(kindClass);
            hint.Add(IconElement(icon, null, brand));
            textLabel = new Label(text);
            textLabel.AddToClassList("vn-hint__text");
            hint.Add(textLabel);
            return hint;
        }

        /// <summary>The same as the other <see cref="Hint(string, string, out Label, VeneerBrand)"/> for callers that do not need the label.</summary>
        public static VisualElement Hint(string text, string kind = "info", VeneerBrand brand = null)
        {
            return Hint(text, kind, out _, brand);
        }

        /// <summary>A statistic card (<c>vn-kpi</c>): a header with icon and upper-case title, and a big value label that the caller updates (starts as "-").</summary>
        public static VisualElement Kpi(string title, string icon, out Label value, VeneerBrand brand = null)
        {
            var kpi = new VisualElement();
            kpi.AddToClassList("vn-kpi");
            var header = new VisualElement();
            header.AddToClassList("vn-kpi__header");
            if (!string.IsNullOrEmpty(icon))
            {
                header.Add(IconElement(icon, null, brand));
            }

            var titleLabel = new Label(title.ToUpperInvariant());
            titleLabel.AddToClassList("vn-kpi__title");
            header.Add(titleLabel);
            kpi.Add(header);
            value = new Label("-");
            value.AddToClassList("vn-kpi__value");
            kpi.Add(value);
            return kpi;
        }

        /// <summary>A thin horizontal fill bar (<c>vn-bar</c> with a child <c>vn-bar__fill</c>); drive it with <see cref="SetBarFill"/>.</summary>
        /// <param name="kind">Optional fill colour: <c>ok</c>, <c>warn</c>, <c>error</c>, <c>special</c>; null or anything else is the accent colour.</param>
        public static VisualElement Bar(string kind = null)
        {
            var bar = new VisualElement();
            bar.AddToClassList("vn-bar");
            var fill = new VisualElement();
            fill.AddToClassList("vn-bar__fill");
            fill.pickingMode = PickingMode.Ignore;
            bar.Add(fill);
            SetBarKind(bar, kind);
            return bar;
        }

        /// <summary>Sets how full a bar made by <see cref="Bar"/> is (0..1, clamped). Allocation free.</summary>
        public static void SetBarFill(VisualElement bar, float fraction)
        {
            float clamped = fraction < 0f ? 0f : (fraction > 1f ? 1f : fraction);
            bar[0].style.width = Length.Percent(clamped * 100f);
        }

        /// <summary>Changes the fill colour of a bar made by <see cref="Bar"/> (see its <c>kind</c> values).</summary>
        public static void SetBarKind(VisualElement bar, string kind)
        {
            int selected = -1;
            for (int i = 0; i < BarKinds.Length; i++)
            {
                if (string.Equals(BarKinds[i], kind, StringComparison.Ordinal))
                {
                    selected = BarKindToClass[i];
                    break;
                }
            }

            for (int i = 0; i < BarClasses.Length; i++)
            {
                bar.EnableInClassList(BarClasses[i], i == selected);
            }
        }

        // ------------------------------------------------------------------ toolbar, search, empty state

        /// <summary>A horizontal toolbar row (<c>vn-toolbar</c>) for icon buttons and a search field.</summary>
        public static VisualElement Toolbar()
        {
            var toolbar = new VisualElement();
            toolbar.AddToClassList("vn-toolbar");
            return toolbar;
        }

        /// <summary>
        /// A rounded search box (<c>vn-search</c>): a magnifier, a text field with a placeholder and a clear button while there is text.
        /// <paramref name="changed"/> is called with the text on every edit.
        /// </summary>
        /// <param name="changed">Called with the new text; may be null.</param>
        /// <param name="field">The text field, to read or set the text.</param>
        /// <param name="placeholder">Grey hint text while empty.</param>
        /// <param name="brand">Brand for icon lookup; may be null.</param>
        public static VisualElement SearchField(Action<string> changed, out TextField field, string placeholder = "Search", VeneerBrand brand = null)
        {
            var box = new VisualElement();
            box.AddToClassList("vn-search");
            box.Add(IconElement("search", null, brand));
            var input = new TextField();
            input.textEdition.placeholder = placeholder;
            input.textEdition.hidePlaceholderOnFocus = true;
            box.Add(input);
            Button clear = IconButton("close", "Clear", () => input.value = string.Empty, brand);
            SetDisplay(clear, false);
            box.Add(clear);
            input.RegisterValueChangedCallback(evt =>
            {
                SetDisplay(clear, !string.IsNullOrEmpty(evt.newValue));
                if (changed != null)
                {
                    changed(evt.newValue);
                }
            });
            field = input;
            return box;
        }

        /// <summary>A centred empty state (<c>vn-empty</c>): a big icon, a title and a wrapping text. Add a button to the returned element if there is an action.</summary>
        public static VisualElement EmptyState(string icon, string title, string text, VeneerBrand brand = null)
        {
            return EmptyState(icon, title, text, out _, out _, brand);
        }

        /// <summary>The same as <see cref="EmptyState(string, string, string, VeneerBrand)"/>, with the title and text labels returned so they can change later.</summary>
        public static VisualElement EmptyState(
            string icon, string title, string text, out Label titleLabel, out Label textLabel, VeneerBrand brand = null)
        {
            var state = new VisualElement();
            state.AddToClassList("vn-empty");
            Texture2D logo = brand != null ? brand.Logo : null;
            if (logo != null && string.IsNullOrEmpty(icon))
            {
                var picture = new VisualElement();
                picture.AddToClassList("vn-banner__logo");
                picture.style.backgroundImage = new StyleBackground(logo);
                picture.pickingMode = PickingMode.Ignore;
                state.Add(picture);
            }
            else if (!string.IsNullOrEmpty(icon))
            {
                state.Add(IconElement(icon, "vn-icon--xl", brand));
            }

            titleLabel = new Label(title);
            titleLabel.AddToClassList("vn-empty__title");
            state.Add(titleLabel);
            textLabel = new Label(text);
            textLabel.AddToClassList("vn-empty__text");
            state.Add(textLabel);
            return state;
        }

        // ------------------------------------------------------------------ internals

        private static int PillIndex(string kind)
        {
            for (int i = 0; i < PillKinds.Length; i++)
            {
                if (string.Equals(PillKinds[i], kind, StringComparison.Ordinal))
                {
                    return PillKindToClass[i];
                }
            }

            return 0;
        }

        private static object BoxedSelection(int index)
        {
            int slot = index + 1;
            return slot >= 0 && slot < BoxedSelections.Length ? BoxedSelections[slot] : index;
        }

        private static object[] BuildBoxedSelections(int count)
        {
            var boxes = new object[count];
            for (int i = 0; i < count; i++)
            {
                boxes[i] = i - 1;
            }

            return boxes;
        }
    }
}
