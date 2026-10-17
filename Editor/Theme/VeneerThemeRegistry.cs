using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// Finds the <see cref="VeneerTheme"/> of a system (by brand name), the global theme and the custom themes (reusable looks that
    /// belong to no system), and merges them into the <see cref="VeneerThemeEffective"/> look the kit applies. The themes are read from
    /// the project once (lazily) and cached, the merged result is memoised per brand name (and per custom theme); system and global
    /// lookups never return a custom theme. <see cref="VeneerThemePostprocessor"/> calls <see cref="Invalidate"/> when a theme
    /// asset is imported, deleted or moved, and a domain reload starts with empty caches. With no theme in the project every lookup is a
    /// dictionary hit that returns <see cref="VeneerThemeEffective.None"/>, so the kit looks exactly as it always did.
    /// </summary>
    public static class VeneerThemeRegistry
    {
        private static readonly List<VeneerTheme> Themes = new List<VeneerTheme>(8);
        private static readonly Dictionary<string, VeneerTheme> ByBrand = new Dictionary<string, VeneerTheme>(8, StringComparer.Ordinal);
        private static readonly Dictionary<string, VeneerThemeEffective> EffectiveByBrand = new Dictionary<string, VeneerThemeEffective>(16, StringComparer.Ordinal);
        private static readonly Dictionary<VeneerTheme, VeneerThemeEffective> EffectiveByCustom = new Dictionary<VeneerTheme, VeneerThemeEffective>(8);

        // A new list is made on every rebuild (never cleared), so a list that was handed out stays valid and unchanged.
        private static List<VeneerTheme> customThemes = new List<VeneerTheme>(0);
        private static VeneerTheme globalTheme;
        private static bool built;
        private static bool changePending;

        /// <summary>
        /// Raised once (on the editor's next idle tick, never during an import) after the set of themes may have changed. By then the
        /// open Inspectors and <see cref="VeneerWindow"/>s have already been asked to rebuild.
        /// </summary>
        public static event Action Changed;

        /// <summary>The global theme (the one with an empty <see cref="VeneerTheme.BrandName"/>); null when the project has none.</summary>
        public static VeneerTheme Global
        {
            get
            {
                EnsureBuilt();
                return globalTheme != null ? globalTheme : null;
            }
        }

        /// <summary>
        /// Every custom theme (<see cref="VeneerTheme.Custom"/>) in the project, ordered by <see cref="VeneerTheme.ThemeName"/> (ignoring case),
        /// then by asset path; never null. The list is rebuilt together with the cache: a list already handed out stays valid and unchanged.
        /// </summary>
        public static IReadOnlyList<VeneerTheme> Custom
        {
            get
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    EnsureBuilt();
                    bool dead = false;
                    for (int i = 0; i < customThemes.Count; i++)
                    {
                        if (customThemes[i] == null)
                        {
                            dead = true;
                            break;
                        }
                    }

                    if (!dead)
                    {
                        break;
                    }

                    // A destroyed asset: read the project again once.
                    built = false;
                }

                return customThemes;
            }
        }

        /// <summary>The custom theme with this exact <see cref="VeneerTheme.ThemeName"/> (the first one in <see cref="Custom"/> order); null when there is none or the name is empty.</summary>
        public static VeneerTheme FindCustom(string themeName)
        {
            if (string.IsNullOrEmpty(themeName))
            {
                return null;
            }

            IReadOnlyList<VeneerTheme> list = Custom;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].ThemeName, themeName, StringComparison.Ordinal))
                {
                    return list[i];
                }
            }

            return null;
        }

        /// <summary>The theme made for the brand with this exact name (<see cref="VeneerBrand.Name"/>); null when there is none or the name is empty. The global theme is not returned here.</summary>
        public static VeneerTheme Find(string brandName)
        {
            if (string.IsNullOrEmpty(brandName))
            {
                return null;
            }

            for (int attempt = 0; attempt < 2; attempt++)
            {
                EnsureBuilt();
                if (!ByBrand.TryGetValue(brandName, out VeneerTheme found))
                {
                    return null;
                }

                if (found != null)
                {
                    return found;
                }

                // A destroyed asset: read the project again once.
                built = false;
            }

            return null;
        }

        /// <summary>
        /// The merged look for a brand: the system's theme where it overrides a setting, else the global theme, else "not set". Memoised
        /// per brand name, so it is safe to call for every Inspector build. A null brand (the default look) gets the global theme only.
        /// Never null; with no theme at all it is <see cref="VeneerThemeEffective.None"/>.
        /// </summary>
        public static VeneerThemeEffective Effective(VeneerBrand brand)
        {
            string key = brand == null ? string.Empty : brand.Name;
            EnsureBuilt();
            if (EffectiveByBrand.TryGetValue(key, out VeneerThemeEffective cached))
            {
                if (!cached.IsStale)
                {
                    return cached;
                }

                built = false;
                EnsureBuilt();
            }

            VeneerTheme system = null;
            if (key.Length > 0 && ByBrand.TryGetValue(key, out VeneerTheme found) && found != null)
            {
                system = found;
            }

            VeneerTheme global = globalTheme != null ? globalTheme : null;
            if (system == null && global == null)
            {
                EffectiveByBrand[key] = VeneerThemeEffective.None;
                return VeneerThemeEffective.None;
            }

            var sheets = new List<string>(2);
            var classes = new List<string>(2);
            AddLayer(global, sheets, classes);
            AddLayer(system, sheets, classes);
            var merged = new VeneerThemeEffective(system, global, sheets.ToArray(), classes.ToArray());
            EffectiveByBrand[key] = merged;
            return merged;
        }

        /// <summary>
        /// The merged look of a custom theme: the custom theme where it overrides a setting, else the global theme, else "not set" (a
        /// custom theme is normally a full snapshot, so the global theme rarely shows through). Memoised per theme, so it is safe to call
        /// for every Inspector build. A null (or destroyed) theme gets the global theme only, like <c>Effective(null)</c>. Never null.
        /// </summary>
        public static VeneerThemeEffective EffectiveFor(VeneerTheme custom)
        {
            if (custom == null)
            {
                return Effective(null);
            }

            EnsureBuilt();
            if (EffectiveByCustom.TryGetValue(custom, out VeneerThemeEffective cached))
            {
                if (!cached.IsStale)
                {
                    return cached;
                }

                built = false;
                EnsureBuilt();
            }

            VeneerTheme global = globalTheme != null ? globalTheme : null;
            var sheets = new List<string>(2);
            var classes = new List<string>(2);
            AddLayer(global, sheets, classes);
            AddLayer(custom, sheets, classes);
            var merged = new VeneerThemeEffective(custom, global, sheets.ToArray(), classes.ToArray());
            EffectiveByCustom[custom] = merged;
            return merged;
        }

        /// <summary>
        /// Drops the caches so the next lookup reads the project again, and raises <see cref="Changed"/> once. Called by the postprocessor;
        /// call it yourself after changing a theme in memory (before it is saved) so the kit picks the change up.
        /// </summary>
        public static void Invalidate()
        {
            built = false;
            EffectiveByBrand.Clear();
            EffectiveByCustom.Clear();
            if (changePending)
            {
                return;
            }

            changePending = true;
            EditorApplication.delayCall += RaiseChanged;
        }

        private static void RaiseChanged()
        {
            changePending = false;

            // Sheet objects may have been replaced by a reimport: forget the loaded ones so roots get fresh ones.
            VeneerStyle.InvalidateIcons();
            ActiveEditorTracker.sharedTracker.ForceRebuild();
            VeneerWindow.RebuildOpenWindows();

            Action handler = Changed;
            if (handler != null)
            {
                handler();
            }
        }

        // A theme that overrides colours, roundness or spacing contributes its class and its generated sheet.
        private static void AddLayer(VeneerTheme theme, List<string> sheets, List<string> classes)
        {
            if (theme == null || !VeneerThemeSheet.HasContent(theme))
            {
                return;
            }

            // Not while assets are importing: writing and importing a sheet then is unsafe; the layer shows up on the next build.
            if (!EditorApplication.isUpdating)
            {
                VeneerThemeSheet.Write(theme);
            }

            string path = VeneerThemeSheet.SheetPath(theme);
            string className = VeneerThemeSheet.RootClass(theme);
            if (path == null || className == null || AssetDatabase.LoadAssetAtPath<StyleSheet>(path) == null)
            {
                return;
            }

            sheets.Add(path);
            classes.Add(className);
        }

        private static void EnsureBuilt()
        {
            if (built)
            {
                return;
            }

            Themes.Clear();
            ByBrand.Clear();
            EffectiveByBrand.Clear();
            EffectiveByCustom.Clear();
            globalTheme = null;

            string[] guids = AssetDatabase.FindAssets("t:VeneerTheme");
            var paths = new string[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
            }

            // Sorted so that two themes for one brand always give the same winner.
            Array.Sort(paths, StringComparer.Ordinal);
            var customs = new List<CustomEntry>(4);
            for (int i = 0; i < paths.Length; i++)
            {
                var theme = AssetDatabase.LoadAssetAtPath<VeneerTheme>(paths[i]);
                if (theme == null)
                {
                    continue;
                }

                Themes.Add(theme);
                if (theme.Custom)
                {
                    // A custom theme belongs to no system and is never the global theme; it is only reachable through Custom / FindCustom.
                    customs.Add(new CustomEntry { Theme = theme, Order = i, Name = theme.ThemeName ?? string.Empty });
                }
                else if (theme.IsGlobal)
                {
                    if (globalTheme == null)
                    {
                        globalTheme = theme;
                    }
                }
                else if (!ByBrand.ContainsKey(theme.BrandName))
                {
                    ByBrand.Add(theme.BrandName, theme);
                }
            }

            // Ordered by name, then by asset path (the order the paths were read in).
            customs.Sort(CompareCustom);
            var sorted = new List<VeneerTheme>(customs.Count);
            for (int i = 0; i < customs.Count; i++)
            {
                sorted.Add(customs[i].Theme);
            }

            customThemes = sorted;

            // While assets are still importing the list may be incomplete; do not trust it for later lookups.
            built = !EditorApplication.isUpdating;
        }

        private static int CompareCustom(CustomEntry a, CustomEntry b)
        {
            int order = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            if (order == 0)
            {
                order = string.CompareOrdinal(a.Name, b.Name);
            }

            return order != 0 ? order : a.Order.CompareTo(b.Order);
        }

        private struct CustomEntry
        {
            public VeneerTheme Theme;
            public int Order;
            public string Name;
        }
    }
}
