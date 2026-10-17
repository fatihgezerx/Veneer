using UnityEngine;

namespace Veneer
{
    /// <summary>How much the interface moves: hover and press effects, tab slides, dropdown openings.</summary>
    public enum VeneerMotion
    {
        /// <summary>No animation at all.</summary>
        Off,
        /// <summary>Short, small movements.</summary>
        Subtle,
        /// <summary>The kit's own motion.</summary>
        Normal,
        /// <summary>Longer, bigger, springier movements.</summary>
        Lively
    }

    /// <summary>How the tab bar looks.</summary>
    public enum VeneerTabStyle
    {
        /// <summary>Text tabs with a line under the selected one (the kit's own look).</summary>
        Underline,
        /// <summary>Rounded tabs; the selected one is a filled pill in the accent colour.</summary>
        Pill,
        /// <summary>Tabs joined into one bordered box; the selected one is filled.</summary>
        Boxed
    }

    /// <summary>
    /// The look of one system's editors (or of all of them, as the global base): colours, corners, spacing, tab style, and the logo and
    /// banner pictures. Every setting has its own "override" switch: a setting that is not overridden falls back to the global theme and
    /// then to the system's own brand, so a theme only changes what the user touched. Deleting the asset gives the default look back.
    /// Made and edited in the Veneer Theme Editor; applied by the kit to every Inspector, window and settings page of the system (a custom theme is applied to the scripts whose layout chooses it).
    /// </summary>
    [CreateAssetMenu(fileName = "New Veneer Theme", menuName = "Veneer/Theme")]
    public sealed class VeneerTheme : ScriptableObject
    {
        /// <summary>
        /// Name of the brand (system) this theme restyles, exactly as <c>VeneerBrand.Name</c>. Empty for the global theme that applies to every system,
        /// and for a custom theme (<see cref="Custom"/>).
        /// </summary>
        public string BrandName;

        /// <summary>
        /// True for a theme the user made as a reusable look of its own (not tied to a system): it is chosen by name in the Inspector Designer for a script.
        /// A custom theme is a full snapshot: it normally has every setting overridden.
        /// </summary>
        public bool Custom;

        /// <summary>The name of a custom theme, shown in lists (for example in the Inspector Designer's theme choice); ignored for system and global themes.</summary>
        public string ThemeName;

        /// <summary>Replace the accent colour (selected tab, chips that are on, icons, primary buttons).</summary>
        public bool OverrideAccent;

        /// <summary>The accent colour.</summary>
        public Color Accent = new Color32(0x3F, 0xB6, 0xFF, 255);

        /// <summary>Replace the card background colour.</summary>
        public bool OverrideCardColor;

        /// <summary>The card background colour (alpha matters: the kit's own is a faint white over the window).</summary>
        public Color CardColor = new Color(1f, 1f, 1f, 0.045f);

        /// <summary>Replace the card border colour.</summary>
        public bool OverrideCardBorder;

        /// <summary>The card border colour.</summary>
        public Color CardBorder = new Color(1f, 1f, 1f, 0.10f);

        /// <summary>Replace the main text colour.</summary>
        public bool OverrideText;

        /// <summary>The main text colour.</summary>
        public Color Text = new Color32(0xD2, 0xD2, 0xD2, 255);

        /// <summary>Replace the muted (secondary) text colour.</summary>
        public bool OverrideTextMuted;

        /// <summary>The muted text colour.</summary>
        public Color TextMuted = new Color32(0x9B, 0x9B, 0x9B, 255);

        /// <summary>Replace the corner roundness.</summary>
        public bool OverrideRadius;

        /// <summary>How round corners are, as a multiple of the kit's own radii: 0 = square, 1 = the kit's look, 2 = twice as round.</summary>
        [Range(0f, 2f)] public float RadiusScale = 1f;

        /// <summary>Replace the spacing.</summary>
        public bool OverrideDensity;

        /// <summary>How much room there is around and between controls, as a multiple of the kit's own spacing: 0.6 = tight, 1 = the kit's look, 1.5 = airy.</summary>
        [Range(0.6f, 1.5f)] public float DensityScale = 1f;

        /// <summary>Replace the motion setting.</summary>
        public bool OverrideMotion;

        /// <summary>How much the interface moves.</summary>
        public VeneerMotion Motion = VeneerMotion.Normal;

        /// <summary>Replace the tab style.</summary>
        public bool OverrideTabStyle;

        /// <summary>The tab style.</summary>
        public VeneerTabStyle TabStyle = VeneerTabStyle.Underline;

        /// <summary>The logo picture used instead of the brand's own (headers, window title bars); null keeps the brand's.</summary>
        public Texture2D Logo;

        /// <summary>The banner picture used instead of the brand's own (the wide picture of studio windows and headers); null keeps the brand's.</summary>
        public Texture2D Banner;

        /// <summary>True for the theme that applies to every system (neither tied to a system nor custom).</summary>
        public bool IsGlobal => !Custom && string.IsNullOrEmpty(BrandName);
    }
}
