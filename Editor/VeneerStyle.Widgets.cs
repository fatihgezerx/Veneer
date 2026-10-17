using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

        /// <summary>
        /// Fades and slides an element in (opacity 0 to 1, 4 px down to its place, about 160 ms; durations and distances follow
        /// <see cref="VeneerMotionKit.Scale"/>). Call it right after showing an element that was hidden; hiding stays instant. It does nothing
        /// when the element is null or not attached to a panel, or when motion is off. A running reveal, pop or pulse of the same element is
        /// finished first; the element always ends with its normal opacity and no transform.
        /// </summary>
        public static void Reveal(VisualElement element)
        {
            if (element == null || element.panel == null)
            {
                return;
            }

            float motion = VeneerMotionKit.Scale(element);
            if (motion <= 0f)
            {
                return;
            }

            GetMotionState(element).StartReveal(motion);
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
            pill.RegisterCallback(PillAttachedCallback);
            return pill;
        }

        /// <summary>
        /// Changes the kind and the text of a pill made by <see cref="Pill"/>; nothing is touched when it already shows them. When a pill that
        /// is already on screen really changes, it gives a brief pulse (scale 1.1 and back, about 180 ms; not during its first 300 ms on a panel,
        /// not more often than every 0.6 s, not when motion is off).
        /// </summary>
        public static void SetPill(Label pill, string kind, string text)
        {
            bool changed = false;
            int index = PillIndex(kind);
            if (!pill.ClassListContains(PillClasses[index]))
            {
                for (int i = 0; i < PillClasses.Length; i++)
                {
                    pill.EnableInClassList(PillClasses[i], i == index);
                }

                changed = true;
            }

            if (!string.Equals(pill.text, text, StringComparison.Ordinal))
            {
                pill.text = text;
                changed = true;
            }

            if (changed)
            {
                PulsePill(pill);
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
        /// bindings); the selected tab is remembered for the editor session under <paramref name="sessionKey"/>. When the user clicks another
        /// tab the open page fades out and slides away from the new tab, then the new page fades in and slides in from the side of the new tab
        /// (about 110 ms out, 190 ms in; scaled by <see cref="VeneerMotionKit.Scale"/>, instant when motion is off). The first build and the
        /// restored selection are never animated; a click during an animation retargets it, and the target page always ends fully visible.
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
            var switcher = new TabSwitcher(view, pageList);
            tabBar = Tabs(icons, labels, tooltips, selected, index =>
            {
                SessionState.SetInt(sessionKey, index);
                switcher.Switch(index);
            }, brand);
            view.Add(tabBar);
            for (int i = 0; i < count; i++)
            {
                var page = new VisualElement();
                SetDisplay(page, i == selected);
                pageList[i] = page;
                view.Add(page);
            }

            switcher.Initialize(tabBar, count == 0 ? -1 : selected);
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
        /// Turning it on by a click gives a quick pop (scale 1.08 and back with a springy return, about 220 ms, scaled by
        /// <see cref="VeneerMotionKit.Scale"/>); turning it off, or setting it from code, does not.
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
                if (next)
                {
                    PopChip(chip);
                }

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

        // ------------------------------------------------------------------ motion (code-driven animations)
        // Everything here uses inline USS transitions plus one-shot timers (no per-frame work). Every animation multiplies its durations and
        // distances by VeneerMotionKit.Scale, does nothing when that is 0, and removes all inline transition styles when it is done so the
        // USS rules (hover, selected, ...) apply again. Unverified in a running editor: see the report of the author.

        private const float TabLeaveMs = 110f;
        private const float TabArriveMs = 190f;
        private const float TabSlidePixels = 18f;
        private const float RevealMs = 160f;
        private const float RevealPixels = 4f;
        private const float ChipPopPeak = 1.08f;
        private const float ChipPopUpMs = 90f;
        private const float ChipPopDownMs = 130f;
        private const float PillPulsePeak = 1.1f;
        private const float PillPulseUpMs = 70f;
        private const float PillPulseDownMs = 110f;
        private const float ChipColorMs = 100f; // the 0.1 s colour transition of a chip in Veneer.uss, kept while the pop overrides the list
        private const double AttachGraceSeconds = 0.3;
        private const double PulseGapSeconds = 0.6;
        private const long FrameMs = 16;
        private const long SafetyMs = 20;

        // Shared, never modified after creation (the style system only reads them).
        private static readonly List<StylePropertyName> OpacityTranslateProperties =
            new List<StylePropertyName> { new StylePropertyName("opacity"), new StylePropertyName("translate") };

        private static readonly List<StylePropertyName> ScaleProperties = new List<StylePropertyName> { new StylePropertyName("scale") };

        private static readonly List<StylePropertyName> ChipProperties = new List<StylePropertyName>
        {
            new StylePropertyName("scale"), new StylePropertyName("background-color"), new StylePropertyName("border-color"),
        };

        private static readonly List<EasingFunction> EaseInList = new List<EasingFunction> { new EasingFunction(EasingMode.EaseIn) };
        private static readonly List<EasingFunction> EaseOutList = new List<EasingFunction> { new EasingFunction(EasingMode.EaseOut) };

        private static readonly List<EasingFunction> ChipUpEase = new List<EasingFunction>
        {
            new EasingFunction(EasingMode.EaseOut), new EasingFunction(EasingMode.Ease), new EasingFunction(EasingMode.Ease),
        };

        private static readonly List<EasingFunction> ChipDownEase = new List<EasingFunction>
        {
            new EasingFunction(EasingMode.EaseOutBack), new EasingFunction(EasingMode.Ease), new EasingFunction(EasingMode.Ease),
        };

        private static readonly StyleTranslate ZeroTranslate = new StyleTranslate(new Translate(0f, 0f));
        private static readonly StyleScale IdentityScale = new StyleScale(new Scale(Vector2.one));
        private static readonly EventCallback<AttachToPanelEvent> PillAttachedCallback = OnPillAttached;
        private static readonly ConditionalWeakTable<VisualElement, MotionState> MotionStates = new ConditionalWeakTable<VisualElement, MotionState>();

        private static readonly ConditionalWeakTable<VisualElement, MotionState>.CreateValueCallback CreateMotionState =
            element => new MotionState(element);

        private static MotionState GetMotionState(VisualElement element)
        {
            return MotionStates.GetValue(element, CreateMotionState);
        }

        private static void SetTransition(VisualElement element, List<StylePropertyName> properties, float milliseconds, List<EasingFunction> easing)
        {
            IStyle style = element.style;
            style.transitionProperty = properties;
            style.transitionDuration = new List<TimeValue>(1) { new TimeValue(milliseconds, TimeUnit.Millisecond) };
            style.transitionTimingFunction = easing;
        }

        private static void ClearTransition(VisualElement element)
        {
            IStyle style = element.style;
            style.transitionProperty = StyleKeyword.Null;
            style.transitionDuration = StyleKeyword.Null;
            style.transitionTimingFunction = StyleKeyword.Null;
        }

        private static void PopChip(VisualElement chip)
        {
            if (chip.panel == null)
            {
                return;
            }

            float motion = VeneerMotionKit.Scale(chip);
            if (motion > 0f)
            {
                GetMotionState(chip).StartPulse(motion, ChipPopPeak, ChipPopUpMs, ChipPopDownMs, true);
            }
        }

        private static void PulsePill(Label pill)
        {
            if (pill.panel == null)
            {
                return;
            }

            float motion = VeneerMotionKit.Scale(pill);
            if (motion <= 0f)
            {
                return;
            }

            MotionState state = GetMotionState(pill);
            double now = EditorApplication.timeSinceStartup;
            if (now - state.AttachedAt < AttachGraceSeconds || now - state.LastPulseAt < PulseGapSeconds)
            {
                return;
            }

            state.LastPulseAt = now;
            state.StartPulse(motion, PillPulsePeak, PillPulseUpMs, PillPulseDownMs, false);
        }

        private static void OnPillAttached(AttachToPanelEvent evt)
        {
            if (evt.target is VisualElement element)
            {
                GetMotionState(element).AttachedAt = EditorApplication.timeSinceStartup;
            }
        }

        // The running animation of one element (reveal, chip pop or pill pulse); starting a new one first settles the old one.
        private sealed class MotionState
        {
            private const int KindNone = 0;
            private const int KindReveal = 1;
            private const int KindPulse = 2;

            private readonly VisualElement element;
            private Action stepAction;
            private IVisualElementScheduledItem pending;
            private int kind;
            private int step;
            private float motion;
            private bool chipColors;
            private float downMs;
            private StyleFloat savedOpacity;

            /// <summary>When the element last attached to a panel (negative infinity when unknown).</summary>
            public double AttachedAt = double.NegativeInfinity;

            /// <summary>When the last pulse started (negative infinity when none).</summary>
            public double LastPulseAt = double.NegativeInfinity;

            public MotionState(VisualElement element)
            {
                this.element = element;
            }

            public void StartReveal(float motionScale)
            {
                Settle();
                kind = KindReveal;
                step = 0;
                motion = motionScale;
                savedOpacity = element.style.opacity;
                element.style.opacity = 0f;
                element.style.translate = new StyleTranslate(new Translate(0f, -RevealPixels * motionScale));
                Schedule(FrameMs);
            }

            public void StartPulse(float motionScale, float peakScale, float upMs, float downMilliseconds, bool withChipColors)
            {
                Settle();
                kind = KindPulse;
                step = 0;
                motion = motionScale;
                chipColors = withChipColors;
                downMs = downMilliseconds * motionScale;
                float up = upMs * motionScale;
                ApplyPulseTransition(up, withChipColors ? ChipUpEase : EaseOutList);
                float amount = 1f + (peakScale - 1f) * motionScale;
                element.style.scale = new StyleScale(new Scale(new Vector2(amount, amount)));
                Schedule((long)up);
            }

            private void Schedule(long delayMs)
            {
                if (stepAction == null)
                {
                    stepAction = Step;
                }

                pending = element.schedule.Execute(stepAction).StartingIn(delayMs);
            }

            private void Step()
            {
                pending = null;
                if (kind == KindReveal)
                {
                    if (step == 0)
                    {
                        step = 1;
                        float ms = RevealMs * motion;
                        SetTransition(element, OpacityTranslateProperties, ms, EaseOutList);
                        element.style.opacity = savedOpacity.keyword == StyleKeyword.Undefined ? savedOpacity.value : 1f;
                        element.style.translate = ZeroTranslate;
                        Schedule((long)ms + SafetyMs);
                    }
                    else
                    {
                        Settle();
                    }
                }
                else if (kind == KindPulse)
                {
                    if (step == 0)
                    {
                        step = 1;
                        ApplyPulseTransition(downMs, chipColors ? ChipDownEase : EaseOutList);
                        element.style.scale = IdentityScale;
                        Schedule((long)downMs + SafetyMs);
                    }
                    else
                    {
                        Settle();
                    }
                }
            }

            private void ApplyPulseTransition(float milliseconds, List<EasingFunction> easing)
            {
                IStyle style = element.style;
                if (chipColors)
                {
                    float colorMs = ChipColorMs * motion;
                    style.transitionProperty = ChipProperties;
                    style.transitionDuration = new List<TimeValue>(3)
                    {
                        new TimeValue(milliseconds, TimeUnit.Millisecond),
                        new TimeValue(colorMs, TimeUnit.Millisecond),
                        new TimeValue(colorMs, TimeUnit.Millisecond),
                    };
                }
                else
                {
                    style.transitionProperty = ScaleProperties;
                    style.transitionDuration = new List<TimeValue>(1) { new TimeValue(milliseconds, TimeUnit.Millisecond) };
                }

                style.transitionTimingFunction = easing;
            }

            // Stops the timer and puts every inline style this animation touched back, so USS rules apply again.
            private void Settle()
            {
                if (pending != null)
                {
                    pending.Pause();
                    pending = null;
                }

                if (kind == KindReveal)
                {
                    element.style.opacity = savedOpacity;
                    element.style.translate = StyleKeyword.Null;
                    ClearTransition(element);
                }
                else if (kind == KindPulse)
                {
                    element.style.scale = StyleKeyword.Null;
                    ClearTransition(element);
                }

                kind = KindNone;
            }
        }

        // Switches the pages of a TabView with a fade and a slide. States: idle, leaving (old page fading out), arriving (new page placed at
        // its start pose, waiting one frame so the style system sees the start values) and settling (new page transitioning to rest).
        // One timer at a time is kept in pending; every new switch or click cancels it, so no page is left half-faded.
        private sealed class TabSwitcher
        {
            private const int Idle = 0;
            private const int Leaving = 1;
            private const int Arriving = 2;
            private const int Settling = 3;

            private readonly VisualElement view;
            private readonly VisualElement[] pages;
            private VisualElement bar;
            private Action stepAction;
            private IVisualElementScheduledItem pending;
            private int phase;
            private int current;
            private int leaving;
            private int target;
            private float motion;

            public TabSwitcher(VisualElement view, VisualElement[] pages)
            {
                this.view = view;
                this.pages = pages;
            }

            public void Initialize(VisualElement tabBar, int selected)
            {
                bar = tabBar;
                current = selected;
                target = selected;
                view.RegisterCallback<DetachFromPanelEvent>(OnDetached);
            }

            // Called after a user click already moved the tab bar's selection to index.
            public void Switch(int index)
            {
                if (index < 0 || index >= pages.Length)
                {
                    return;
                }

                float scale = VeneerMotionKit.Scale(view);
                if (scale <= 0f || view.panel == null)
                {
                    Snap(index);
                    return;
                }

                if (phase == Idle)
                {
                    // Rules (show-if on tabs) can move the shown page without telling us, so look at what is really shown.
                    current = FindShown();
                }

                if (current < 0 || current >= pages.Length || (current == index && phase != Leaving))
                {
                    Snap(index);
                    return;
                }

                motion = scale;
                target = index;
                if (phase == Leaving)
                {
                    if (index == current)
                    {
                        // Back to the page that is still fading out: bring it back from where it is.
                        CancelTimer();
                        BeginArrive(current, false);
                    }

                    // Otherwise the running leave lands on the new target by itself.
                    return;
                }

                CancelTimer();
                BeginLeave(current, index);
            }

            private int FindShown()
            {
                for (int i = 0; i < pages.Length; i++)
                {
                    if (pages[i].style.display.value != DisplayStyle.None)
                    {
                        return i;
                    }
                }

                return -1;
            }

            private void BeginLeave(int from, int to)
            {
                phase = Leaving;
                leaving = from;
                float direction = to > from ? 1f : -1f;
                float ms = TabLeaveMs * motion;
                VisualElement page = pages[from];
                SetTransition(page, OpacityTranslateProperties, ms, EaseInList);
                page.style.opacity = 0f;
                page.style.translate = new StyleTranslate(new Translate(-direction * TabSlidePixels * motion, 0f));
                Schedule((long)ms + SafetyMs);
            }

            private void BeginArrive(int index, bool fresh)
            {
                current = index;
                if (!fresh)
                {
                    StartArrival();
                    return;
                }

                phase = Arriving;
                float side = index >= leaving ? 1f : -1f;
                VisualElement page = pages[index];
                ClearTransition(page);
                page.style.opacity = 0f;
                page.style.translate = new StyleTranslate(new Translate(side * TabSlidePixels * motion, 0f));
                SetDisplay(page, true);
                Schedule(FrameMs);
            }

            private void StartArrival()
            {
                phase = Settling;
                float ms = TabArriveMs * motion;
                VisualElement page = pages[current];
                SetTransition(page, OpacityTranslateProperties, ms, EaseOutList);
                page.style.opacity = 1f;
                page.style.translate = ZeroTranslate;
                Schedule((long)ms + SafetyMs);
            }

            private void Schedule(long delayMs)
            {
                if (stepAction == null)
                {
                    stepAction = Step;
                }

                pending = view.schedule.Execute(stepAction).StartingIn(delayMs);
            }

            private void Step()
            {
                pending = null;
                switch (phase)
                {
                    case Leaving:
                        ResetPage(pages[leaving]);
                        SetDisplay(pages[leaving], false);
                        // The bar is the truth: a rule may have moved the selection while the old page was fading.
                        int selected = GetSegmentedSelected(bar);
                        if (selected < 0 || selected >= pages.Length)
                        {
                            Snap(selected);
                            return;
                        }

                        target = selected;
                        BeginArrive(selected, true);
                        break;
                    case Arriving:
                        StartArrival();
                        break;
                    case Settling:
                        ResetPage(pages[current]);
                        phase = Idle;
                        break;
                }
            }

            // Ends any animation at once: the page at index is shown with no leftover style, every other page is hidden (-1 hides all).
            private void Snap(int index)
            {
                CancelTimer();
                for (int i = 0; i < pages.Length; i++)
                {
                    ResetPage(pages[i]);
                    SetDisplay(pages[i], i == index);
                }

                current = index;
                target = index;
                phase = Idle;
            }

            private void CancelTimer()
            {
                if (pending != null)
                {
                    pending.Pause();
                    pending = null;
                }
            }

            private static void ResetPage(VisualElement page)
            {
                page.style.opacity = StyleKeyword.Null;
                page.style.translate = StyleKeyword.Null;
                ClearTransition(page);
            }

            private void OnDetached(DetachFromPanelEvent evt)
            {
                if (phase != Idle)
                {
                    Snap(GetSegmentedSelected(bar));
                }
            }
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
