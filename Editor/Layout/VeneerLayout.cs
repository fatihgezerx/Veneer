using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Veneer
{
    /// <summary>How a field is drawn inside a layout. <see cref="Auto"/> picks by the field's type (a <c>[Range]</c> float becomes a slider, and so on).</summary>
    public enum VeneerWidget
    {
        /// <summary>Chosen from the field's type and attributes.</summary>
        Auto,
        /// <summary>A number row with an optional unit.</summary>
        Number,
        /// <summary>A slider (uses <c>[Range]</c>, or 0..1 when there is none).</summary>
        Slider,
        /// <summary>A toggle chip.</summary>
        Toggle,
        /// <summary>Segmented buttons for an enum.</summary>
        Enum,
        /// <summary>An animated dropdown list for an enum (opens with a springy motion).</summary>
        Dropdown,
        /// <summary>An object slot.</summary>
        Object,
        /// <summary>A list editor.</summary>
        List,
        /// <summary>A Vector2 / Vector3 row.</summary>
        Vector,
        /// <summary>A tag dropdown for a string field.</summary>
        Tag,
        /// <summary>A two-handle slider for a Vector2 (x = minimum, y = maximum) inside <see cref="VeneerLayoutEntry.Min"/>..<see cref="VeneerLayoutEntry.Max"/> (needs <see cref="VeneerLayoutEntry.HasRange"/>).</summary>
        MinMax,
        /// <summary>A colour row.</summary>
        Color,
        /// <summary>An animation curve row.</summary>
        Curve
    }

    /// <summary>The test of a "show only when" rule on a field.</summary>
    public enum VeneerCondition
    {
        /// <summary>The other field equals the value.</summary>
        Equals,
        /// <summary>The other field differs from the value.</summary>
        NotEquals,
        /// <summary>The other field is greater than the value (numbers).</summary>
        Greater,
        /// <summary>The other field is less than the value (numbers).</summary>
        Less,
        /// <summary>The other field is a true bool.</summary>
        IsTrue,
        /// <summary>The other field is a false bool.</summary>
        IsFalse,
        /// <summary>The other field is an assigned object reference.</summary>
        NotNull,
        /// <summary>The other field is an empty object reference (nothing assigned).</summary>
        IsNull,
        /// <summary>The other field is empty: an unassigned object reference, an empty text, or a list with no elements.</summary>
        IsEmpty,
        /// <summary>The other field is not empty: an assigned object reference, a text, or a list with at least one element.</summary>
        NotEmpty
    }

    /// <summary>The kind of a note placed in a card; it sets the colour and icon of the message box (or, for <see cref="Text"/>, no box at all).</summary>
    public enum VeneerNoteKind
    {
        /// <summary>A neutral explanation.</summary>
        Info,
        /// <summary>A warning.</summary>
        Warning,
        /// <summary>An error message.</summary>
        Error,
        /// <summary>Plain muted text: no box, border or icon (like the explanation under a field in the Combat studio windows).</summary>
        Text,
        /// <summary>A small section heading inside a card: short muted capital text (the kit's section title), used to split a long card into parts.</summary>
        Heading
    }

    /// <summary>Where a banner that is narrower than the Inspector sits.</summary>
    public enum VeneerBannerAlign
    {
        /// <summary>At the left edge.</summary>
        Left,
        /// <summary>Centred.</summary>
        Center,
        /// <summary>At the right edge.</summary>
        Right
    }

    /// <summary>How the banner picture fills its box.</summary>
    public enum VeneerBannerFit
    {
        /// <summary>The picture fills the box and the part that does not fit is cropped (like a background); the aspect ratio is kept.</summary>
        Cover,
        /// <summary>The whole picture is shown inside the box, scaled to fit with its aspect ratio kept (empty space may remain), like the Image component's Preserve Aspect.</summary>
        Fit,
        /// <summary>The picture is stretched to the box; the aspect ratio is not kept.</summary>
        Stretch
    }

    /// <summary>
    /// One item of a card: a field with the user's presentation choices (the field itself is never changed), or a note (<see cref="IsNote"/>),
    /// a message box with free text that is not tied to any field. Both can carry a show-if rule.
    /// </summary>
    [Serializable]
    public sealed class VeneerLayoutEntry
    {
        /// <summary>True when this item is a note: <see cref="Path"/> is empty and <see cref="NoteText"/> and <see cref="NoteKind"/> are used instead of the field options.</summary>
        public bool IsNote;

        /// <summary>The text of a note (may have several lines).</summary>
        public string NoteText;

        /// <summary>The kind of a note.</summary>
        public VeneerNoteKind NoteKind;

        /// <summary>The <c>SerializedProperty.propertyPath</c> of the field: a top-level field (<c>radius</c>) or a field inside a nested serializable struct (<c>generalSettings.radius</c>); empty for a note or a custom item.</summary>
        public string Path;

        /// <summary>Label shown instead of the field's own name; empty keeps the default.</summary>
        public string Label;

        /// <summary>Tooltip shown instead of the field's own; empty keeps the default.</summary>
        public string Tooltip;

        /// <summary>Unit text written next to a number (for example <c>m</c> or <c>hp</c>); empty for none.</summary>
        public string Unit;

        /// <summary>How the field is drawn.</summary>
        public VeneerWidget Widget;

        /// <summary>When true the field is shown but cannot be edited.</summary>
        public bool ReadOnly;

        /// <summary>True when this item is a custom item the system registered (a button, a state pill, a group of computed hints...): <see cref="CustomId"/> names it, <see cref="Path"/> is empty.</summary>
        public bool IsCustom;

        /// <summary>The id of the registered custom item (see <c>VeneerCustomItems</c>).</summary>
        public string CustomId;

        /// <summary>Kit icon name shown on the toggle chip of a bool field (for example <c>eye</c>); empty for none. Used with <see cref="VeneerWidget.Toggle"/> and with Auto on a bool.</summary>
        public string Icon;

        /// <summary>True: this item is drawn in the same row as the previous item (toggle chips side by side), wrapping when the row is full. Only meaningful for toggles and buttons.</summary>
        public bool SameRow;

        /// <summary>True: <see cref="Min"/> and <see cref="Max"/> replace the field's own <c>[Range]</c>/<c>[Min]</c> for a slider, a number row or a min-max slider (a "soft" range chosen in the layout; the stored value is clamped to it when edited here).</summary>
        public bool HasRange;

        /// <summary>The lower end of the range (used with <see cref="HasRange"/>).</summary>
        public float Min;

        /// <summary>The upper end of the range (used with <see cref="HasRange"/>).</summary>
        public float Max = 1f;

        /// <summary>Text under the left end of a slider (for example "Short"); empty for none.</summary>
        public string SliderLeft;

        /// <summary>Text under the right end of a slider (for example "Long"); empty for none.</summary>
        public string SliderRight;

        /// <summary>Kit icon names for the buttons of an enum shown with <see cref="VeneerWidget.Enum"/>, comma separated, one per value in order; empty shows the value names.</summary>
        public string EnumIcons;

        /// <summary>Path of the field the show-if rule looks at; empty means the item is always shown (this also applies to notes).</summary>
        public string ShowIfPath;

        /// <summary>The test applied to the field at <see cref="ShowIfPath"/>.</summary>
        public VeneerCondition ShowIf;

        /// <summary>The value the test compares with (text; parsed by the type of the other field).</summary>
        public string ShowIfValue;
    }

    /// <summary>A card: a titled box holding fields.</summary>
    [Serializable]
    public sealed class VeneerLayoutBlock
    {
        /// <summary>Title of the card.</summary>
        public string Title = "Card";

        /// <summary>Name of a kit icon (see the README icon list); empty for none. <see cref="IconTexture"/> wins when set.</summary>
        public string Icon;

        /// <summary>The user's own icon picture (square is best); null uses <see cref="Icon"/>.</summary>
        public Texture2D IconTexture;

        /// <summary>False draws the card without its title bar.</summary>
        public bool ShowTitle = true;

        /// <summary>The fields of the card, in order.</summary>
        public List<VeneerLayoutEntry> Entries = new List<VeneerLayoutEntry>();
        /// <summary>Path of the field this card's show-if rule looks at; empty means the card is always shown.</summary>
        public string ShowIfPath;

        /// <summary>The test applied to the field at <see cref="ShowIfPath"/>.</summary>
        public VeneerCondition ShowIf;

        /// <summary>The value the test compares with (text; parsed by the type of the other field).</summary>
        public string ShowIfValue;

    }

    /// <summary>A tab: a titled page holding cards.</summary>
    [Serializable]
    public sealed class VeneerLayoutPage
    {
        /// <summary>Title on the tab button.</summary>
        public string Title = "Tab";

        /// <summary>Name of a kit icon; empty for none. <see cref="IconTexture"/> wins when set.</summary>
        public string Icon;

        /// <summary>The user's own icon picture; null uses <see cref="Icon"/>.</summary>
        public Texture2D IconTexture;

        /// <summary>The cards of the tab, in order.</summary>
        public List<VeneerLayoutBlock> Blocks = new List<VeneerLayoutBlock>();
        /// <summary>Path of the field this tab's show-if rule looks at; empty means the tab is always shown.</summary>
        public string ShowIfPath;

        /// <summary>The test applied to the field at <see cref="ShowIfPath"/>.</summary>
        public VeneerCondition ShowIf;

        /// <summary>The value the test compares with (text; parsed by the type of the other field).</summary>
        public string ShowIfValue;

    }

    /// <summary>A picture across the top of the Inspector.</summary>
    [Serializable]
    public sealed class VeneerLayoutBanner
    {
        /// <summary>Whether the banner is shown.</summary>
        public bool Enabled;

        /// <summary>True: the picture is the banner of the layout's theme (see <see cref="VeneerLayout.Theme"/> / <see cref="VeneerLayout.ThemeBrand"/>); <see cref="Image"/> is ignored. False: <see cref="Image"/> is used.</summary>
        public bool UseThemePicture;

        /// <summary>The picture.</summary>
        public Texture2D Image;

        /// <summary>Height of the banner in pixels.</summary>
        public float Height = 96f;

        /// <summary>True: the banner spans the Inspector's width. False: it is <see cref="Width"/> pixels wide. Ignored when <see cref="OverHeader"/> is on.</summary>
        public bool FullWidth = true;

        /// <summary>Width in pixels when <see cref="FullWidth"/> is off.</summary>
        public float Width = 256f;

        /// <summary>How the picture fills its box: cropped to cover it, fitted whole with its aspect ratio kept, or stretched.</summary>
        public VeneerBannerFit Fit = VeneerBannerFit.Cover;

        /// <summary>
        /// True: the banner is the background of the header and the header's icon, title and subtitle are drawn on top of it (like a game
        /// tool's title banner); it then always spans the Inspector's width. False: the banner is a separate strip above the header.
        /// </summary>
        public bool OverHeader = true;

        /// <summary>Darkening laid over the picture so the header text stays readable, 0 (none) to 1 (black). Only used when <see cref="OverHeader"/> is on.</summary>
        public float Dim = 0.35f;

        /// <summary>Size in pixels of the header icon when the header is drawn on the banner.</summary>
        public float IconSize = 64f;

        /// <summary>Where a banner that does not fill the width sits. Ignored when <see cref="OverHeader"/> is on.</summary>
        public VeneerBannerAlign Align;

        /// <summary>Colour multiplied with the picture (white keeps it as it is).</summary>
        public Color Tint = Color.white;
    }

    /// <summary>
    /// How the Inspector of one script (a MonoBehaviour or ScriptableObject) is laid out: a header, an optional banner, tabs, cards and the
    /// fields in them. A layout never touches the script or its data; deleting the asset gives the default Inspector back. It is made and
    /// edited in the Veneer Inspector Designer and rendered by the generic editors of the kit.
    /// </summary>
    [CreateAssetMenu(fileName = "New Inspector Layout", menuName = "Veneer/Inspector Layout")]
    public sealed class VeneerLayout : ScriptableObject
    {
        /// <summary>The script this layout styles (editor-only reference).</summary>
        public MonoScript Script;

        /// <summary>Full name of the script's class, kept so the layout is still found when the script reference is lost.</summary>
        public string TypeName;

        /// <summary>
        /// A custom theme (a <see cref="VeneerTheme"/> asset with <c>Custom</c> on) this script's Inspector uses: its colours, corner roundness, spacing, tab style
        /// and pictures. Null with an empty <see cref="ThemeBrand"/> means the kit's default look.
        /// </summary>
        public VeneerTheme Theme;

        /// <summary>The name of a system (<c>VeneerBrand.Name</c>) whose whole look this script's Inspector uses (its accent, theme, logo and banner); empty for none. Ignored when <see cref="Theme"/> is set.</summary>
        public string ThemeBrand;

        /// <summary>True: the header icon is the logo of the chosen theme or system (<see cref="HeaderIcon"/> and <see cref="HeaderIconTexture"/> are ignored).</summary>
        public bool UseThemeLogo;

        /// <summary>True when this layout is a copy made from the system's own default design (the Designer's "Customize"); "Reset to system default" deletes it.</summary>
        public bool FromSystemDefault;

        /// <summary>Whether the header (icon, title, subtitle) is drawn.</summary>
        public bool ShowHeader = true;

        /// <summary>Header title; empty uses the type's name split into words.</summary>
        public string Title;

        /// <summary>Muted line under the title.</summary>
        public string Subtitle;

        /// <summary>Name of a kit icon for the header; empty shows none. <see cref="HeaderIconTexture"/> wins when set.</summary>
        public string HeaderIcon;

        /// <summary>The user's own header icon picture; null uses <see cref="HeaderIcon"/>.</summary>
        public Texture2D HeaderIconTexture;

        /// <summary>Icon of the script itself (Project window, Add Component menu, Inspector title). Applied to the MonoScript by <c>VeneerScriptIcon</c>.</summary>
        public Texture2D ScriptIcon;

        /// <summary>The picture across the top.</summary>
        public VeneerLayoutBanner Banner = new VeneerLayoutBanner();

        /// <summary>When true <see cref="Accent"/> replaces the kit's accent colour in this Inspector.</summary>
        public bool UseAccent;

        /// <summary>The accent colour of this Inspector (tab underline, chips that are on, icons, primary buttons).</summary>
        public Color Accent = new Color32(0x3F, 0xB6, 0xFF, 255);

        /// <summary>The tabs. With none the layout is empty and the default Inspector is shown; with one, no tab bar is drawn.</summary>
        public List<VeneerLayoutPage> Pages = new List<VeneerLayoutPage>();

        /// <summary>Property paths the user hid on purpose (they are not shown, not even in the Other section).</summary>
        public List<string> HiddenPaths = new List<string>();

        /// <summary>True: the automatic "Other" card is put at the end of the LAST tab (when there are tabs) instead of under the whole tab view.</summary>
        public bool OtherInLastPage;

        /// <summary>The title of the automatic "Other" card; empty uses "Other".</summary>
        public string OtherTitle;

        /// <summary>Show fields that are in no card in an automatic "Other" section at the end, so a field added to the script later is never lost.</summary>
        public bool ShowOthers = true;

        /// <summary>True when the layout has at least one tab, so it replaces the default Inspector.</summary>
        public bool HasContent => Pages != null && Pages.Count > 0;

        /// <summary>Finds the entry that holds <paramref name="path"/>, or null.</summary>
        /// <param name="path">A top-level property path.</param>
        /// <param name="page">The index of the tab holding it, or -1.</param>
        /// <param name="block">The index of the card holding it, or -1.</param>
        public VeneerLayoutEntry FindEntry(string path, out int page, out int block)
        {
            page = -1;
            block = -1;
            if (Pages == null || string.IsNullOrEmpty(path))
            {
                return null;
            }

            for (int p = 0; p < Pages.Count; p++)
            {
                List<VeneerLayoutBlock> blocks = Pages[p].Blocks;
                for (int b = 0; b < blocks.Count; b++)
                {
                    List<VeneerLayoutEntry> entries = blocks[b].Entries;
                    for (int e = 0; e < entries.Count; e++)
                    {
                        if (entries[e].Path == path)
                        {
                            page = p;
                            block = b;
                            return entries[e];
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>True when <paramref name="path"/> sits in a card.</summary>
        public bool IsPlaced(string path)
        {
            return FindEntry(path, out _, out _) != null;
        }

        /// <summary>True when the user hid <paramref name="path"/> on purpose.</summary>
        public bool IsHidden(string path)
        {
            return HiddenPaths != null && HiddenPaths.Contains(path);
        }

        /// <summary>The type the layout styles, or null when the script is missing or has no class.</summary>
        public Type ResolveType()
        {
            Type type = Script != null ? Script.GetClass() : null;
            if (type != null)
            {
                return type;
            }

            return string.IsNullOrEmpty(TypeName) ? null : ResolveByName(TypeName);
        }

        private static Type ResolveByName(string fullName)
        {
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        /// <summary>Points the layout at <paramref name="script"/> and remembers its class name.</summary>
        public void Bind(MonoScript script)
        {
            Script = script;
            Type type = script != null ? script.GetClass() : null;
            TypeName = type != null ? type.FullName : null;
        }
    }
}
