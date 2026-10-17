using UnityEngine;

namespace Veneer
{
    /// <summary>
    /// The merged result of the global theme and one more theme (a system's, or a custom one): for every setting the value of that theme
    /// if it overrides it, else the global theme's if it overrides it, else "not set" (the brand's own look stays). Made once per brand
    /// name by <see cref="VeneerThemeRegistry.Effective"/> (or per custom theme by <see cref="VeneerThemeRegistry.EffectiveFor"/>) and
    /// reused until the registry is invalidated, so reading it costs nothing.
    /// The style sheets and classes in it are what <see cref="VeneerStyle.ApplyBrand"/> puts on a root; the colour and shape values are
    /// there for code that draws itself (graphs, previews) and for the Theme Editor.
    /// </summary>
    public sealed class VeneerThemeEffective
    {
        /// <summary>The structural class of the Pill tab style.</summary>
        internal const string PillClass = "vn-tabstyle--pill";

        /// <summary>The structural class of the Boxed tab style.</summary>
        internal const string BoxedClass = "vn-tabstyle--boxed";

        private static readonly string[] NoStrings = new string[0];

        /// <summary>The result when no theme applies: nothing is overridden, no sheets, no classes.</summary>
        public static readonly VeneerThemeEffective None = new VeneerThemeEffective(null, null, NoStrings, NoStrings);

        private readonly VeneerTheme systemTheme;
        private readonly VeneerTheme globalTheme;

        internal VeneerThemeEffective(VeneerTheme system, VeneerTheme global, string[] sheetPaths, string[] themeClasses)
        {
            systemTheme = system;
            globalTheme = global;
            SheetPaths = sheetPaths;
            ThemeClasses = themeClasses;

            VeneerTheme owner = Owner(system, global, system != null && system.OverrideAccent, global != null && global.OverrideAccent);
            HasAccent = owner != null;
            Accent = owner != null ? owner.Accent : default;

            owner = Owner(system, global, system != null && system.OverrideCardColor, global != null && global.OverrideCardColor);
            HasCardColor = owner != null;
            CardColor = owner != null ? owner.CardColor : default;

            owner = Owner(system, global, system != null && system.OverrideCardBorder, global != null && global.OverrideCardBorder);
            HasCardBorder = owner != null;
            CardBorder = owner != null ? owner.CardBorder : default;

            owner = Owner(system, global, system != null && system.OverrideText, global != null && global.OverrideText);
            HasText = owner != null;
            Text = owner != null ? owner.Text : default;

            owner = Owner(system, global, system != null && system.OverrideTextMuted, global != null && global.OverrideTextMuted);
            HasTextMuted = owner != null;
            TextMuted = owner != null ? owner.TextMuted : default;

            owner = Owner(system, global, system != null && system.OverrideRadius, global != null && global.OverrideRadius);
            HasRadius = owner != null;
            RadiusScale = owner != null ? ClampRadiusScale(owner.RadiusScale) : 1f;

            owner = Owner(system, global, system != null && system.OverrideDensity, global != null && global.OverrideDensity);
            HasDensity = owner != null;
            DensityScale = owner != null ? ClampDensityScale(owner.DensityScale) : 1f;

            owner = Owner(system, global, system != null && system.OverrideTabStyle, global != null && global.OverrideTabStyle);
            HasTabStyle = owner != null;
            TabStyle = owner != null ? owner.TabStyle : VeneerTabStyle.Underline;

            owner = Owner(system, global, system != null && system.OverrideMotion, global != null && global.OverrideMotion);
            HasMotion = owner != null;
            Motion = owner != null ? owner.Motion : VeneerMotion.Normal;

            // Underline and Normal are the kit's own look: no class. The classes come from the EFFECTIVE values, so a system theme that
            // picks Underline (or Normal motion) cancels a global Pill (or Lively).
            string tabClass = TabStyle == VeneerTabStyle.Pill ? PillClass
                : TabStyle == VeneerTabStyle.Boxed ? BoxedClass
                : null;
            string motionClass = MotionClassFor(Motion);
            if (tabClass == null && motionClass == null)
            {
                StructuralClasses = NoStrings;
            }
            else if (tabClass != null && motionClass != null)
            {
                StructuralClasses = new[] { tabClass, motionClass };
            }
            else
            {
                StructuralClasses = new[] { tabClass ?? motionClass };
            }
        }

        /// <summary>True when a theme overrides the accent colour.</summary>
        public bool HasAccent { get; }

        /// <summary>The effective accent colour; meaningful only when <see cref="HasAccent"/> is true.</summary>
        public Color Accent { get; }

        /// <summary>True when a theme overrides the card colour.</summary>
        public bool HasCardColor { get; }

        /// <summary>The effective card colour; meaningful only when <see cref="HasCardColor"/> is true.</summary>
        public Color CardColor { get; }

        /// <summary>True when a theme overrides the card border colour.</summary>
        public bool HasCardBorder { get; }

        /// <summary>The effective card border colour; meaningful only when <see cref="HasCardBorder"/> is true.</summary>
        public Color CardBorder { get; }

        /// <summary>True when a theme overrides the main text colour.</summary>
        public bool HasText { get; }

        /// <summary>The effective main text colour; meaningful only when <see cref="HasText"/> is true.</summary>
        public Color Text { get; }

        /// <summary>True when a theme overrides the muted text colour.</summary>
        public bool HasTextMuted { get; }

        /// <summary>The effective muted text colour; meaningful only when <see cref="HasTextMuted"/> is true.</summary>
        public Color TextMuted { get; }

        /// <summary>True when a theme overrides the corner roundness.</summary>
        public bool HasRadius { get; }

        /// <summary>The effective corner roundness as a multiple of the kit's own radii (0 = square, 1 = the kit's look, 2 = twice as round); 1 when not set.</summary>
        public float RadiusScale { get; }

        /// <summary>True when a theme overrides the spacing.</summary>
        public bool HasDensity { get; }

        /// <summary>The effective spacing as a multiple of the kit's own spacing (0.6 = tight, 1 = the kit's look, 1.5 = airy); 1 when not set.</summary>
        public float DensityScale { get; }

        /// <summary>True when a theme overrides the tab style.</summary>
        public bool HasTabStyle { get; }

        /// <summary>The effective tab style (Underline when not set).</summary>
        public VeneerTabStyle TabStyle { get; }

        /// <summary>True when a theme overrides the motion setting.</summary>
        public bool HasMotion { get; }

        /// <summary>
        /// The effective motion setting (Normal when not set). It reaches the style sheet as a root class (see <see cref="VeneerMotionKit"/>);
        /// Normal adds none.
        /// </summary>
        public VeneerMotion Motion { get; }

        /// <summary>
        /// The effective logo: the system theme's, else the global theme's; null when neither sets one (the brand's own logo stays).
        /// Read from the theme assets on every call, so a texture that Unity reloaded is never stale.
        /// </summary>
        public Texture2D Logo
        {
            get
            {
                if (systemTheme != null && systemTheme.Logo != null)
                {
                    return systemTheme.Logo;
                }

                return globalTheme != null ? globalTheme.Logo : null;
            }
        }

        /// <summary>The effective banner: the system theme's, else the global theme's; null when neither sets one (the brand's own banner stays).</summary>
        public Texture2D Banner
        {
            get
            {
                if (systemTheme != null && systemTheme.Banner != null)
                {
                    return systemTheme.Banner;
                }

                return globalTheme != null ? globalTheme.Banner : null;
            }
        }

        /// <summary>Asset paths of the generated style sheets to add to a root, in order: the global theme's first, then the system's, so the system wins.</summary>
        public string[] SheetPaths { get; }

        /// <summary>The theme classes (<c>vnt-xxxxxxxx</c>) to add to a root, in the same order as <see cref="SheetPaths"/>.</summary>
        public string[] ThemeClasses { get; }

        /// <summary>
        /// The structural classes to add to a root, such as <c>vn-tabstyle--pill</c> and the motion class (<c>vn-motion--off|subtle|lively</c>);
        /// empty when the effective look is the kit's own.
        /// </summary>
        public string[] StructuralClasses { get; }

        /// <summary>True when one of the themes this was made from has been destroyed, so the registry must make it again.</summary>
        internal bool IsStale
        {
            get
            {
                return IsDead(systemTheme) || IsDead(globalTheme);
            }
        }

        // A theme that was alive when this was made and compares equal to null now was deleted or reloaded.
        private bool IsDead(VeneerTheme theme)
        {
            return !ReferenceEquals(theme, null) && theme == null;
        }

        /// <summary>The root class of a motion setting; null for Normal (the kit's own look) and for a value outside the enum.</summary>
        internal static string MotionClassFor(VeneerMotion motion)
        {
            switch (motion)
            {
                case VeneerMotion.Off:
                    return VeneerMotionKit.OffClass;
                case VeneerMotion.Subtle:
                    return VeneerMotionKit.SubtleClass;
                case VeneerMotion.Lively:
                    return VeneerMotionKit.LivelyClass;
                default:
                    return null;
            }
        }

        /// <summary>Keeps a corner roundness scale inside 0..2 (a value that is not a number counts as 1).</summary>
        internal static float ClampRadiusScale(float scale)
        {
            return float.IsNaN(scale) ? 1f : Mathf.Clamp(scale, 0f, 2f);
        }

        /// <summary>Keeps a spacing scale inside 0.6..1.5 (a value that is not a number counts as 1).</summary>
        internal static float ClampDensityScale(float scale)
        {
            return float.IsNaN(scale) ? 1f : Mathf.Clamp(scale, 0.6f, 1.5f);
        }

        private static VeneerTheme Owner(VeneerTheme system, VeneerTheme global, bool systemOverrides, bool globalOverrides)
        {
            if (systemOverrides)
            {
                return system;
            }

            return globalOverrides ? global : null;
        }
    }
}
