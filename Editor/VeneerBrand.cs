using System;
using UnityEditor;
using UnityEngine;

namespace Veneer
{
    /// <summary>
    /// The identity of one system in the shared editor look: its name, subtitle, accent colour, logo and banner pictures, an
    /// optional folder with extra icon glyphs and an optional extra style sheet. A brand is not registered anywhere: a system keeps
    /// one in a static field of an internal <c>&lt;System&gt;Brand</c> class and passes it to <see cref="VeneerInspector.Brand"/>,
    /// <see cref="VeneerStyle.Root"/>, <see cref="VeneerWindow"/> and so on.
    /// <para>
    /// Accent: USS cannot be given a colour from C#, so the accent reaches the style sheet in two ways. (1) The brand picks one of the
    /// accent presets of the kit sheet (<c>vn-accent--blue</c>, <c>violet</c>, <c>magenta</c>, <c>red</c>, <c>orange</c>, <c>amber</c>,
    /// <c>green</c>, <c>teal</c>, <c>slate</c>; cyan is the default) through <paramref name="accentClass"/>, or automatically the
    /// preset nearest to <see cref="Accent"/>. (2) For an exact colour the system ships a tiny style sheet that sets
    /// <c>--vn-accent</c>, <c>--vn-accent-soft</c>, <c>--vn-accent-line</c> and <c>--vn-pressed</c> on <c>.vn-root</c> and names it in
    /// <paramref name="styleSheetPath"/>. <see cref="Accent"/> itself is always exact in code that draws with colours
    /// (<see cref="VeneerGraph"/>).
    /// </para>
    /// </summary>
    public sealed class VeneerBrand
    {
        /// <summary>The kit's own accent (cyan-blue, #3FB6FF).</summary>
        public static readonly Color DefaultAccent = new Color32(63, 182, 255, 255);

        private static readonly string[] PresetNames =
        {
            "blue", "violet", "magenta", "red", "orange", "amber", "green", "teal", "slate",
        };

        // The dark-skin accent of each preset above, in the same order (kept in step with Veneer.uss).
        private static readonly Color32[] PresetColors =
        {
            new Color32(0x5B, 0x8C, 0xFF, 255), new Color32(0xA7, 0x8B, 0xFA, 255), new Color32(0xF4, 0x72, 0xB6, 255),
            new Color32(0xF8, 0x71, 0x71, 255), new Color32(0xFB, 0x92, 0x3C, 255), new Color32(0xFB, 0xBF, 0x24, 255),
            new Color32(0x4A, 0xDE, 0x80, 255), new Color32(0x2D, 0xD4, 0xBF, 255), new Color32(0x94, 0xA3, 0xB8, 255),
        };

        private static VeneerBrand defaultBrand;

        private readonly string logoPath;
        private readonly string bannerPath;
        private Texture2D logo;
        private Texture2D banner;

        /// <summary>Creates a brand from textures that are already loaded (or null).</summary>
        /// <param name="name">Display name, shown in banners and window title bars.</param>
        /// <param name="subtitle">Short line under the name; may be null.</param>
        /// <param name="accent">Accent colour; null keeps <see cref="DefaultAccent"/>.</param>
        /// <param name="logo">Square logo picture; may be null.</param>
        /// <param name="banner">Wide banner picture (about 1200x200); may be null for a plain dark banner.</param>
        /// <param name="iconFolder">Asset folder with extra glyph PNGs (<c>Assets/.../Icons</c>, white on transparent); may be null.</param>
        /// <param name="styleSheetPath">Asset path of an extra .uss added after the kit sheet; may be null.</param>
        /// <param name="accentClass">Accent preset class such as <c>vn-accent--orange</c>; null picks the nearest preset to <paramref name="accent"/>.</param>
        public VeneerBrand(
            string name,
            string subtitle = null,
            Color? accent = null,
            Texture2D logo = null,
            Texture2D banner = null,
            string iconFolder = null,
            string styleSheetPath = null,
            string accentClass = null)
        {
            Name = name ?? string.Empty;
            Subtitle = subtitle;
            Accent = accent ?? DefaultAccent;
            this.logo = logo;
            this.banner = banner;
            IconFolder = TrimFolder(iconFolder);
            StyleSheetPath = styleSheetPath;
            AccentClass = string.IsNullOrEmpty(accentClass) ? ResolveAccentClass(Accent) : accentClass;
        }

        private VeneerBrand(
            string name,
            string subtitle,
            Color accent,
            string logoPath,
            string bannerPath,
            string iconFolder,
            string styleSheetPath,
            string accentClass)
            : this(name, subtitle, accent, (Texture2D)null, (Texture2D)null, iconFolder, styleSheetPath, accentClass)
        {
            this.logoPath = logoPath;
            this.bannerPath = bannerPath;
        }

        /// <summary>A brand with no pictures and the default accent; used when a caller passes null.</summary>
        public static VeneerBrand Default
        {
            get
            {
                if (defaultBrand == null)
                {
                    defaultBrand = new VeneerBrand("Studio");
                }

                return defaultBrand;
            }
        }

        /// <summary>Display name.</summary>
        public string Name { get; }

        /// <summary>Short line under the name; may be null.</summary>
        public string Subtitle { get; }

        /// <summary>Accent colour, exact (used by drawing code such as <see cref="VeneerGraph"/>).</summary>
        public Color Accent { get; }

        /// <summary>Asset folder with the brand's extra icon glyphs; null when it has none. Searched after the kit's own icons.</summary>
        public string IconFolder { get; }

        /// <summary>Asset path of the brand's extra style sheet; null when it has none.</summary>
        public string StyleSheetPath { get; }

        /// <summary>The accent preset class added to every root of this brand (for example <c>vn-accent--orange</c>); null for the default cyan.</summary>
        public string AccentClass { get; }

        /// <summary>The logo texture (loaded again from its path if Unity destroyed it); null when the brand has none.</summary>
        public Texture2D Logo
        {
            get
            {
                if (logo == null && !string.IsNullOrEmpty(logoPath))
                {
                    logo = AssetDatabase.LoadAssetAtPath<Texture2D>(logoPath);
                }

                return logo;
            }
        }

        /// <summary>The banner texture (loaded again from its path if Unity destroyed it); null when the brand has none.</summary>
        public Texture2D Banner
        {
            get
            {
                if (banner == null && !string.IsNullOrEmpty(bannerPath))
                {
                    banner = AssetDatabase.LoadAssetAtPath<Texture2D>(bannerPath);
                }

                return banner;
            }
        }

        /// <summary>
        /// Creates a brand whose logo and banner are loaded lazily from asset paths, so a brand made in a static initializer survives
        /// reimports and is valid before the asset database is ready.
        /// </summary>
        /// <param name="name">Display name.</param>
        /// <param name="subtitle">Short line under the name; may be null.</param>
        /// <param name="accent">Accent colour.</param>
        /// <param name="logoPath">Asset path of the logo PNG; may be null.</param>
        /// <param name="bannerPath">Asset path of the banner PNG; may be null.</param>
        /// <param name="iconFolder">Asset folder with extra glyph PNGs; may be null.</param>
        /// <param name="styleSheetPath">Asset path of an extra .uss for an exact accent; may be null.</param>
        /// <param name="accentClass">Accent preset class; null picks the nearest preset.</param>
        public static VeneerBrand FromAssets(
            string name,
            string subtitle,
            Color accent,
            string logoPath,
            string bannerPath,
            string iconFolder = null,
            string styleSheetPath = null,
            string accentClass = null)
        {
            return new VeneerBrand(name, subtitle, accent, logoPath, bannerPath, iconFolder, styleSheetPath, accentClass);
        }

        /// <summary>The accent preset class nearest to a colour (null for the default cyan family). Allocation free.</summary>
        public static string ResolveAccentClass(Color accent)
        {
            Color32 target = accent;
            int best = -1;
            int bestDistance = Distance(target, DefaultAccent);
            for (int i = 0; i < PresetColors.Length; i++)
            {
                int distance = Distance(target, PresetColors[i]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best < 0 ? null : "vn-accent--" + PresetNames[best];
        }

        private static int Distance(Color32 a, Color32 b)
        {
            int r = a.r - b.r;
            int g = a.g - b.g;
            int bl = a.b - b.b;
            return r * r + g * g + bl * bl;
        }

        private static string TrimFolder(string folder)
        {
            return string.IsNullOrEmpty(folder) ? null : folder.Replace('\\', '/').TrimEnd('/');
        }
    }
}
