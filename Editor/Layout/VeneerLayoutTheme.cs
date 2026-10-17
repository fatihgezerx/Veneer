using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// The look side of a layout. USS variables cannot be set from C#, so a layout that uses its own accent gets a small companion
    /// style sheet (<c>&lt;layout&gt;.uss</c>, next to the layout asset) whose rule names the layout by a class made from its GUID; the
    /// renderer puts that class on the Inspector root. A layout that does not use its own accent needs no sheet and no class.
    /// <para>
    /// A layout can also borrow a whole look: a custom theme (<see cref="VeneerLayout.Theme"/>) or a system's look
    /// (<see cref="VeneerLayout.ThemeBrand"/>, ignored when a custom theme is set). <see cref="ApplyTo"/> puts them on a root in this
    /// order: the system's look, the custom theme, then the layout's own accent, so the layout's accent always wins.
    /// <see cref="ThemeLogo"/> and <see cref="ThemeBanner"/> give the pictures of that theme or system.
    /// </para>
    /// </summary>
    public static class VeneerLayoutTheme
    {
        private const string ClassPrefix = "vnl-";
        private const int HexLength = 8;

        private static readonly Dictionary<int, BrandEntry> Brands = new Dictionary<int, BrandEntry>(8);

        /// <summary>
        /// The USS class that selects this layout's colours, <c>vnl-</c> plus the first 8 hex characters of the layout's asset GUID; null for
        /// a layout that is not saved as an asset yet.
        /// </summary>
        public static string RootClass(VeneerLayout layout)
        {
            string guid = GuidOf(layout);
            return guid.Length < HexLength ? null : ClassPrefix + guid.Substring(0, HexLength);
        }

        /// <summary>
        /// The brand of a layout: its name, and (when <see cref="VeneerLayout.UseAccent"/> is on) its accent colour and its companion style
        /// sheet, which is written first if it is missing or out of date. The brand is cached per layout and made again only when the name,
        /// the accent or the switch changed. It carries only the layout's own look: the theme or system look the layout borrows is added by
        /// <see cref="ApplyTo"/> (which is safe to call on a root made with this brand, and puts the layout's accent sheet last again).
        /// </summary>
        public static VeneerBrand BrandFor(VeneerLayout layout)
        {
            if (layout == null)
            {
                return VeneerBrand.Default;
            }

            string name = DisplayName(layout);
            bool use = layout.UseAccent;
            Color32 accent = layout.Accent;
            int id = layout.GetInstanceID();
            if (Brands.TryGetValue(id, out BrandEntry entry)
                && entry.Use == use
                && entry.Name == name
                && (!use || (entry.Accent.r == accent.r && entry.Accent.g == accent.g && entry.Accent.b == accent.b)))
            {
                return entry.Brand;
            }

            string sheet = null;
            if (use)
            {
                WriteSheet(layout);
                string path = SheetPath(layout);
                if (path != null && AssetDatabase.LoadAssetAtPath<StyleSheet>(path) != null)
                {
                    sheet = path;
                }
            }

            VeneerBrand brand = use
                ? new VeneerBrand(name, subtitle: null, accent: (Color)accent, styleSheetPath: sheet)
                : new VeneerBrand(name);

            // Not cached while the sheet is still missing, so the next Inspector open tries again.
            if (!use || sheet != null)
            {
                Brands[id] = new BrandEntry { Use = use, Name = name, Accent = accent, Brand = brand };
            }

            return brand;
        }

        /// <summary>
        /// The system whose whole look the layout borrows: the catalog brand named by <see cref="VeneerLayout.ThemeBrand"/>. Null when the
        /// name is empty or unknown, or when <see cref="VeneerLayout.Theme"/> is set (a custom theme wins over a system).
        /// </summary>
        public static VeneerBrand ThemeBrandOf(VeneerLayout layout)
        {
            if (layout == null || layout.Theme != null || string.IsNullOrEmpty(layout.ThemeBrand))
            {
                return null;
            }

            return VeneerBrandCatalog.Find(layout.ThemeBrand);
        }

        /// <summary>
        /// The logo of the layout's theme: the custom theme's <see cref="VeneerTheme.Logo"/> when <see cref="VeneerLayout.Theme"/> is set,
        /// else the borrowed system's logo (with that system's own theme applied); null when there is none.
        /// </summary>
        public static Texture2D ThemeLogo(VeneerLayout layout)
        {
            if (layout == null)
            {
                return null;
            }

            if (layout.Theme != null)
            {
                return layout.Theme.Logo;
            }

            VeneerBrand brand = ThemeBrandOf(layout);
            return brand != null ? brand.Logo : null;
        }

        /// <summary>
        /// The banner picture of the layout's theme: the custom theme's <see cref="VeneerTheme.Banner"/> when <see cref="VeneerLayout.Theme"/>
        /// is set, else the borrowed system's banner (with that system's own theme applied); null when there is none.
        /// </summary>
        public static Texture2D ThemeBanner(VeneerLayout layout)
        {
            if (layout == null)
            {
                return null;
            }

            if (layout.Theme != null)
            {
                return layout.Theme.Banner;
            }

            VeneerBrand brand = ThemeBrandOf(layout);
            return brand != null ? brand.Banner : null;
        }

        /// <summary>
        /// Puts the look of <paramref name="layout"/> on <paramref name="root"/>, in this order: the borrowed system's look
        /// (<see cref="VeneerStyle.ApplyBrand"/>), the custom theme (<see cref="VeneerStyle.ApplyTheme"/>), then the layout's own accent
        /// sheet and classes, so the layout's accent still wins. The style sheets are put in that order even when the root already had
        /// some of them (a root made with <see cref="BrandFor"/> already carries the layout's accent sheet), and a tab style of the
        /// global or a system theme is replaced by the tab style of the borrowed look. Safe to call more than once on the same root.
        /// Does nothing for a layout that borrows nothing and has no own accent.
        /// </summary>
        /// <param name="root">The Inspector root (a Veneer root, with the kit sheet).</param>
        /// <param name="layout">The layout; null does nothing.</param>
        public static void ApplyTo(VisualElement root, VeneerLayout layout)
        {
            if (root == null || layout == null)
            {
                return;
            }

            VeneerBrand brand = ThemeBrandOf(layout);
            if (brand != null)
            {
                VeneerStyle.ClearStructuralClasses(root);
                VeneerStyle.ApplyBrand(root, brand);

                // ApplyBrand skips a sheet the root already has; the brand's sheet, then its theme layers, must come after what the root had.
                MoveSheetToEnd(root, brand.StyleSheetPath);
                MoveSheetsToEnd(root, VeneerThemeRegistry.Effective(brand).SheetPaths);
            }
            else if (layout.Theme != null)
            {
                VeneerStyle.ApplyTheme(root, layout.Theme);
                MoveSheetsToEnd(root, VeneerThemeRegistry.EffectiveFor(layout.Theme).SheetPaths);
            }

            if (layout.UseAccent)
            {
                VeneerBrand own = BrandFor(layout);
                if (!string.IsNullOrEmpty(own.AccentClass))
                {
                    root.AddToClassList(own.AccentClass);
                }

                string className = RootClass(layout);
                if (!string.IsNullOrEmpty(className))
                {
                    root.AddToClassList(className);
                }

                MoveSheetToEnd(root, own.StyleSheetPath);
            }
        }

        /// <summary>
        /// Writes (or updates) the companion style sheet of <paramref name="layout"/> next to its asset and imports it. The file is only
        /// touched when its text would change, so calling this often is harmless. Does nothing for a layout that does not use its own
        /// accent, or that is not an asset yet.
        /// </summary>
        public static void WriteSheet(VeneerLayout layout)
        {
            if (layout == null || !layout.UseAccent)
            {
                return;
            }

            string sheetPath = SheetPath(layout);
            string className = RootClass(layout);
            if (sheetPath == null || className == null)
            {
                return;
            }

            string text = BuildText(className, layout.Accent);
            string fullPath = FullPath(sheetPath);
            try
            {
                if (File.Exists(fullPath) && string.Equals(File.ReadAllText(fullPath), text, StringComparison.Ordinal))
                {
                    return;
                }

                // Write beside the target and swap, so a failure never leaves a half-written sheet. Unity ignores .tmp files.
                string temp = fullPath + ".tmp";
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                if (File.Exists(fullPath))
                {
                    File.Replace(temp, fullPath, null);
                }
                else
                {
                    File.Move(temp, fullPath);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Veneer: could not write the style sheet " + sheetPath + ": " + exception.Message);
                return;
            }

            AssetDatabase.ImportAsset(sheetPath);
        }

        /// <summary>The asset path of the layout's companion style sheet (the layout's path with the extension <c>.uss</c>); null when the layout is not an asset.</summary>
        public static string SheetPath(VeneerLayout layout)
        {
            string assetPath = layout == null ? null : AssetDatabase.GetAssetPath(layout);
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            return assetPath.Substring(0, assetPath.Length - Path.GetExtension(assetPath).Length) + ".uss";
        }

        /// <summary>The asset GUID of the layout, or an empty string when it is not saved as an asset.</summary>
        internal static string GuidOf(VeneerLayout layout)
        {
            if (layout == null)
            {
                return string.Empty;
            }

            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(layout, out string guid, out long _) ? guid : string.Empty;
        }

        // Puts a sheet last in the root's list (adds it when it is missing), so it wins over the sheets before it.
        private static void MoveSheetToEnd(VisualElement root, string assetPath)
        {
            StyleSheet sheet = VeneerStyle.LoadSheet(assetPath);
            if (sheet == null)
            {
                return;
            }

            if (root.styleSheets.Contains(sheet))
            {
                root.styleSheets.Remove(sheet);
            }

            root.styleSheets.Add(sheet);
        }

        private static void MoveSheetsToEnd(VisualElement root, string[] assetPaths)
        {
            for (int i = 0; i < assetPaths.Length; i++)
            {
                MoveSheetToEnd(root, assetPaths[i]);
            }
        }

        private static string DisplayName(VeneerLayout layout)
        {
            if (!string.IsNullOrEmpty(layout.Title))
            {
                return layout.Title;
            }

            Type type = layout.ResolveType();
            return type != null ? ObjectNames.NicifyVariableName(type.Name) : layout.name;
        }

        private static string FullPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath);
        }

        // One rule for the dark skin and one with the extra class of the light skin, so the light accent presets (three classes) lose to it.
        private static string BuildText(string className, Color accentColor)
        {
            Color32 c = accentColor;
            string solid = "#" + c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2");
            string body = " { --vn-accent: " + solid
                + "; --vn-accent-soft: " + Rgba(c, 0.16f)
                + "; --vn-accent-line: " + Rgba(c, 0.55f)
                + "; --vn-pressed: " + Rgba(c, 0.24f) + "; }\n";
            return "/* Written by the Veneer Inspector Designer from the layout's accent colour. Edit the layout, not this file. */\n"
                + ".vn-root." + className + body
                + ".vn-root.vn-root--light." + className + body;
        }

        private static string Rgba(Color32 c, float alpha)
        {
            return "rgba(" + c.r + ", " + c.g + ", " + c.b + ", " + alpha.ToString("0.##", CultureInfo.InvariantCulture) + ")";
        }

        private struct BrandEntry
        {
            public bool Use;
            public string Name;
            public Color32 Accent;
            public VeneerBrand Brand;
        }
    }
}
