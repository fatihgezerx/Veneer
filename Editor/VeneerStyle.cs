using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// The shared look of every studio editor surface: loads the kit style sheet (<c>Styles/Veneer.uss</c>) and the white glyph icons
    /// (<c>Icons/*.png</c> of the kit, then the icon folder of a <see cref="VeneerBrand"/>), makes roots, and builds the small reusable
    /// widgets (see the other half of this class, <c>VeneerStyle.Widgets.cs</c>) as plain <see cref="VisualElement"/> trees that only
    /// carry USS classes. The kit folder is found through the location of this very script, so the kit can sit anywhere in the project.
    /// Everything is cached, safe across domain reloads, and never returns null: a missing icon is a 1x1 transparent texture, a missing
    /// sheet is an empty one (the next call tries again).
    /// </summary>
    public static partial class VeneerStyle
    {
        private const string ScriptName = "VeneerStyle";
        private const string SheetRelativePath = "/Styles/Veneer.uss";
        private const string IconsRelativeFolder = "/Icons";
        private const string RootClass = "vn-root";
        private const string LightClass = "vn-root--light";

        private static readonly IconSet KitIcons = new IconSet();
        private static readonly Dictionary<string, IconSet> BrandIcons = new Dictionary<string, IconSet>(4);
        private static readonly Dictionary<string, StyleSheet> ExtraSheets = new Dictionary<string, StyleSheet>(4);

        private static string folder;
        private static string kitIconsFolder;
        private static StyleSheet sheet;
        private static StyleSheet placeholderSheet;
        private static Texture2D placeholderIcon;

        static VeneerStyle()
        {
            // The placeholders are HideAndDontSave objects, which a domain reload would otherwise leak.
            AssemblyReloadEvents.beforeAssemblyReload += ReleasePlaceholders;
        }

        /// <summary>The kit style sheet. An empty sheet (never null) while the file cannot be loaded yet, for example right after import.</summary>
        public static StyleSheet Sheet
        {
            get
            {
                if (sheet != null)
                {
                    return sheet;
                }

                string kit = KitFolder;
                if (kit != null)
                {
                    sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(kit + SheetRelativePath);
                }

                if (sheet != null)
                {
                    return sheet;
                }

                if (placeholderSheet == null)
                {
                    placeholderSheet = ScriptableObject.CreateInstance<StyleSheet>();
                    placeholderSheet.hideFlags = HideFlags.HideAndDontSave;
                }

                return placeholderSheet;
            }
        }

        /// <summary>The asset folder that holds the kit's Editor code (and with it <c>Icons/</c> and <c>Styles/</c>); null while it cannot be found.</summary>
        public static string KitFolder
        {
            get
            {
                if (!string.IsNullOrEmpty(folder))
                {
                    return folder;
                }

                string[] guids = AssetDatabase.FindAssets("t:MonoScript " + ScriptName);
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(Path.GetFileNameWithoutExtension(path), ScriptName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    folder = Path.GetDirectoryName(path).Replace('\\', '/');
                    kitIconsFolder = folder + IconsRelativeFolder;
                    return folder;
                }

                return null;
            }
        }

        /// <summary>
        /// The icon <c>&lt;name&gt;.png</c>: first in the kit's <c>Icons</c> folder, then in the brand's icon folder. A white glyph; tint it
        /// with <c>-unity-background-image-tint-color</c>. Returns a 1x1 transparent texture, never null, when it is missing.
        /// </summary>
        /// <param name="name">Lowercase-kebab icon name without extension, for example <c>chevron-down</c>.</param>
        /// <param name="brand">The brand whose icon folder is searched after the kit's; may be null.</param>
        public static Texture2D Icon(string name, VeneerBrand brand = null)
        {
            Texture2D found = IconOrNull(name, brand);
            if (found != null)
            {
                return found;
            }

            if (placeholderIcon == null)
            {
                placeholderIcon = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                placeholderIcon.SetPixel(0, 0, new Color(1f, 1f, 1f, 0f));
                placeholderIcon.Apply(false, true);
                placeholderIcon.hideFlags = HideFlags.HideAndDontSave;
            }

            return placeholderIcon;
        }

        /// <summary>The same as <see cref="Icon"/>, but null when the icon exists nowhere (for callers that must tell).</summary>
        public static Texture2D IconOrNull(string name, VeneerBrand brand = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (KitFolder != null)
            {
                Texture2D kit = LookUp(KitIcons, kitIconsFolder, name);
                if (kit != null)
                {
                    return kit;
                }
            }

            if (brand == null || string.IsNullOrEmpty(brand.IconFolder))
            {
                return null;
            }

            if (!BrandIcons.TryGetValue(brand.IconFolder, out IconSet set))
            {
                set = new IconSet();
                BrandIcons[brand.IconFolder] = set;
            }

            return LookUp(set, brand.IconFolder, name);
        }

        /// <summary>Loads an extra style sheet by asset path (cached); null when the path is empty or the sheet is missing.</summary>
        public static StyleSheet LoadSheet(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            if (ExtraSheets.TryGetValue(assetPath, out StyleSheet cached) && cached != null)
            {
                return cached;
            }

            StyleSheet loaded = AssetDatabase.LoadAssetAtPath<StyleSheet>(assetPath);
            if (loaded != null)
            {
                ExtraSheets[assetPath] = loaded;
            }

            return loaded;
        }

        /// <summary>
        /// A new root element with the kit sheet (and the brand's extra sheet) added, the class <c>vn-root</c> (the palette lives on it), the
        /// brand's accent preset class, and <c>vn-root--light</c> in the light editor skin.
        /// </summary>
        /// <param name="brand">The brand; null for the default look.</param>
        /// <param name="extraClass">Optional extra USS class.</param>
        public static VisualElement Root(VeneerBrand brand = null, string extraClass = null)
        {
            var root = new VisualElement();
            root.styleSheets.Add(Sheet);
            root.AddToClassList(RootClass);
            if (!EditorGUIUtility.isProSkin)
            {
                root.AddToClassList(LightClass);
            }

            ApplyBrand(root, brand);
            if (!string.IsNullOrEmpty(extraClass))
            {
                root.AddToClassList(extraClass);
            }

            return root;
        }

        /// <summary>Applies a brand to an existing root: its accent preset class and its extra style sheet.</summary>
        public static void ApplyBrand(VisualElement root, VeneerBrand brand)
        {
            if (brand == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(brand.AccentClass))
            {
                root.AddToClassList(brand.AccentClass);
            }

            StyleSheet extra = LoadSheet(brand.StyleSheetPath);
            if (extra != null && !root.styleSheets.Contains(extra))
            {
                root.styleSheets.Add(extra);
            }
        }

        /// <summary>Forgets every cached icon and sheet lookup, including the misses. Called by the icon postprocessor after an import.</summary>
        public static void InvalidateIcons()
        {
            KitIcons.Clear();
            BrandIcons.Clear();
            ExtraSheets.Clear();
        }

        private static Texture2D LookUp(IconSet set, string iconFolder, string name)
        {
            if (set.Loaded.TryGetValue(name, out Texture2D cached) && cached != null)
            {
                return cached;
            }

            if (set.Missing.Contains(name))
            {
                return null;
            }

            Texture2D loaded = AssetDatabase.LoadAssetAtPath<Texture2D>(iconFolder + "/" + name + ".png");
            if (loaded != null)
            {
                set.Loaded[name] = loaded;
            }
            else
            {
                set.Missing.Add(name);
            }

            return loaded;
        }

        private static void ReleasePlaceholders()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= ReleasePlaceholders;
            if (placeholderSheet != null)
            {
                UnityEngine.Object.DestroyImmediate(placeholderSheet);
                placeholderSheet = null;
            }

            if (placeholderIcon != null)
            {
                UnityEngine.Object.DestroyImmediate(placeholderIcon);
                placeholderIcon = null;
            }
        }

        private sealed class IconSet
        {
            public readonly Dictionary<string, Texture2D> Loaded = new Dictionary<string, Texture2D>(64);
            public readonly HashSet<string> Missing = new HashSet<string>();

            public void Clear()
            {
                Loaded.Clear();
                Missing.Clear();
            }
        }
    }
}
