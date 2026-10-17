using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>One preset card of a <see cref="VeneerSettingsPage"/>: a name, a one-line description, an icon and a tooltip.</summary>
    public readonly struct VeneerPreset
    {
        /// <summary>Creates a preset description.</summary>
        public VeneerPreset(string name, string description, string icon, string tooltip = null)
        {
            Name = name;
            Description = description;
            Icon = icon;
            Tooltip = tooltip;
        }

        /// <summary>Card title.</summary>
        public string Name { get; }

        /// <summary>One line under the title.</summary>
        public string Description { get; }

        /// <summary>Icon name of the card.</summary>
        public string Icon { get; }

        /// <summary>Tooltip of the card; may be null.</summary>
        public string Tooltip { get; }
    }

    /// <summary>
    /// A helper for the Project Settings / Preferences page of a system: a branded banner, a row of preset cards (one of them highlighted
    /// while the current values match it), tabs, a live card that appears only in Play Mode, and the "create the settings asset" hint.
    /// It owns the root and its schedulers: <see cref="Dispose"/> stops them, which <see cref="Provider"/> does when the page closes.
    /// The page is a plain helper, not a <see cref="SettingsProvider"/>, so a system keeps its own provider class if it wants one;
    /// <see cref="Provider"/> makes a ready one for the common case.
    /// <code>
    /// [SettingsProvider]
    /// static SettingsProvider Create() =&gt; VeneerSettingsPage.Provider("Project/My System", "My System", MyBrand.Value, "My System", "World settings", Build);
    /// static void Build(VeneerSettingsPage page) { page.PresetRow("Presets", "preset", presets, Apply); var tabs = page.Tabs(...); ... }
    /// </code>
    /// </summary>
    public sealed class VeneerSettingsPage : IDisposable
    {
        private const float PageMargin = 10f;

        private readonly VeneerBrand brand;
        private readonly string title;
        private readonly string subtitle;
        private readonly List<VisualElement> liveCards = new List<VisualElement>(2);
        private readonly List<IVisualElementScheduledItem> schedules = new List<IVisualElementScheduledItem>(2);
        private VisualElement[] presetCards;
        private VisualElement[] presetChecks;

        /// <summary>Creates a page.</summary>
        /// <param name="brand">The brand (accent, banner, icons); may be null.</param>
        /// <param name="title">Banner title.</param>
        /// <param name="subtitle">Banner subtitle; may be null.</param>
        public VeneerSettingsPage(VeneerBrand brand, string title, string subtitle = null)
        {
            this.brand = brand;
            this.title = title;
            this.subtitle = subtitle;
        }

        /// <summary>The page root (kit sheet, brand, margins); null before <see cref="Mount"/>.</summary>
        public VisualElement Root { get; private set; }

        /// <summary>The container below the banner that everything is added to; null before <see cref="Mount"/>.</summary>
        public VisualElement Content { get; private set; }

        /// <summary>The brand of the page.</summary>
        public VeneerBrand Brand => brand;

        /// <summary>Clears <paramref name="rootElement"/> (the element a <see cref="SettingsProvider"/> hands to its activate handler) and builds the root and the banner in it.</summary>
        public void Mount(VisualElement rootElement)
        {
            Dispose();
            rootElement.Clear();
            Root = VeneerStyle.Root(brand);
            Root.style.marginLeft = PageMargin;
            Root.style.marginRight = PageMargin;
            Root.style.marginTop = 4f;
            Root.style.marginBottom = 16f;
            rootElement.Add(Root);
            Root.Add(VeneerStyle.Banner(brand, title, subtitle));
            Content = new VisualElement();
            Root.Add(Content);
        }

        /// <summary>
        /// Adds a card with one clickable card per preset. <paramref name="apply"/> gets the index of the clicked preset. Call
        /// <see cref="RefreshPresets"/> after any data change to highlight the preset the values match.
        /// </summary>
        public VisualElement PresetRow(string cardTitle, string cardIcon, IReadOnlyList<VeneerPreset> presets, Action<int> apply)
        {
            VisualElement card = VeneerStyle.Card(cardTitle, cardIcon, brand);
            VisualElement row = VeneerStyle.Row(true);
            presetCards = new VisualElement[presets.Count];
            presetChecks = new VisualElement[presets.Count];
            for (int i = 0; i < presets.Count; i++)
            {
                int index = i;
                VeneerPreset preset = presets[i];
                var button = new Button(() => apply(index));
                button.AddToClassList("vn-preset");
                button.tooltip = preset.Tooltip;

                VisualElement header = VeneerStyle.Row();
                VisualElement icon = VeneerStyle.IconElement(preset.Icon, "vn-icon--accent vn-icon--lg", brand);
                header.Add(icon);
                var name = new Label(preset.Name);
                name.AddToClassList("vn-preset__title");
                name.pickingMode = PickingMode.Ignore;
                header.Add(name);
                header.Add(VeneerStyle.Spacer());
                VisualElement check = VeneerStyle.IconElement("check", "vn-icon--accent", brand);
                check.tooltip = "The current values match this preset.";
                VeneerStyle.SetDisplay(check, false);
                header.Add(check);
                button.Add(header);

                var description = new Label(preset.Description);
                description.AddToClassList("vn-preset__text");
                description.pickingMode = PickingMode.Ignore;
                button.Add(description);

                presetCards[i] = button;
                presetChecks[i] = check;
                row.Add(button);
            }

            card.Add(row);
            Content.Add(card);
            return card;
        }

        /// <summary>Highlights the preset cards for which <paramref name="matches"/> returns true (usually at most one).</summary>
        public void RefreshPresets(Func<int, bool> matches)
        {
            if (presetCards == null)
            {
                return;
            }

            for (int i = 0; i < presetCards.Length; i++)
            {
                bool on = matches(i);
                presetCards[i].EnableInClassList("vn-preset--on", on);
                VeneerStyle.SetDisplay(presetChecks[i], on);
            }
        }

        /// <summary>Adds a tab bar and its pages to <see cref="Content"/> (the selected tab is remembered under <paramref name="sessionKey"/>) and returns the pages.</summary>
        public VisualElement[] Tabs(string sessionKey, params (string label, string icon)[] tabs)
        {
            var labels = new string[tabs.Length];
            var icons = new string[tabs.Length];
            for (int i = 0; i < tabs.Length; i++)
            {
                labels[i] = tabs[i].label;
                icons[i] = tabs[i].icon;
            }

            Content.Add(VeneerStyle.TabView(sessionKey, labels, icons, null, brand, out VisualElement[] pages, out _));
            return pages;
        }

        /// <summary>
        /// Adds a card that is shown only in Play Mode (checked by the page's scheduler, see <see cref="Every"/>); fill it with KPI cards and
        /// hints. Returns the card.
        /// </summary>
        public VisualElement LiveCard(string cardTitle = "Live", string cardIcon = "eye")
        {
            VisualElement card = VeneerStyle.Card(cardTitle, cardIcon, brand);
            VeneerStyle.SetDisplay(card, Application.isPlaying);
            liveCards.Add(card);
            Content.Add(card);
            return card;
        }

        /// <summary>
        /// Adds a card with a message and a primary button for the case that the settings asset does not exist yet; <paramref name="create"/>
        /// should create it and rebuild the page. Returns the card.
        /// </summary>
        public VisualElement CreateAssetHint(string message, string buttonText, string buttonTooltip, Action create)
        {
            VisualElement card = VeneerStyle.Card("Settings asset", "settings", brand);
            card.Add(VeneerStyle.Hint(message, "info", brand));
            card.Add(VeneerStyle.Button(buttonText, "plus", buttonTooltip, create, true, brand));
            Content.Add(card);
            return card;
        }

        /// <summary>
        /// Runs <paramref name="action"/> every <paramref name="milliseconds"/> while the page is open (also shows and hides the live cards at
        /// that pace). Stopped by <see cref="Dispose"/>; use 250 ms or slower.
        /// </summary>
        public void Every(Action action, long milliseconds = 250)
        {
            IVisualElementScheduledItem item = Root.schedule.Execute(() =>
            {
                SyncLiveCards();
                action();
            }).Every(milliseconds);
            schedules.Add(item);
        }

        /// <summary>Stops the schedulers and forgets the page's elements. Safe to call twice.</summary>
        public void Dispose()
        {
            for (int i = 0; i < schedules.Count; i++)
            {
                schedules[i].Pause();
            }

            schedules.Clear();
            liveCards.Clear();
            presetCards = null;
            presetChecks = null;
            Root = null;
            Content = null;
        }

        /// <summary>
        /// Makes a <see cref="SettingsProvider"/> whose page is built by <paramref name="build"/> every time it opens and disposed when it
        /// closes.
        /// </summary>
        /// <param name="path">Settings path, for example <c>Project/My System</c>.</param>
        /// <param name="label">The label in the settings tree.</param>
        /// <param name="brand">The brand of the page; may be null.</param>
        /// <param name="bannerTitle">Banner title.</param>
        /// <param name="bannerSubtitle">Banner subtitle; may be null.</param>
        /// <param name="build">Fills the mounted page.</param>
        /// <param name="scope">Project or User settings.</param>
        /// <param name="keywords">Search keywords; may be null.</param>
        public static SettingsProvider Provider(
            string path,
            string label,
            VeneerBrand brand,
            string bannerTitle,
            string bannerSubtitle,
            Action<VeneerSettingsPage> build,
            SettingsScope scope = SettingsScope.Project,
            IEnumerable<string> keywords = null)
        {
            VeneerSettingsPage page = null;
            var provider = new SettingsProvider(path, scope)
            {
                label = label,
                keywords = keywords,
                activateHandler = (search, rootElement) =>
                {
                    if (page != null)
                    {
                        page.Dispose();
                    }

                    page = new VeneerSettingsPage(brand, bannerTitle, bannerSubtitle);
                    page.Mount(rootElement);
                    build(page);
                },
                deactivateHandler = () =>
                {
                    if (page != null)
                    {
                        page.Dispose();
                        page = null;
                    }
                },
            };
            return provider;
        }

        private void SyncLiveCards()
        {
            bool playing = Application.isPlaying;
            for (int i = 0; i < liveCards.Count; i++)
            {
                VeneerStyle.SetDisplay(liveCards[i], playing);
            }
        }
    }
}
