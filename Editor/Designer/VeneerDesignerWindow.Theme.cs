using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    // The theme choice of the script: which look (default, a system's, or a custom theme) the Inspector uses. The Theme card sits first in the
    // Header pane; the pictures cards (Banner, Icon) can take their picture from the chosen look. Every write goes through the layout's SerializedObject.
    public sealed partial class VeneerDesignerWindow
    {
        private const string DefaultThemeLabel = "Default (Veneer)";
        private const string ThemeEditorMenu = "Tools/Veneer/Theme Editor";

        // One entry of the theme dropdown, in the order of the dropdown's choices.
        private sealed class ThemeChoice
        {
            public string Label;
            public VeneerTheme Theme;
            public string BrandName;
            public bool Missing;
        }

        private readonly List<ThemeChoice> themeChoices = new List<ThemeChoice>(16);

        // Re-evaluates the rows that depend on the theme choice and the "Theme / Own" switches (set while the Header pane is built).
        private Action lookRefresh;

        // The set of themes changed (a theme was made, renamed or deleted): the dropdown's choices and the look-dependent rows are rebuilt.
        private void OnThemesChanged()
        {
            if (RootElement == null || layout == null || layoutSo == null)
            {
                return;
            }

            if (selKind == NodeKind.Header)
            {
                BuildProperties();
            }

            SchedulePreview();
        }

        private void BuildThemeCard()
        {
            VisualElement card = VeneerStyle.Card("Theme", "target", Brand);
            SerializedProperty themeProperty = layoutSo.FindProperty("Theme");
            SerializedProperty brandProperty = layoutSo.FindProperty("ThemeBrand");
            if (themeProperty == null || brandProperty == null)
            {
                card.Add(VeneerStyle.Hint("The layout has no theme fields.", "warning", Brand));
                propsBody.Add(card);
                return;
            }

            UnityEngine.Object currentTheme = themeProperty.objectReferenceValue;
            bool themeDeleted = currentTheme == null && themeProperty.objectReferenceInstanceIDValue != 0;
            string currentBrand = brandProperty.stringValue ?? string.Empty;

            var labels = new List<string>(16);
            themeChoices.Clear();
            AddThemeChoice(labels, DefaultThemeLabel, null, null, false);
            IReadOnlyList<VeneerBrand> brands = VeneerBrandCatalog.All;
            for (int i = 0; i < brands.Count; i++)
            {
                AddThemeChoice(labels, "System: " + brands[i].Name, null, brands[i].Name, false);
            }

            IReadOnlyList<VeneerTheme> customs = VeneerThemeRegistry.Custom;
            for (int i = 0; i < customs.Count; i++)
            {
                if (customs[i] != null)
                {
                    AddThemeChoice(labels, "Theme: " + ThemeLabel(customs[i]), customs[i], null, false);
                }
            }

            // The current choice: a theme wins over a system, a system over the default.
            int index = 0;
            if (currentTheme != null)
            {
                index = IndexOfTheme(currentTheme);
                if (index < 0)
                {
                    // A live theme asset that is not in the custom list (for example not marked Custom): still shown, so nothing is lost.
                    AddThemeChoice(labels, "Theme: " + ThemeLabel(currentTheme as VeneerTheme), currentTheme as VeneerTheme, null, false);
                    index = themeChoices.Count - 1;
                }
            }
            else if (themeDeleted)
            {
                AddThemeChoice(labels, "Missing: deleted theme", null, null, true);
                index = themeChoices.Count - 1;
            }
            else if (currentBrand.Length > 0)
            {
                index = IndexOfBrand(currentBrand);
                if (index < 0)
                {
                    AddThemeChoice(labels, "Missing: " + currentBrand, null, null, true);
                    index = themeChoices.Count - 1;
                }
            }

            var dropdown = new DropdownField(labels, index);
            dropdown.AddToClassList("vnd-control--capped");
            var dot = new VisualElement();
            dot.style.width = 12f;
            dot.style.height = 12f;
            dot.style.flexShrink = 0f;
            dot.style.marginRight = 6f;
            dot.style.alignSelf = Align.Center;
            dot.style.borderTopLeftRadius = 6f;
            dot.style.borderTopRightRadius = 6f;
            dot.style.borderBottomLeftRadius = 6f;
            dot.style.borderBottomRightRadius = 6f;
            dot.tooltip = "The accent colour of this look.";
            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.flexGrow = 1f;
            line.Add(dot);
            dropdown.style.flexGrow = 1f;
            line.Add(dropdown);
            card.Add(VeneerStyle.FieldRow(
                "Theme", line, "The look this script's Inspector uses: the default, the look of a system (with its logo and banner), or a custom theme from the Theme Editor.", out _));

            Label effect = VeneerStyle.MutedLabel("Colours, corners, spacing, tabs and pictures of this look are used in the Inspector.");
            effect.AddToClassList("vnd-note");
            card.Add(effect);
            Label systemNote = VeneerStyle.MutedLabel("A system look also uses that system's logo and banner.");
            systemNote.AddToClassList("vnd-note");
            card.Add(systemNote);
            var hintBox = new VisualElement();
            card.Add(hintBox);

            Action<ThemeChoice> show = choice =>
            {
                dot.style.backgroundColor = choice.Missing ? VeneerBrand.DefaultAccent : AccentOfLook(choice.Theme, choice.BrandName);
                VeneerStyle.SetDisplay(systemNote, !string.IsNullOrEmpty(choice.BrandName));
                hintBox.Clear();
                if (choice.Missing)
                {
                    string name = choice.Label.StartsWith("Missing: ", StringComparison.Ordinal) ? choice.Label.Substring(9) : choice.Label;
                    hintBox.Add(VeneerStyle.Hint("'" + name + "' no longer exists. The default look is used until you choose another one.", "warning", Brand));
                }
            };
            show(themeChoices[index]);

            dropdown.RegisterValueChangedCallback(evt =>
            {
                int selected = dropdown.index;
                if (selected < 0 || selected >= themeChoices.Count)
                {
                    return;
                }

                ThemeChoice choice = themeChoices[selected];
                if (choice.Missing)
                {
                    return;
                }

                SerializedProperty themeWrite = layoutSo.FindProperty("Theme");
                SerializedProperty brandWrite = layoutSo.FindProperty("ThemeBrand");
                if (themeWrite == null || brandWrite == null)
                {
                    return;
                }

                themeWrite.objectReferenceValue = choice.Theme;
                brandWrite.stringValue = choice.BrandName ?? string.Empty;
                layoutSo.ApplyModifiedProperties();
                show(choice);
                if (lookRefresh != null)
                {
                    lookRefresh();
                }

                if (bannerRefresh != null)
                {
                    bannerRefresh();
                }
            });

            Button open = VeneerStyle.Button(
                "Open Theme Editor", "target", "Opens Tools > Veneer > Theme Editor, where themes are made and edited.", () => EditorApplication.ExecuteMenuItem(ThemeEditorMenu), false, Brand);
            open.style.marginTop = 4f;
            card.Add(open);
            propsBody.Add(card);
        }

        private void AddThemeChoice(List<string> labels, string label, VeneerTheme theme, string brandName, bool missing)
        {
            // Two entries with the same text would be told apart by index only; the suffix keeps them readable too.
            string unique = label;
            int copy = 2;
            while (labels.Contains(unique))
            {
                unique = label + " (" + copy + ")";
                copy++;
            }

            labels.Add(unique);
            themeChoices.Add(new ThemeChoice { Label = unique, Theme = theme, BrandName = brandName, Missing = missing });
        }

        private int IndexOfTheme(UnityEngine.Object theme)
        {
            for (int i = 0; i < themeChoices.Count; i++)
            {
                if (themeChoices[i].Theme != null && themeChoices[i].Theme == theme)
                {
                    return i;
                }
            }

            return -1;
        }

        private int IndexOfBrand(string brandName)
        {
            for (int i = 0; i < themeChoices.Count; i++)
            {
                if (string.Equals(themeChoices[i].BrandName, brandName, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string ThemeLabel(VeneerTheme theme)
        {
            if (theme == null)
            {
                return string.Empty;
            }

            return string.IsNullOrEmpty(theme.ThemeName) ? theme.name : theme.ThemeName;
        }

        // The accent of a look for the colour dot: a custom theme's own (else the global theme's, else the kit's), a system's merged look, or the default look.
        private static Color AccentOfLook(VeneerTheme theme, string brandName)
        {
            if (theme != null)
            {
                if (theme.OverrideAccent)
                {
                    return theme.Accent;
                }

                VeneerTheme global = VeneerThemeRegistry.Global;
                return global != null && global.OverrideAccent ? global.Accent : VeneerBrand.DefaultAccent;
            }

            if (!string.IsNullOrEmpty(brandName))
            {
                VeneerBrand brand = VeneerBrandCatalog.Find(brandName);
                if (brand == null)
                {
                    return VeneerBrand.DefaultAccent;
                }

                VeneerThemeEffective merged = VeneerThemeRegistry.Effective(brand);
                return merged.HasAccent ? merged.Accent : brand.Accent;
            }

            VeneerThemeEffective basic = VeneerThemeRegistry.Effective(null);
            return basic.HasAccent ? basic.Accent : VeneerBrand.DefaultAccent;
        }

        // The name of the look the layout uses now, for captions.
        private string LookName()
        {
            if (layout == null)
            {
                return "Veneer";
            }

            if (layout.Theme != null)
            {
                return ThemeLabel(layout.Theme);
            }

            return string.IsNullOrEmpty(layout.ThemeBrand) ? "Veneer" : layout.ThemeBrand;
        }

        // A two-button choice bound to a bool: the first button is "true". The change callback gets the new value (the model is already written).
        private VisualElement BoolChoice(string first, string second, bool firstOn, string path, string firstTip, string secondTip, Action<bool> changed)
        {
            VisualElement group = VeneerStyle.SegmentedIcons(new string[2], new[] { first, second }, firstOn ? 0 : 1, index =>
            {
                SerializedProperty property = layoutSo.FindProperty(path);
                if (property == null)
                {
                    return;
                }

                property.boolValue = index == 0;
                layoutSo.ApplyModifiedProperties();
                changed(index == 0);
            }, Brand);
            if (group.childCount > 1)
            {
                group[0].tooltip = firstTip;
                group[1].tooltip = secondTip;
            }

            return group;
        }

        // A thumbnail of the chosen look's logo (40 px square) or banner (60 px high) with a caption; refresh re-reads the picture.
        private VisualElement ThemePictureView(bool logo, out Action refresh)
        {
            var box = new VisualElement();
            var image = new Image { scaleMode = ScaleMode.ScaleToFit };
            image.style.alignSelf = Align.FlexStart;
            image.style.marginBottom = 2f;
            Label caption = VeneerStyle.MutedLabel(string.Empty);
            caption.AddToClassList("vnd-note");
            // The banner explains a missing picture with a Hint box; the logo with a plain muted line.
            VisualElement none = logo ? (VisualElement)VeneerStyle.MutedLabel("This look has no logo") : VeneerStyle.Hint("This look has no banner", "info", Brand);
            box.Add(image);
            box.Add(caption);
            box.Add(none);
            refresh = () =>
            {
                Texture2D texture = layout == null ? null : (logo ? VeneerLayoutTheme.ThemeLogo(layout) : VeneerLayoutTheme.ThemeBanner(layout));
                bool has = texture != null;
                image.image = texture;
                if (has)
                {
                    float height = logo ? 40f : 60f;
                    float aspect = texture.height > 0 ? (float)texture.width / texture.height : 1f;
                    image.style.height = height;
                    image.style.width = logo ? 40f : Mathf.Clamp(height * aspect, 30f, 300f);
                    caption.text = (logo ? "Logo of " : "Banner of ") + LookName();
                }

                VeneerStyle.SetDisplay(image, has);
                VeneerStyle.SetDisplay(caption, has);
                VeneerStyle.SetDisplay(none, !has);
            };
            refresh();
            return box;
        }
    }
}
