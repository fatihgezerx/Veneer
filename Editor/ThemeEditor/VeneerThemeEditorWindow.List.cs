using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    // Left pane, three groups: the global theme (one entry), the systems (one row per brand of the catalog, the ready-made looks) and the
    // custom themes (reusable looks the user made, with a "New theme" button on the group header). The rows are built once per catalog or
    // theme-set change; selecting, searching and refreshing the markers only change classes, display and colours of those rows.
    public sealed partial class VeneerThemeEditorWindow
    {
        private const string GlobalName = "Global (all systems)";
        private const int GroupGlobal = 0;
        private const int GroupSystems = 1;
        private const int GroupThemes = 2;

        private struct ListRow
        {
            public string Key;
            public string Search;
            public int Group;
            public VeneerBrand Source;
            public VeneerTheme Custom;
            public VisualElement Element;
            public Label Label;
            public VisualElement Logo;
            public VisualElement Fallback;
            public VisualElement Dot;
        }

        private struct ListHeader
        {
            public int Group;
            public VisualElement Element;
        }

        private readonly List<VeneerBrand> brands = new List<VeneerBrand>(8);
        private readonly List<ListRow> rows = new List<ListRow>(8);
        private readonly List<ListHeader> headers = new List<ListHeader>(3);
        private readonly List<VeneerTheme> customThemes = new List<VeneerTheme>(8);
        private ScrollView listScroll;
        private VisualElement themesEmpty;
        private string filter = string.Empty;

        private VisualElement BuildListPane()
        {
            var pane = new VisualElement();
            pane.AddToClassList("vnte-pane");
            pane.AddToClassList("vnte-pane--side");
            VisualElement title = VeneerStyle.Section("Themes");
            title.AddToClassList("vnte-pane__title");
            pane.Add(title);

            VisualElement search = VeneerStyle.SearchField(ApplyFilter, out TextField _, "Search systems and themes", Brand);
            search.AddToClassList("vnte-search");
            pane.Add(search);

            listScroll = NewPaneScroll("vnte-scroll");
            pane.Add(listScroll);
            return pane;
        }

        private void ResetListState()
        {
            rows.Clear();
            headers.Clear();
            listScroll = null;
            themesEmpty = null;
            filter = string.Empty;
        }

        // The custom themes of the registry, sorted by their shown name (a copy: the registry's own list is never touched).
        private void CollectCustomThemes()
        {
            customThemes.Clear();
            IReadOnlyList<VeneerTheme> all = VeneerThemeRegistry.Custom;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null)
                {
                    customThemes.Add(all[i]);
                }
            }

            customThemes.Sort(CompareThemeNames);
        }

        private static int CompareThemeNames(VeneerTheme a, VeneerTheme b)
        {
            int order = string.Compare(DisplayName(a), DisplayName(b), StringComparison.OrdinalIgnoreCase);
            return order != 0 ? order : string.CompareOrdinal(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b));
        }

        /// <summary>The name a custom theme is shown with: its <see cref="VeneerTheme.ThemeName"/>, or its asset name when that is empty.</summary>
        private static string DisplayName(VeneerTheme custom)
        {
            return string.IsNullOrEmpty(custom.ThemeName) ? custom.name : custom.ThemeName;
        }

        // Builds the rows (called when the tree is built, after the catalog was read again and when the set of custom themes changed).
        private void PopulateList()
        {
            if (listScroll == null)
            {
                return;
            }

            listScroll.Clear();
            rows.Clear();
            headers.Clear();
            CollectCustomThemes();

            AddHeader(GroupGlobal, GlobalName, false);
            AddRow(GroupGlobal, string.Empty, GlobalName, null, null, "globe");

            AddHeader(GroupSystems, "Systems", false);
            for (int i = 0; i < brands.Count; i++)
            {
                AddRow(GroupSystems, brands[i].Name, brands[i].Name, brands[i], null, "cube");
            }

            AddHeader(GroupThemes, "Themes", true);
            for (int i = 0; i < customThemes.Count; i++)
            {
                AddRow(GroupThemes, CustomKey(customThemes[i]), DisplayName(customThemes[i]), null, customThemes[i], "preset");
            }

            themesEmpty = VeneerStyle.MutedLabel("No themes yet. Press New theme to make one from the look that is selected.");
            themesEmpty.AddToClassList("vnte-note");
            listScroll.Add(themesEmpty);

            ApplyFilter(filter);
        }

        // True when the rows of the custom themes no longer match the registry (a theme was made, deleted, renamed or moved).
        private bool CustomRowsChanged()
        {
            CollectCustomThemes();
            int index = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Group != GroupThemes)
                {
                    continue;
                }

                if (index >= customThemes.Count || !string.Equals(rows[i].Key, CustomKey(customThemes[index]), StringComparison.Ordinal))
                {
                    return true;
                }

                index++;
            }

            return index != customThemes.Count;
        }

        // Makes the list match the registry and the selection point at something that exists. True when the list was rebuilt or the
        // selection fell back, so the caller must refresh every pane.
        private bool SyncList()
        {
            if (RootElement == null || listScroll == null)
            {
                return false;
            }

            bool changed = false;
            if (CustomRowsChanged())
            {
                PopulateList();
                changed = true;
            }

            if (IsCustomSelected && FindCustomByKey(selectedBrand) == null)
            {
                selectedBrand = string.Empty;
                changed = true;
            }

            return changed;
        }

        private void AddHeader(int group, string text, bool withButton)
        {
            var header = new VisualElement();
            header.AddToClassList("vnte-group");
            Label label = new Label(text.ToUpperInvariant());
            label.AddToClassList("vnte-group__label");
            label.pickingMode = PickingMode.Ignore;
            header.Add(label);
            if (withButton)
            {
                Button add = VeneerStyle.Button("New theme", "plus", "Create a new theme from the selected look", CreateCustomTheme, false, Brand);
                add.AddToClassList("vnte-group__button");
                header.Add(add);
            }

            listScroll.Add(header);
            headers.Add(new ListHeader { Group = group, Element = header });
        }

        private void AddRow(int group, string key, string name, VeneerBrand source, VeneerTheme custom, string fallbackIcon)
        {
            var element = new VisualElement();
            element.AddToClassList("vnte-row");
            if (custom != null)
            {
                element.tooltip = "A theme of your own. Choose it by name for any script in the Inspector Designer.";
            }
            else
            {
                element.tooltip = source == null ? "Applies to every system; a system's own theme wins over it." : source.Subtitle;
            }

            var logo = new VisualElement();
            logo.AddToClassList("vnte-row__logo");
            logo.pickingMode = PickingMode.Ignore;
            element.Add(logo);
            VisualElement fallback = VeneerStyle.IconElement(fallbackIcon, null, Brand);
            element.Add(fallback);

            Label label = VeneerStyle.EllipsisLabel(name);
            label.AddToClassList("vnte-row__label");
            element.Add(label);

            var dot = new VisualElement();
            dot.AddToClassList("vnte-row__dot");
            dot.pickingMode = PickingMode.Ignore;
            element.Add(dot);

            string captured = key;
            element.RegisterCallback<ClickEvent>(evt => SelectKey(captured));
            listScroll.Add(element);

            string search = name;
            if (source == null && custom == null)
            {
                search = name + " global";
            }
            else if (custom != null)
            {
                search = name + " theme";
            }

            rows.Add(new ListRow
            {
                Key = key,
                Search = search,
                Group = group,
                Source = source,
                Custom = custom,
                Element = element,
                Label = label,
                Logo = logo,
                Fallback = fallback,
                Dot = dot,
            });
        }

        private void ApplyFilter(string text)
        {
            filter = text ?? string.Empty;
            bool filtering = filter.Length > 0;
            int[] visibleInGroup = new int[3];
            for (int i = 0; i < rows.Count; i++)
            {
                bool visible = !filtering || rows[i].Search.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                VeneerStyle.SetDisplay(rows[i].Element, visible);
                if (visible)
                {
                    visibleInGroup[rows[i].Group]++;
                }
            }

            // A group header is hidden while a search leaves nothing in its group; the Themes header keeps its button.
            for (int i = 0; i < headers.Count; i++)
            {
                int group = headers[i].Group;
                bool show = !filtering || visibleInGroup[group] > 0 || group == GroupThemes;
                VeneerStyle.SetDisplay(headers[i].Element, show);
            }

            if (themesEmpty != null)
            {
                VeneerStyle.SetDisplay(themesEmpty, !filtering && customThemes.Count == 0);
            }
        }

        // Highlights the selected row.
        private void RefreshSelectionMarks()
        {
            string key = selectedBrand ?? string.Empty;
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].Element.EnableInClassList("vnte-row--selected", string.Equals(rows[i].Key, key, StringComparison.Ordinal));
            }
        }

        // Updates the accent dot, the logo and the name of every row from the registry.
        private void RefreshMarks()
        {
            VeneerTheme global = VeneerThemeRegistry.Global;
            for (int i = 0; i < rows.Count; i++)
            {
                ListRow row = rows[i];
                bool isCustom = row.Custom != null;
                VeneerTheme own;
                if (isCustom)
                {
                    own = row.Custom;
                }
                else
                {
                    own = row.Source == null ? global : VeneerThemeRegistry.Find(row.Key);
                }

                if (isCustom && row.Custom != null)
                {
                    string shown = DisplayName(row.Custom);
                    if (!string.Equals(row.Label.text, shown, StringComparison.Ordinal))
                    {
                        row.Label.text = shown;
                    }
                }

                // The dot shows what the accent will be: the theme's own, else the global one, else the brand's colour.
                Color accent = row.Source != null ? row.Source.Accent : VeneerBrand.DefaultAccent;
                if (own != null && own.OverrideAccent)
                {
                    accent = own.Accent;
                }
                else if (global != null && global.OverrideAccent)
                {
                    accent = global.Accent;
                }

                accent.a = 1f;
                row.Dot.style.backgroundColor = accent;

                Texture2D logo = row.Source != null ? row.Source.Logo : null;
                bool hasLogo = logo != null;
                if (hasLogo)
                {
                    row.Logo.style.backgroundImage = new StyleBackground(logo);
                }

                VeneerStyle.SetDisplay(row.Logo, hasLogo);
                VeneerStyle.SetDisplay(row.Fallback, !hasLogo);
            }
        }
    }
}
