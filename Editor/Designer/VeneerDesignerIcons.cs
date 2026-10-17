using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// The kit icon picker of the Inspector Designer: a search box and a wrapping grid of every glyph in the kit's <c>Icons</c> folder.
    /// One instance is built once per window (about a hundred small buttons) and re-used for every icon field: <see cref="Show"/> says
    /// which icon is current and what to call when one is clicked.
    /// </summary>
    internal sealed class VeneerDesignerIcons
    {
        private static string[] names;

        private readonly List<Button> cells = new List<Button>(128);
        private readonly TextField search;
        private Action<string> chosen;
        private string current;

        /// <summary>Builds the picker (search box plus grid).</summary>
        /// <param name="brand">Brand for icon lookup; may be null.</param>
        public VeneerDesignerIcons(VeneerBrand brand)
        {
            Element = new VisualElement();
            Element.AddToClassList("vnd-icons");
            Element.Add(VeneerStyle.SearchField(ApplyFilter, out search, "Search icons", brand));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("vnd-icons__scroll");
            var grid = new VisualElement();
            grid.AddToClassList("vnd-icons__grid");
            scroll.Add(grid);
            Element.Add(scroll);

            IReadOnlyList<string> all = Names;
            for (int i = 0; i < all.Count; i++)
            {
                string iconName = all[i];
                Button cell = VeneerStyle.IconButton(iconName, iconName, () => Pick(iconName), brand);
                cell.AddToClassList("vnd-icon-cell");
                grid.Add(cell);
                cells.Add(cell);
            }
        }

        /// <summary>The element to put into a panel.</summary>
        public VisualElement Element { get; }

        /// <summary>The names (no extension) of the icons in the kit's <c>Icons</c> folder, sorted; cached until <see cref="Reload"/>.</summary>
        public static IReadOnlyList<string> Names
        {
            get
            {
                if (names != null)
                {
                    return names;
                }

                string kit = VeneerStyle.KitFolder;
                if (kit == null)
                {
                    return Array.Empty<string>();
                }

                string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { kit + "/Icons" });
                var list = new List<string>(guids.Length);
                for (int i = 0; i < guids.Length; i++)
                {
                    list.Add(Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guids[i])));
                }

                list.Sort(StringComparer.Ordinal);
                names = list.ToArray();
                return names;
            }
        }

        /// <summary>Forgets the cached icon names so the next picker sees the folder as it is now.</summary>
        public static void Reload()
        {
            names = null;
        }

        /// <summary>Marks <paramref name="currentIcon"/> and sets what happens on a click.</summary>
        /// <param name="currentIcon">The icon in use; may be empty.</param>
        /// <param name="onChosen">Called with the icon name that was clicked.</param>
        public void Show(string currentIcon, Action<string> onChosen)
        {
            current = currentIcon;
            chosen = onChosen;
            IReadOnlyList<string> all = Names;
            for (int i = 0; i < cells.Count && i < all.Count; i++)
            {
                cells[i].EnableInClassList("vn-icon-button--on", string.Equals(all[i], current, StringComparison.Ordinal));
            }
        }

        private void Pick(string iconName)
        {
            current = iconName;
            if (chosen != null)
            {
                chosen(iconName);
            }
        }

        private void ApplyFilter(string text)
        {
            IReadOnlyList<string> all = Names;
            bool showAll = string.IsNullOrEmpty(text);
            for (int i = 0; i < cells.Count && i < all.Count; i++)
            {
                VeneerStyle.SetDisplay(cells[i], showAll || all[i].IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }
    }
}
