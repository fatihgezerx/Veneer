using UnityEditor;
using UnityEngine;

namespace Veneer
{
    /// <summary>
    /// The look a setting has when a theme does NOT override it: what the Theme Editor's properties pane shows (greyed) next to an
    /// unticked Override box, and what a ticked box starts from. For a system's theme the parent is the global theme (where it
    /// overrides), then the system's own brand; for a custom theme the parent is the global theme too, then the kit's own look (a custom
    /// theme is tied to no system); for the global theme there is no parent, only the kit itself.
    /// </summary>
    /// <remarks>
    /// The colour numbers mirror <c>Veneer.uss</c>: <c>.vn-root</c> (dark skin) and <c>.vn-root.vn-root--light</c> (light skin), variables
    /// <c>--vn-card</c>, <c>--vn-card-border</c>, <c>--vn-text</c> and <c>--vn-text-muted</c>. Keep them in step with that file. The
    /// accent comes from <see cref="VeneerBrand.Accent"/> (exact), not from the sheet.
    /// </remarks>
    internal static class VeneerThemeInherited
    {
        // Veneer.uss .vn-root (dark skin).
        private static readonly Color DarkCard = new Color(1f, 1f, 1f, 0.045f);
        private static readonly Color DarkCardBorder = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color DarkText = new Color32(0xD2, 0xD2, 0xD2, 255);
        private static readonly Color DarkTextMuted = new Color32(0x9B, 0x9B, 0x9B, 255);

        // Veneer.uss .vn-root.vn-root--light (light skin).
        private static readonly Color LightCard = new Color(1f, 1f, 1f, 0.45f);
        private static readonly Color LightCardBorder = new Color(0f, 0f, 0f, 0.20f);
        private static readonly Color LightText = new Color32(0x1A, 0x1A, 0x1A, 255);
        private static readonly Color LightTextMuted = new Color32(0x55, 0x55, 0x55, 255);

        /// <summary>The parent a theme inherits from: the global theme for a system's theme and for a custom theme, nothing for the global theme itself.</summary>
        /// <param name="isGlobalSelected">True when the selected entry is the global theme.</param>
        internal static VeneerTheme Parent(bool isGlobalSelected)
        {
            return isGlobalSelected ? null : VeneerThemeRegistry.Global;
        }

        /// <summary>The accent: the parent theme's if it overrides, else the brand's own (the kit's default accent when <paramref name="brand"/> is null).</summary>
        /// <param name="brand">The selected system; null for the global theme and for a custom theme.</param>
        /// <param name="parent">The global theme when a system or a custom theme is selected; null for the global theme.</param>
        internal static Color Accent(VeneerBrand brand, VeneerTheme parent)
        {
            if (parent != null && parent.OverrideAccent)
            {
                return parent.Accent;
            }

            return brand != null ? brand.Accent : VeneerBrand.DefaultAccent;
        }

        /// <summary>The card colour: the parent's if it overrides, else the kit's for the current editor skin.</summary>
        internal static Color CardColor(VeneerTheme parent)
        {
            if (parent != null && parent.OverrideCardColor)
            {
                return parent.CardColor;
            }

            return EditorGUIUtility.isProSkin ? DarkCard : LightCard;
        }

        /// <summary>The card border colour: the parent's if it overrides, else the kit's for the current editor skin.</summary>
        internal static Color CardBorder(VeneerTheme parent)
        {
            if (parent != null && parent.OverrideCardBorder)
            {
                return parent.CardBorder;
            }

            return EditorGUIUtility.isProSkin ? DarkCardBorder : LightCardBorder;
        }

        /// <summary>The main text colour: the parent's if it overrides, else the kit's for the current editor skin.</summary>
        internal static Color Text(VeneerTheme parent)
        {
            if (parent != null && parent.OverrideText)
            {
                return parent.Text;
            }

            return EditorGUIUtility.isProSkin ? DarkText : LightText;
        }

        /// <summary>The muted text colour: the parent's if it overrides, else the kit's for the current editor skin.</summary>
        internal static Color TextMuted(VeneerTheme parent)
        {
            if (parent != null && parent.OverrideTextMuted)
            {
                return parent.TextMuted;
            }

            return EditorGUIUtility.isProSkin ? DarkTextMuted : LightTextMuted;
        }

        /// <summary>The corner roundness scale: the parent's if it overrides, else 1 (the kit's own look).</summary>
        internal static float RadiusScale(VeneerTheme parent)
        {
            return parent != null && parent.OverrideRadius ? parent.RadiusScale : 1f;
        }

        /// <summary>The spacing scale: the parent's if it overrides, else 1 (the kit's own look).</summary>
        internal static float DensityScale(VeneerTheme parent)
        {
            return parent != null && parent.OverrideDensity ? parent.DensityScale : 1f;
        }

        /// <summary>The motion setting: the parent's if it overrides, else Normal (the kit's own motion).</summary>
        internal static VeneerMotion Motion(VeneerTheme parent)
        {
            return parent != null && parent.OverrideMotion ? parent.Motion : VeneerMotion.Normal;
        }

        /// <summary>The tab style: the parent's if it overrides, else Underline.</summary>
        internal static VeneerTabStyle TabStyle(VeneerTheme parent)
        {
            return parent != null && parent.OverrideTabStyle ? parent.TabStyle : VeneerTabStyle.Underline;
        }
    }
}
