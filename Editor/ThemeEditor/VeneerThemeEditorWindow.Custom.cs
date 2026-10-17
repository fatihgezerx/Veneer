using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    // Custom themes: reusable, named looks that belong to no system. "New theme" makes one as a full snapshot of the selected look (every
    // override ticked), renaming keeps the name and the asset file unique, and deleting sends the layouts that used it back to the default look.
    public sealed partial class VeneerThemeEditorWindow
    {
        private const string DefaultThemeName = "New Theme";

        private TextField nameField;
        private bool focusNamePending;

        private static string CustomKey(VeneerTheme custom)
        {
            return CustomPrefix + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(custom));
        }

        // The custom theme a selection key points at (a prefix plus a GUID); null when the asset is gone or is not a custom theme.
        private static VeneerTheme FindCustomByKey(string key)
        {
            if (string.IsNullOrEmpty(key) || !key.StartsWith(CustomPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            string path = AssetDatabase.GUIDToAssetPath(key.Substring(CustomPrefix.Length));
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            VeneerTheme found = AssetDatabase.LoadAssetAtPath<VeneerTheme>(path);
            return found != null && found.Custom ? found : null;
        }

        // A theme name that no other custom theme has and whose asset file (and sheet) is free; <paramref name="except"/> is the theme being renamed.
        private static string UniqueThemeName(string desired, VeneerTheme except)
        {
            string exceptPath = except != null ? AssetDatabase.GetAssetPath(except) : string.Empty;
            IReadOnlyList<VeneerTheme> all = VeneerThemeRegistry.Custom;
            for (int attempt = 1; attempt < 1000; attempt++)
            {
                string candidate = attempt == 1 ? desired : desired + " " + attempt;
                bool taken = false;
                for (int i = 0; i < all.Count && !taken; i++)
                {
                    VeneerTheme other = all[i];
                    taken = other != null && other != except && string.Equals(DisplayName(other), candidate, StringComparison.OrdinalIgnoreCase);
                }

                if (!taken)
                {
                    string path = ThemeFolder + "/" + FileBase(candidate) + ".asset";
                    string sheet = ThemeFolder + "/" + FileBase(candidate) + ".uss";
                    taken = PathTaken(path, exceptPath) || PathTaken(sheet, exceptPath.Length > 0 ? exceptPath.Substring(0, exceptPath.Length - ".asset".Length) + ".uss" : string.Empty);
                }

                if (!taken)
                {
                    return candidate;
                }
            }

            return desired + " " + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        private static bool PathTaken(string path, string ownPath)
        {
            if (string.Equals(path, ownPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path));
        }

        // The "New theme" button: a custom theme that is a full snapshot of the look the selected entry has now (system, Global or another
        // custom theme): every override ticked with the value the properties pane shows, and the pictures the entry really uses.
        private void CreateCustomTheme()
        {
            if (RootElement == null)
            {
                return;
            }

            FlushSave();
            VeneerTheme parent = VeneerThemeInherited.Parent(IsGlobalSelected);
            VeneerBrand brand = IsSystemSelected ? selected : null;
            VeneerTheme own = theme;
            bool hasOwn = own != null;

            if (!AssetDatabase.IsValidFolder(ThemeFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Veneer Themes");
            }

            string themeName = UniqueThemeName(DefaultThemeName, null);
            string path = AssetDatabase.GenerateUniqueAssetPath(ThemeFolder + "/" + FileBase(themeName) + ".asset");
            var created = CreateInstance<VeneerTheme>();
            created.Custom = true;
            created.BrandName = string.Empty;
            created.ThemeName = themeName;

            created.OverrideAccent = true;
            created.Accent = hasOwn && own.OverrideAccent ? own.Accent : VeneerThemeInherited.Accent(brand, parent);
            created.OverrideCardColor = true;
            created.CardColor = hasOwn && own.OverrideCardColor ? own.CardColor : VeneerThemeInherited.CardColor(parent);
            created.OverrideCardBorder = true;
            created.CardBorder = hasOwn && own.OverrideCardBorder ? own.CardBorder : VeneerThemeInherited.CardBorder(parent);
            created.OverrideText = true;
            created.Text = hasOwn && own.OverrideText ? own.Text : VeneerThemeInherited.Text(parent);
            created.OverrideTextMuted = true;
            created.TextMuted = hasOwn && own.OverrideTextMuted ? own.TextMuted : VeneerThemeInherited.TextMuted(parent);
            created.OverrideRadius = true;
            created.RadiusScale = hasOwn && own.OverrideRadius ? own.RadiusScale : VeneerThemeInherited.RadiusScale(parent);
            created.OverrideDensity = true;
            created.DensityScale = hasOwn && own.OverrideDensity ? own.DensityScale : VeneerThemeInherited.DensityScale(parent);
            created.OverrideTabStyle = true;
            created.TabStyle = hasOwn && own.OverrideTabStyle ? own.TabStyle : VeneerThemeInherited.TabStyle(parent);
            created.OverrideMotion = true;
            created.Motion = hasOwn && own.OverrideMotion ? own.Motion : VeneerThemeInherited.Motion(parent);

            // The pictures the entry really uses: its own theme's, else the global theme's, else the system's own; none when it has none.
            Texture2D logo = null;
            Texture2D banner = null;
            if (hasOwn)
            {
                logo = own.Logo;
                banner = own.Banner;
            }

            if (logo == null && parent != null)
            {
                logo = parent.Logo;
            }

            if (banner == null && parent != null)
            {
                banner = parent.Banner;
            }

            if (logo == null && brand != null)
            {
                logo = brand.Logo;
            }

            if (banner == null && brand != null)
            {
                banner = brand.Banner;
            }

            created.Logo = logo;
            created.Banner = banner;

            AssetDatabase.CreateAsset(created, path);
            Undo.RegisterCreatedObjectUndo(created, "Create Veneer theme");
            VeneerThemeRegistry.Invalidate();

            selectedBrand = CustomKey(created);
            focusNamePending = true;
            PopulateList();
            RefreshAll();
            MarkDirty();
        }

        // Called after the name field committed a value (it is a delayed field: Enter or leaving it).
        private void OnNameCommitted(string newValue)
        {
            if (theme == null || nameField == null || !theme.Custom)
            {
                return;
            }

            string current = DisplayName(theme);
            string wanted = newValue == null ? string.Empty : newValue.Trim();
            if (wanted.Length == 0)
            {
                // An empty name is rejected: the old one stays.
                nameField.SetValueWithoutNotify(current);
                return;
            }

            string unique = UniqueThemeName(wanted, theme);
            nameField.SetValueWithoutNotify(unique);
            if (string.Equals(unique, current, StringComparison.Ordinal))
            {
                return;
            }

            if (BeginEdit())
            {
                SerializedProperty property = themeSo.FindProperty("ThemeName");
                if (property != null)
                {
                    property.stringValue = unique;
                }

                EndEdit();
            }

            RenameAssets(theme, unique);
            VeneerThemeRegistry.Invalidate();
            if (SyncList())
            {
                RefreshAll();
                return;
            }

            RefreshSelectionMarks();
            RefreshMarks();
        }

        // Gives the asset (and its generated sheet) the file name of the theme name; the GUID stays, so nothing that points at it breaks.
        private static void RenameAssets(VeneerTheme target, string themeName)
        {
            string assetPath = AssetDatabase.GetAssetPath(target);
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            string fileName = FileBase(themeName);
            string sheetPath = VeneerThemeSheet.SheetPath(target);
            if (!string.IsNullOrEmpty(sheetPath) && !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(sheetPath)))
            {
                AssetDatabase.RenameAsset(sheetPath, fileName);
            }

            string error = AssetDatabase.RenameAsset(assetPath, fileName);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning("Veneer Theme Editor: could not rename the theme asset: " + error);
            }
        }

        // Layouts (Inspector Designer) that currently use this theme.
        private static int CountLayoutsUsing(VeneerTheme target)
        {
            int count = 0;
            IReadOnlyList<VeneerLayout> layouts = VeneerLayoutRegistry.All;
            for (int i = 0; i < layouts.Count; i++)
            {
                if (layouts[i] != null && layouts[i].Theme == target)
                {
                    count++;
                }
            }

            return count;
        }

        // The "Delete theme" button of a custom theme: asks (saying how many layouts use it), sends those layouts back to the default
        // look and deletes the asset with its sheet.
        private void DeleteCustomTheme()
        {
            if (theme == null || !theme.Custom)
            {
                return;
            }

            VeneerTheme target = theme;
            int used = CountLayoutsUsing(target);
            string usage = used == 0
                ? "No layout uses it."
                : used == 1 ? "1 layout uses it and will go back to the default look." : used + " layouts use it and will go back to the default look.";
            string message = "Delete the theme \"" + DisplayName(target) + "\" (" + AssetDatabase.GetAssetPath(target) + ")?\n\n" + usage
                + "\n\nThis cannot be undone.";
            if (!EditorUtility.DisplayDialog("Delete theme", message, "Delete", "Cancel"))
            {
                return;
            }

            if (used > 0)
            {
                IReadOnlyList<VeneerLayout> layouts = VeneerLayoutRegistry.All;
                for (int i = 0; i < layouts.Count; i++)
                {
                    VeneerLayout layout = layouts[i];
                    if (layout == null || layout.Theme != target)
                    {
                        continue;
                    }

                    var so = new SerializedObject(layout);
                    SerializedProperty property = so.FindProperty("Theme");
                    if (property != null)
                    {
                        property.objectReferenceValue = null;
                        so.ApplyModifiedProperties();
                        AssetDatabase.SaveAssetIfDirty(layout);
                    }

                    so.Dispose();
                }
            }

            DeleteThemeAsset(target);
            selectedBrand = string.Empty;
            PopulateList();
            RefreshAll();
        }
    }
}
