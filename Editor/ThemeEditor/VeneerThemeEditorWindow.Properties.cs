using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    // Right pane: the settings of the selected entry. Every setting is one block: an Override checkbox, the label and the control. While
    // the box is off the control is greyed and shows the look the setting INHERITS (the global theme, else the system's own brand, else
    // the kit; see VeneerThemeInherited); ticking the box copies that shown value into the asset, so editing starts from the current look.
    // A system or Global always shows all its controls: its theme asset is made silently when the first override is ticked or a picture is
    // chosen. A custom theme adds a name field on top and a Delete theme button at the bottom.
    // The controls are not bound: each one writes through the theme's SerializedObject (Undo and dirty marking come from Unity) and calls
    // MarkDirty, so the sample follows and the save is scheduled. The pane is never rebuilt by an edit made through one of its controls
    // (that would drop the focus and close an open colour picker).
    public sealed partial class VeneerThemeEditorWindow
    {
        // The Override* flags of VeneerTheme, in the order of the rows (used by Reset all).
        private static readonly string[] OverridePaths =
        {
            "OverrideAccent", "OverrideCardColor", "OverrideCardBorder", "OverrideText", "OverrideTextMuted",
            "OverrideRadius", "OverrideDensity", "OverrideTabStyle", "OverrideMotion",
        };

        private static readonly string[] TabStyleNames = { "Underline", "Pill", "Boxed" };

        // The order of VeneerMotion: Off, Subtle, Normal, Lively.
        private static readonly string[] MotionNames = { "Off", "Subtle", "Normal", "Lively" };

        private static readonly string[] MotionTips =
        {
            "Off: no animation at all.",
            "Subtle: short and small movements.",
            "Normal: the kit's own motion.",
            "Lively: longer, bigger and springier movements.",
        };

        private const float RadiusMin = 0f;
        private const float RadiusMax = 2f;
        private const float DensityMin = 0.6f;
        private const float DensityMax = 1.5f;
        private const float ScaleStep = 0.05f;

        private ScrollView propsScroll;
        private VisualElement propsBody;
        private Label propsTitle;
        private Button resetButton;

        private VisualElement BuildPropertiesPane()
        {
            var pane = new VisualElement();
            pane.AddToClassList("vnte-pane");
            pane.AddToClassList("vnte-pane--side");
            VisualElement title = VeneerStyle.Section("Theme");
            propsTitle = title[0] as Label;
            title.AddToClassList("vnte-pane__title");
            pane.Add(title);
            propsScroll = NewPaneScroll("vnte-scroll");
            pane.Add(propsScroll);
            return pane;
        }

        private void ResetPropertiesState()
        {
            propsScroll = null;
            propsBody = null;
            propsTitle = null;
            resetButton = null;
            nameField = null;
        }

        // Rebuilt after the selection changes, after the asset changes and after Reset all; an edit made through a control never rebuilds
        // it (that would drop the focus and close an open colour picker).
        private void BuildProperties()
        {
            if (propsScroll == null)
            {
                return;
            }

            if (propsBody != null)
            {
                propsBody.Unbind();
                propsBody.RemoveFromHierarchy();
            }

            resetButton = null;
            nameField = null;
            propsBody = new VisualElement();
            propsBody.AddToClassList("vnte-body-column");
            propsScroll.Add(propsBody);
            if (IsCustomSelected)
            {
                propsTitle.text = "CUSTOM THEME";
            }
            else
            {
                propsTitle.text = IsGlobalSelected ? "GLOBAL THEME" : (selected != null ? selected.Name.ToUpperInvariant() : string.Empty) + " THEME";
            }

            if (IsCustomSelected && (theme == null || themeSo == null))
            {
                // Cannot happen while the selection is valid; kept so the pane is never a blank.
                propsBody.Add(VeneerStyle.Hint("This theme no longer exists.", "warn", Brand));
                return;
            }

            if (themeSo != null)
            {
                themeSo.Update();
            }

            BuildThemeRows();
            if (themeSo != null)
            {
                propsBody.TrackSerializedObjectValue(themeSo, OnThemeSoChanged);
            }
        }

        private void BuildThemeRows()
        {
            // What a setting looks like when this theme does not override it: the global theme (for a system's or a custom theme), else
            // the system's own look. The global theme has no parent: its inherited look is the kit's. A custom theme is tied to no system.
            VeneerTheme parent = VeneerThemeInherited.Parent(IsGlobalSelected);
            VeneerBrand brand = IsSystemSelected ? selected : null;

            if (IsCustomSelected)
            {
                propsBody.Add(NameSetting());
            }

            if (theme != null)
            {
                string path = AssetDatabase.GetAssetPath(theme);
                Label pathLabel = VeneerStyle.MutedLabel(path);
                pathLabel.AddToClassList("vnte-path");
                pathLabel.tooltip = path + "\nClick to show the theme asset in the Project window.";
                pathLabel.RegisterCallback<ClickEvent>(evt =>
                {
                    if (theme != null)
                    {
                        EditorGUIUtility.PingObject(theme);
                    }
                });
                propsBody.Add(pathLabel);
            }
            else
            {
                Label none = VeneerStyle.MutedLabel("No theme asset yet: " + ThemeFolder + "/" + FileBase(selectedBrand) + " Theme.asset is made when you tick a setting or choose a picture.");
                none.AddToClassList("vnte-note");
                propsBody.Add(none);
            }

            string howText;
            if (IsCustomSelected)
            {
                howText = "A look of your own: choose it by name for any script in the Inspector Designer. Untick a setting to fall back to the global theme, then to the kit's look.";
            }
            else if (IsGlobalSelected)
            {
                howText = "Tick a setting to override it for every system. A greyed setting shows the kit's own look, which every system keeps.";
            }
            else
            {
                howText = "Tick a setting to override it. A greyed setting shows the look this system has now (the global theme, else its own).";
            }

            Label how = VeneerStyle.MutedLabel(howText);
            how.AddToClassList("vnte-note");
            propsBody.Add(how);

            VisualElement colours = VeneerStyle.Card("Colours", "target", Brand);
            colours.Add(ColorSetting("Accent", "OverrideAccent", "Accent", VeneerThemeInherited.Accent(brand, parent), "Selected tab, chips that are on, icons and primary buttons."));
            colours.Add(ColorSetting("Card", "OverrideCardColor", "CardColor", VeneerThemeInherited.CardColor(parent), "Card background. The alpha matters: the kit's own is a faint white over the window."));
            colours.Add(ColorSetting("Card border", "OverrideCardBorder", "CardBorder", VeneerThemeInherited.CardBorder(parent), "Card border colour."));
            colours.Add(ColorSetting("Text", "OverrideText", "Text", VeneerThemeInherited.Text(parent), "Main text colour."));
            colours.Add(ColorSetting("Muted text", "OverrideTextMuted", "TextMuted", VeneerThemeInherited.TextMuted(parent), "Secondary text colour (labels, captions)."));
            propsBody.Add(colours);

            VisualElement shape = VeneerStyle.Card("Shape and spacing", "grid", Brand);
            shape.Add(ScaleSetting("Corner radius", "OverrideRadius", "RadiusScale", RadiusMin, RadiusMax, VeneerThemeInherited.RadiusScale(parent), RadiusCaption, "How round cards, buttons, chips and fields are: 0 is square, 1 is the kit's look, 2 is twice as round."));
            shape.Add(ScaleSetting("Density", "OverrideDensity", "DensityScale", DensityMin, DensityMax, VeneerThemeInherited.DensityScale(parent), DensityCaption, "Room around and between controls: 1 is the kit's look, lower is tighter, higher is airier."));
            shape.Add(ChoiceSetting("Tab style", "OverrideTabStyle", "TabStyle", TabStyleNames, (int)VeneerThemeInherited.TabStyle(parent), "How the tab bar looks.", null));
            propsBody.Add(shape);

            VisualElement motion = VeneerStyle.Card("Motion", "speed", Brand);
            motion.Add(ChoiceSetting(
                "Motion",
                "OverrideMotion",
                "Motion",
                MotionNames,
                (int)VeneerThemeInherited.Motion(parent),
                "How much the interface moves (hover and press effects, tab slides, dropdown openings).\n" + string.Join("\n", MotionTips),
                MotionTips));
            propsBody.Add(motion);

            // The pictures the entry really uses now (the global theme's, else the system's own); a custom theme has no system of its own.
            Texture2D inheritedLogo = parent != null && parent.Logo != null ? parent.Logo : (brand != null ? brand.Logo : null);
            Texture2D inheritedBanner = parent != null && parent.Banner != null ? parent.Banner : (brand != null ? brand.Banner : null);
            VisualElement pictures = VeneerStyle.Card("Pictures", "image", Brand);
            pictures.Add(PictureSetting("Logo", "Logo", inheritedLogo, "Used instead of the usual logo (headers, window title bars). Unticked keeps the inherited one."));
            pictures.Add(PictureSetting("Banner", "Banner", inheritedBanner, "Used instead of the usual banner (the wide picture of headers). Unticked keeps the inherited one."));
            propsBody.Add(pictures);

            propsBody.Add(VeneerStyle.Hint(
                EditorGUIUtility.isProSkin
                    ? "These colours are used on both editor skins, the dark and the light one. Pick colours that read well on both."
                    : "You are in the light editor skin. These colours are used on both skins, so check the look in the dark skin too.",
                "info",
                Brand));

            VisualElement actions = new VisualElement();
            actions.AddToClassList("vnte-actions");
            if (IsCustomSelected)
            {
                actions.Add(VeneerStyle.Button("Delete theme", "trash", "Deletes this theme and its style sheet. Layouts that use it go back to the default look.", DeleteCustomTheme, false, Brand));
            }
            else
            {
                resetButton = VeneerStyle.Button("Reset all", "refresh", "Unticks every override, clears the pictures and deletes this theme's asset: the editors go back to their own look.", ResetAll, false, Brand);
                actions.Add(resetButton);
                UpdateResetButton();
            }

            propsBody.Add(actions);
        }

        // Reset all has nothing to reset until the theme asset exists.
        private void UpdateResetButton()
        {
            if (resetButton != null)
            {
                resetButton.SetEnabled(theme != null);
            }
        }

        // The name of a custom theme: a delayed text field (committed with Enter or when it loses the focus). Renaming also renames the asset.
        private VisualElement NameSetting()
        {
            VisualElement setting = NewSetting("The name of this theme, shown when you choose it for a script. It is also the asset's file name.", out VisualElement head);
            head.Add(NewSettingLabel("Name", true));
            var field = new TextField { isDelayed = true, maxLength = 64 };
            field.AddToClassList("vnte-setting__text");
            field.SetValueWithoutNotify(theme != null ? DisplayName(theme) : string.Empty);
            field.RegisterValueChangedCallback(evt => OnNameCommitted(evt.newValue));
            head.Add(field);
            nameField = field;

            if (focusNamePending)
            {
                focusNamePending = false;
                field.schedule.Execute(() =>
                {
                    field.Focus();
                    field.SelectAll();
                }).ExecuteLater(30);
            }

            return setting;
        }

        // After an edit made through the SerializedObject elsewhere (not by one of our controls, which call MarkDirty themselves).
        private void OnThemeSoChanged(SerializedObject changed)
        {
            if (theme == null || RootElement == null || !EditorUtility.IsDirty(theme))
            {
                return;
            }

            MarkDirty();
        }

        // ------------------------------------------------------------------ reading and writing

        private bool FlagOf(string path)
        {
            SerializedProperty property = themeSo != null ? themeSo.FindProperty(path) : null;
            return property != null && property.boolValue;
        }

        private Color ColorOf(string path, Color fallback)
        {
            SerializedProperty property = themeSo != null ? themeSo.FindProperty(path) : null;
            return property != null ? property.colorValue : fallback;
        }

        private float FloatOf(string path, float fallback)
        {
            SerializedProperty property = themeSo != null ? themeSo.FindProperty(path) : null;
            return property != null ? property.floatValue : fallback;
        }

        private int EnumOf(string path, int fallback)
        {
            SerializedProperty property = themeSo != null ? themeSo.FindProperty(path) : null;
            return property != null ? property.enumValueIndex : fallback;
        }

        private Texture2D TextureOf(string path)
        {
            SerializedProperty property = themeSo != null ? themeSo.FindProperty(path) : null;
            return property != null ? property.objectReferenceValue as Texture2D : null;
        }

        // An edit is: BeginEdit, set one or more properties, EndEdit (one Undo step, then the sample and the save follow). A system's or
        // Global's theme asset is made here, the first time an edit needs it.
        private bool BeginEdit()
        {
            if (!EnsureTheme())
            {
                return false;
            }

            themeSo.Update();
            return true;
        }

        private void EndEdit()
        {
            themeSo.ApplyModifiedProperties();
            MarkDirty();
        }

        private void SetFlag(string path, bool value)
        {
            SerializedProperty property = themeSo.FindProperty(path);
            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private void SetColor(string path, Color value)
        {
            SerializedProperty property = themeSo.FindProperty(path);
            if (property != null)
            {
                property.colorValue = value;
            }
        }

        private void SetFloat(string path, float value)
        {
            SerializedProperty property = themeSo.FindProperty(path);
            if (property != null)
            {
                property.floatValue = value;
            }
        }

        private void SetEnum(string path, int index)
        {
            SerializedProperty property = themeSo.FindProperty(path);
            if (property != null)
            {
                property.enumValueIndex = index;
            }
        }

        private void SetTexture(string path, Texture2D value)
        {
            SerializedProperty property = themeSo.FindProperty(path);
            if (property != null)
            {
                property.objectReferenceValue = value;
            }
        }

        // ------------------------------------------------------------------ rows

        // The shell of one setting: a column with a head line (Override box and label); stacked settings put their control on a second
        // line, so a narrow pane never has to squeeze a wide control next to the label.
        private static VisualElement NewSetting(string tooltip, out VisualElement head)
        {
            var setting = new VisualElement();
            setting.AddToClassList("vnte-setting");
            setting.tooltip = tooltip;
            head = new VisualElement();
            head.AddToClassList("vnte-setting__head");
            setting.Add(head);
            return setting;
        }

        private static Toggle NewOverrideToggle(bool value)
        {
            var toggle = new Toggle();
            toggle.AddToClassList("vnte-setting__override");
            toggle.tooltip = "Override this setting";
            toggle.SetValueWithoutNotify(value);
            return toggle;
        }

        private static Label NewSettingLabel(string text, bool fixedWidth)
        {
            var label = new Label(text);
            label.AddToClassList("vnte-setting__label");
            if (fixedWidth)
            {
                label.AddToClassList("vnte-setting__label--fixed");
            }

            return label;
        }

        // A colour: [box] [label] [colour field]. Off: the inherited colour, greyed. On: the theme's own.
        private VisualElement ColorSetting(string label, string overridePath, string valuePath, Color inherited, string tooltip)
        {
            bool on = FlagOf(overridePath);

            VisualElement setting = NewSetting(tooltip, out VisualElement head);
            Toggle toggle = NewOverrideToggle(on);
            head.Add(toggle);
            head.Add(NewSettingLabel(label, true));
            var field = new ColorField { showAlpha = true };
            field.AddToClassList("vnte-setting__color");
            field.SetValueWithoutNotify(on ? ColorOf(valuePath, inherited) : inherited);
            field.SetEnabled(on);
            head.Add(field);

            field.RegisterValueChangedCallback(evt =>
            {
                if (BeginEdit())
                {
                    SetColor(valuePath, evt.newValue);
                    EndEdit();
                }
            });
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (!BeginEdit())
                {
                    return;
                }

                if (evt.newValue)
                {
                    // Editing starts from the look that was shown.
                    SetFlag(overridePath, true);
                    SetColor(valuePath, field.value);
                }
                else
                {
                    SetFlag(overridePath, false);
                    field.SetValueWithoutNotify(inherited);
                }

                field.SetEnabled(evt.newValue);
                EndEdit();
            });
            return setting;
        }

        private static string RadiusCaption(float value)
        {
            if (value <= 0.001f)
            {
                return "Square";
            }

            if (Mathf.Abs(value - 1f) < 0.025f)
            {
                return "Kit default";
            }

            return value > 1.3f ? "Rounder" : "Rounded";
        }

        private static string DensityCaption(float value)
        {
            if (Mathf.Abs(value - 1f) < 0.025f)
            {
                return "Kit default";
            }

            return value < 1f ? "Tight" : "Airy";
        }

        // A scale: [box] [label] [caption] on the head line, [slider] [number] [reset to 1] under it. Off: the inherited scale, greyed.
        // Every change writes the theme at once (the sample and the save are debounced by MarkDirty), so the sample follows a drag.
        private VisualElement ScaleSetting(string label, string overridePath, string valuePath, float min, float max, float inherited, Func<float, string> captionOf, string tooltip)
        {
            bool on = FlagOf(overridePath);
            float start = on ? Mathf.Clamp(FloatOf(valuePath, 1f), min, max) : inherited;

            VisualElement setting = NewSetting(tooltip, out VisualElement head);
            Toggle toggle = NewOverrideToggle(on);
            head.Add(toggle);
            head.Add(NewSettingLabel(label, false));
            Label caption = VeneerStyle.MutedLabel(captionOf(start));
            caption.AddToClassList("vnte-setting__caption");
            head.Add(caption);

            var body = new VisualElement();
            body.AddToClassList("vnte-scale");
            var slider = new Slider(min, max);
            slider.AddToClassList("vnte-scale__slider");
            slider.SetValueWithoutNotify(start);
            slider.SetEnabled(on);
            body.Add(slider);

            var field = new FloatField { isDelayed = true, formatString = "0.##" };
            field.AddToClassList("vnte-scale__field");
            field.SetValueWithoutNotify(start);
            field.SetEnabled(on);
            body.Add(field);

            Button reset = VeneerStyle.IconButton("undo", "Reset to 1: the kit's own look", null, Brand);
            reset.AddToClassList("vnte-scale__reset");
            reset.SetEnabled(on);
            body.Add(reset);
            setting.Add(body);

            // Shows a value in the three controls without raising their events, then writes it.
            Action<float> apply = raw =>
            {
                float value = Mathf.Clamp(Mathf.Round(raw * 100f) / 100f, min, max);
                if (!Mathf.Approximately(slider.value, value))
                {
                    slider.SetValueWithoutNotify(value);
                }

                if (!Mathf.Approximately(field.value, value))
                {
                    field.SetValueWithoutNotify(value);
                }

                caption.text = captionOf(value);
                if (!BeginEdit())
                {
                    return;
                }

                if (FlagOf(overridePath) && Mathf.Approximately(FloatOf(valuePath, value), value))
                {
                    return;
                }

                SetFloat(valuePath, value);
                EndEdit();
            };

            slider.RegisterValueChangedCallback(evt =>
            {
                // The slider moves in steps of ScaleStep; the number field takes any value in between.
                apply(Mathf.Round(evt.newValue / ScaleStep) * ScaleStep);
            });
            field.RegisterValueChangedCallback(evt => apply(evt.newValue));
            reset.clicked += () => apply(1f);

            toggle.RegisterValueChangedCallback(evt =>
            {
                if (!BeginEdit())
                {
                    return;
                }

                if (evt.newValue)
                {
                    SetFlag(overridePath, true);
                    SetFloat(valuePath, Mathf.Clamp(Mathf.Round(slider.value * 100f) / 100f, min, max));
                }
                else
                {
                    SetFlag(overridePath, false);
                    slider.SetValueWithoutNotify(inherited);
                    field.SetValueWithoutNotify(inherited);
                    caption.text = captionOf(inherited);
                }

                slider.SetEnabled(evt.newValue);
                field.SetEnabled(evt.newValue);
                reset.SetEnabled(evt.newValue);
                EndEdit();
            });
            return setting;
        }

        // An enum as a segmented choice with text buttons (the value is the enum's index), on its own line under the label. The optional
        // tips are one tooltip per button (null keeps the button text as its tooltip).
        private VisualElement ChoiceSetting(string label, string overridePath, string valuePath, string[] names, int inherited, string tooltip, string[] tips)
        {
            bool on = FlagOf(overridePath);

            VisualElement setting = NewSetting(tooltip, out VisualElement head);
            Toggle toggle = NewOverrideToggle(on);
            head.Add(toggle);
            head.Add(NewSettingLabel(label, false));

            int current = on ? EnumOf(valuePath, inherited) : inherited;
            VisualElement group = VeneerStyle.SegmentedIcons(new string[names.Length], names, current, index =>
            {
                if (BeginEdit())
                {
                    SetEnum(valuePath, index);
                    EndEdit();
                }
            }, Brand);
            group.AddToClassList("vnte-choice");
            if (tips != null)
            {
                for (int i = 0; i < group.childCount && i < tips.Length; i++)
                {
                    group[i].tooltip = tips[i];
                }
            }

            group.SetEnabled(on);
            setting.Add(group);

            toggle.RegisterValueChangedCallback(evt =>
            {
                if (!BeginEdit())
                {
                    return;
                }

                if (evt.newValue)
                {
                    SetFlag(overridePath, true);
                    SetEnum(valuePath, VeneerStyle.GetSegmentedSelected(group));
                }
                else
                {
                    SetFlag(overridePath, false);
                    VeneerStyle.SetSegmentedSelected(group, inherited);
                }

                group.SetEnabled(evt.newValue);
                EndEdit();
            });
            return setting;
        }

        // A picture: [box] [label] on one line, [thumbnail] [object field] under it. A theme has no switch for pictures: the picture
        // itself is the override, so the box is ticked while the theme holds one. Off: the picture the entry really uses now (greyed,
        // by name); ticking copies it into the theme, unticking clears the theme's picture (the inherited one shows again).
        private VisualElement PictureSetting(string label, string path, Texture2D inherited, string tooltip)
        {
            Texture2D own = TextureOf(path);
            bool on = own != null;

            VisualElement setting = NewSetting(tooltip, out VisualElement head);
            Toggle toggle = NewOverrideToggle(on);
            head.Add(toggle);
            head.Add(NewSettingLabel(label, false));

            var body = new VisualElement();
            body.AddToClassList("vnte-setting__body");
            var thumb = new VisualElement();
            thumb.AddToClassList("vnte-thumb");
            thumb.pickingMode = PickingMode.Ignore;
            Texture2D shown = on ? own : inherited;
            SetThumb(thumb, shown);
            body.Add(thumb);

            var field = new ObjectField { objectType = typeof(Texture2D), allowSceneObjects = false };
            field.AddToClassList("vnte-setting__object");
            field.SetValueWithoutNotify(shown);
            field.SetEnabled(on);
            body.Add(field);
            setting.Add(body);

            field.RegisterValueChangedCallback(evt =>
            {
                Texture2D picked = evt.newValue as Texture2D;
                SetThumb(thumb, picked);
                if (picked != null && !toggle.value)
                {
                    toggle.SetValueWithoutNotify(true);
                }

                if (BeginEdit())
                {
                    SetTexture(path, picked);
                    EndEdit();
                }
            });
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    // Nothing to copy when there is no picture: the field simply becomes editable and empty.
                    if (inherited != null && BeginEdit())
                    {
                        SetTexture(path, inherited);
                        EndEdit();
                    }
                }
                else
                {
                    field.SetValueWithoutNotify(inherited);
                    SetThumb(thumb, inherited);
                    if (theme != null && BeginEdit())
                    {
                        SetTexture(path, null);
                        EndEdit();
                    }
                }

                field.SetEnabled(evt.newValue);
            });
            return setting;
        }

        private static void SetThumb(VisualElement thumb, Texture2D texture)
        {
            if (texture != null)
            {
                thumb.style.backgroundImage = new StyleBackground(texture);
            }
            else
            {
                thumb.style.backgroundImage = StyleKeyword.None;
            }
        }
    }
}
