using System;
using UnityEngine;

namespace Veneer
{
    /// <summary>
    /// A fluent builder for the default <see cref="VeneerLayout"/> of a system's editor, written in code (diff-friendly, no hand-made
    /// asset). The result is an in-memory layout (<see cref="HideFlags.HideAndDontSave"/>) that is never saved. Every method returns the
    /// builder. An item added before any page or card is put in a page "General" and a card "Fields".
    /// <code>
    /// new VeneerLayoutBuilder(typeof(MyData), "My Data", "Settings")
    ///     .Page("Main", "list").Card("Shape", "target")
    ///     .Slider("radius", "Reach", "Short", "Long", "m")
    ///     .Field("mask", "Layers").ShowIf("useMask", VeneerCondition.IsTrue)
    ///     .Build();
    /// </code>
    /// </summary>
    public sealed class VeneerLayoutBuilder
    {
        private const string DefaultPageTitle = "General";
        private const string DefaultCardTitle = "Fields";

        private readonly VeneerLayout layout;
        private VeneerLayoutPage page;
        private VeneerLayoutBlock block;
        private VeneerLayoutEntry last;

        /// <summary>Starts a layout for <paramref name="targetType"/>.</summary>
        /// <param name="targetType">The edited type (<see cref="VeneerLayout.TypeName"/> is set from it).</param>
        /// <param name="title">The header title; empty uses the type's name split into words.</param>
        /// <param name="subtitle">The muted line under the title; may be null.</param>
        /// <param name="headerIcon">A kit icon name for the header; may be null.</param>
        public VeneerLayoutBuilder(Type targetType, string title, string subtitle = null, string headerIcon = null)
        {
            if (targetType == null)
            {
                throw new ArgumentNullException(nameof(targetType));
            }

            layout = ScriptableObject.CreateInstance<VeneerLayout>();
            layout.hideFlags = HideFlags.HideAndDontSave;
            layout.name = targetType.Name + " (system default)";
            layout.TypeName = targetType.FullName;
            layout.Title = title;
            layout.Subtitle = subtitle;
            layout.HeaderIcon = headerIcon;
            layout.ShowHeader = true;
            layout.ShowOthers = true;
        }

        /// <summary>Starts a new tab; the following cards go into it.</summary>
        public VeneerLayoutBuilder Page(string title, string icon = null)
        {
            page = new VeneerLayoutPage { Title = title, Icon = icon };
            layout.Pages.Add(page);
            block = null;
            last = null;
            return this;
        }

        /// <summary>Starts a new card in the current tab (a tab is made when there is none); the following items go into it.</summary>
        public VeneerLayoutBuilder Card(string title, string icon = null, bool showTitle = true)
        {
            EnsurePage();
            block = new VeneerLayoutBlock { Title = title, Icon = icon, ShowTitle = showTitle };
            page.Blocks.Add(block);
            last = null;
            return this;
        }

        /// <summary>Adds a field. Any property path works, nested ones included (<c>generalSettings.radius</c>).</summary>
        public VeneerLayoutBuilder Field(string path, string label = null, string tooltip = null, string unit = null, VeneerWidget widget = VeneerWidget.Auto)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("A field needs a property path.", nameof(path));
            }

            VeneerLayoutEntry entry = NewEntry();
            entry.Path = path;
            entry.Label = label;
            entry.Tooltip = tooltip;
            entry.Unit = unit;
            entry.Widget = widget;
            return this;
        }

        /// <summary>Adds a slider with captions under its two ends.</summary>
        public VeneerLayoutBuilder Slider(string path, string label, string left, string right, string unit = null)
        {
            Field(path, label, null, unit, VeneerWidget.Slider);
            last.SliderLeft = left;
            last.SliderRight = right;
            return this;
        }

        /// <summary>Adds an enum as segmented buttons with kit icons (<paramref name="icons"/>: names separated by commas, one per value in order).</summary>
        public VeneerLayoutBuilder Enum(string path, string label, string icons)
        {
            Field(path, label, null, null, VeneerWidget.Enum);
            last.EnumIcons = icons;
            return this;
        }

        /// <summary>Adds a note (free text in a message box, or plain muted text for <see cref="VeneerNoteKind.Text"/>).</summary>
        public VeneerLayoutBuilder Note(string text, VeneerNoteKind kind = VeneerNoteKind.Info)
        {
            VeneerLayoutEntry entry = NewEntry();
            entry.IsNote = true;
            entry.NoteText = text;
            entry.NoteKind = kind;
            return this;
        }

        /// <summary>Sets the range of the last added field: <see cref="VeneerLayoutEntry.HasRange"/> on with <paramref name="min"/>..<paramref name="max"/>.</summary>
        public VeneerLayoutBuilder Range(float min, float max)
        {
            VeneerLayoutEntry entry = RequireLast(nameof(Range));
            entry.HasRange = true;
            entry.Min = min;
            entry.Max = max;
            return this;
        }

        /// <summary>Adds a Vector2 as a two-handle slider (x = minimum, y = maximum) inside <paramref name="min"/>..<paramref name="max"/>.</summary>
        public VeneerLayoutBuilder MinMax(string path, string label, float min, float max, string unit = null)
        {
            Field(path, label, null, unit, VeneerWidget.MinMax);
            return Range(min, max);
        }

        /// <summary>Shows the current tab (the last <see cref="Page"/> call) only while the field at <paramref name="path"/> passes <paramref name="condition"/>.</summary>
        public VeneerLayoutBuilder PageShowIf(string path, VeneerCondition condition, string value = null)
        {
            if (page == null)
            {
                throw new InvalidOperationException("PageShowIf() needs a Page() before it.");
            }

            page.ShowIfPath = path;
            page.ShowIf = condition;
            page.ShowIfValue = value;
            return this;
        }

        /// <summary>Shows the current card (the last <see cref="Card"/> call) only while the field at <paramref name="path"/> passes <paramref name="condition"/>.</summary>
        public VeneerLayoutBuilder CardShowIf(string path, VeneerCondition condition, string value = null)
        {
            if (block == null)
            {
                throw new InvalidOperationException("CardShowIf() needs a Card() before it.");
            }

            block.ShowIfPath = path;
            block.ShowIf = condition;
            block.ShowIfValue = value;
            return this;
        }

        /// <summary>Sets the automatic Other card: at the end of the last tab or under the tab view, its title (null keeps "Other"), and whether it is shown.</summary>
        public VeneerLayoutBuilder OtherCard(bool inLastPage, string title = null, bool show = true)
        {
            layout.OtherInLastPage = inLastPage;
            layout.OtherTitle = title;
            layout.ShowOthers = show;
            return this;
        }

        /// <summary>Adds a small section heading inside the card (a note of kind <see cref="VeneerNoteKind.Heading"/>).</summary>
        public VeneerLayoutBuilder Heading(string text)
        {
            return Note(text, VeneerNoteKind.Heading);
        }

        /// <summary>Gives the last added field the kit icon <paramref name="kitIconName"/> on its toggle chip (a bool field; a bool with an icon is drawn as a chip).</summary>
        public VeneerLayoutBuilder Icon(string kitIconName)
        {
            RequireLast(nameof(Icon)).Icon = kitIconName;
            return this;
        }

        /// <summary>Draws the last added item in the same wrapping row as the item before it (toggle chips side by side). Only toggles and custom items join a row.</summary>
        public VeneerLayoutBuilder SameRow()
        {
            RequireLast(nameof(SameRow)).SameRow = true;
            return this;
        }

        /// <summary>Adds a custom item registered with <see cref="VeneerCustomItems.Register"/>.</summary>
        public VeneerLayoutBuilder Custom(string customId)
        {
            if (string.IsNullOrEmpty(customId))
            {
                throw new ArgumentException("A custom item needs an id.", nameof(customId));
            }

            VeneerLayoutEntry entry = NewEntry();
            entry.IsCustom = true;
            entry.CustomId = customId;
            return this;
        }

        /// <summary>Shows the last added item (field, note or custom item) only while the field at <paramref name="path"/> passes <paramref name="condition"/>.</summary>
        public VeneerLayoutBuilder ShowIf(string path, VeneerCondition condition, string value = null)
        {
            VeneerLayoutEntry entry = RequireLast(nameof(ShowIf));
            entry.ShowIfPath = path;
            entry.ShowIf = condition;
            entry.ShowIfValue = value;
            return this;
        }

        /// <summary>Makes the last added field read-only.</summary>
        public VeneerLayoutBuilder ReadOnly()
        {
            VeneerLayoutEntry entry = RequireLast(nameof(ReadOnly));
            if (entry.IsNote || entry.IsCustom)
            {
                throw new InvalidOperationException("ReadOnly() applies to a field; the last item is a note or a custom item.");
            }

            entry.ReadOnly = true;
            return this;
        }

        /// <summary>Returns the finished layout (an in-memory object that is never saved). The builder should not be used afterwards.</summary>
        public VeneerLayout Build()
        {
            return layout;
        }

        private VeneerLayoutEntry NewEntry()
        {
            EnsurePage();
            if (block == null)
            {
                block = new VeneerLayoutBlock { Title = DefaultCardTitle };
                page.Blocks.Add(block);
            }

            var entry = new VeneerLayoutEntry();
            block.Entries.Add(entry);
            last = entry;
            return entry;
        }

        private void EnsurePage()
        {
            if (page == null)
            {
                page = new VeneerLayoutPage { Title = DefaultPageTitle };
                layout.Pages.Add(page);
            }
        }

        private VeneerLayoutEntry RequireLast(string method)
        {
            if (last == null)
            {
                throw new InvalidOperationException(method + "() needs an item added before it.");
            }

            return last;
        }
    }
}
