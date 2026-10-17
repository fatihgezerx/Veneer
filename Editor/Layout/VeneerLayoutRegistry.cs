using System;
using System.Collections.Generic;
using UnityEditor;

namespace Veneer
{
    /// <summary>
    /// Finds the <see cref="VeneerLayout"/> that styles a script's type. The layouts are read from the project once (lazily) and cached;
    /// <see cref="VeneerLayoutPostprocessor"/> drops the cache when a layout asset is imported, deleted or moved, and a domain reload
    /// starts with an empty cache. A lookup is a dictionary hit, so the generic Inspector can ask for every component it draws.
    /// </summary>
    public static class VeneerLayoutRegistry
    {
        private static readonly List<VeneerLayout> Layouts = new List<VeneerLayout>(8);
        private static readonly Dictionary<Type, VeneerLayout> ByType = new Dictionary<Type, VeneerLayout>(8);

        // Results of Find per asked type, misses included, so a script without a layout costs one lookup.
        private static readonly Dictionary<Type, VeneerLayout> Lookups = new Dictionary<Type, VeneerLayout>(64);

        private static bool built;
        private static bool changePending;

        /// <summary>Raised (on the editor's next idle tick, never during an import) after the set of layouts may have changed.</summary>
        public static event Action Changed;

        /// <summary>Every layout asset of the project, in asset-path order.</summary>
        public static IReadOnlyList<VeneerLayout> All
        {
            get
            {
                EnsureBuilt();
                return Layouts;
            }
        }

        /// <summary>
        /// The layout for <paramref name="type"/>: the one made for exactly that type, otherwise the one of the nearest base type; null when
        /// there is none. A layout is matched through <see cref="VeneerLayout.ResolveType"/>. When two layouts name the same type the one
        /// with the first asset path wins.
        /// </summary>
        public static VeneerLayout Find(Type type)
        {
            if (type == null)
            {
                return null;
            }

            for (int attempt = 0; attempt < 2; attempt++)
            {
                EnsureBuilt();
                if (!Lookups.TryGetValue(type, out VeneerLayout found))
                {
                    found = Resolve(type);
                    Lookups[type] = found;
                }

                // A live layout, or a remembered miss (a real C# null). Anything else is a destroyed asset: rebuild once.
                if (found != null || ReferenceEquals(found, null))
                {
                    return found;
                }

                built = false;
            }

            return null;
        }

        /// <summary>
        /// Drops the cache so the next lookup reads the project again, and raises <see cref="Changed"/> once. Called by the postprocessor;
        /// call it yourself after changing which script a layout is bound to.
        /// </summary>
        public static void Invalidate()
        {
            built = false;
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
            Action handler = Changed;
            if (handler != null)
            {
                handler();
            }
        }

        private static VeneerLayout Resolve(Type type)
        {
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                if (ByType.TryGetValue(t, out VeneerLayout layout) && layout != null)
                {
                    return layout;
                }
            }

            return null;
        }

        private static void EnsureBuilt()
        {
            if (built)
            {
                return;
            }

            Layouts.Clear();
            ByType.Clear();
            Lookups.Clear();

            string[] guids = AssetDatabase.FindAssets("t:VeneerLayout");
            var paths = new string[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
            }

            // Sorted so that two layouts for one type always give the same winner.
            Array.Sort(paths, StringComparer.Ordinal);
            for (int i = 0; i < paths.Length; i++)
            {
                var layout = AssetDatabase.LoadAssetAtPath<VeneerLayout>(paths[i]);
                if (layout == null)
                {
                    continue;
                }

                Layouts.Add(layout);
                Type type = layout.ResolveType();
                if (type != null && !ByType.ContainsKey(type))
                {
                    ByType.Add(type, layout);
                }
            }

            // While assets are still importing the list may be incomplete; do not trust it for later lookups.
            built = !EditorApplication.isUpdating;
        }
    }
}
