using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    // Right pane: the properties of the selected node. Every control is bound to the layout's SerializedObject, so Undo, dirty marking and
    // multi-step undo come from Unity; a tracker on the pane's body reports each change so the tree, the marks and the preview follow.
    public sealed partial class VeneerDesignerWindow
    {
        private const string NoShowIf = "(always shown)";
        private const string SameRowTip = "Draws this item in the same row as the item before it (for example two toggle chips side by side).";

        private ScrollView propsScroll;
        private VisualElement propsBody;
        private Label propsTitle;

        // Re-evaluates which banner rows are shown (set while the Header pane is built; the tracker calls it after every layout change).
        private Action bannerRefresh;

        private VisualElement BuildPropertiesPane()
        {
            var pane = new VisualElement();
            pane.AddToClassList("vnd-pane");
            propsTitle = VeneerStyle.Section("Properties")[0] as Label;
            propsTitle.parent.AddToClassList("vnd-props__title");
            pane.Add(propsTitle.parent);
            propsScroll = new ScrollView(ScrollViewMode.Vertical);
            propsScroll.AddToClassList("vnd-scroll");
            propsScroll.AddToClassList("vnd-props");
            pane.Add(propsScroll);
            return pane;
        }

        // Rebuilt after selection changes and after direct (non-bound) edits of the model; bound edits must not rebuild it (focus would be lost).
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

            bannerRefresh = null;
            lookRefresh = null;
            propsBody = new VisualElement();
            propsScroll.Add(propsBody);
            // The system's own (read-only) design can be looked at, not changed.
            propsScroll.SetEnabled(Editable);
            if (layout == null || layoutSo == null)
            {
                propsTitle.text = "PROPERTIES";
                return;
            }

            layoutSo.Update();
            switch (selKind)
            {
                case NodeKind.Header:
                    propsTitle.text = "HEADER AND LOOK";
                    BuildHeaderProperties();
                    break;
                case NodeKind.Page:
                    propsTitle.text = "TAB";
                    BuildPageProperties();
                    break;
                case NodeKind.Block:
                    propsTitle.text = "CARD";
                    BuildBlockProperties();
                    break;
                case NodeKind.Entry:
                    propsTitle.text = "FIELD";
                    BuildEntryProperties();
                    break;
                case NodeKind.HiddenHeader:
                    propsTitle.text = "HIDDEN";
                    propsBody.Add(VeneerStyle.Hint("Hidden fields are not drawn at all, not even in the automatic Other section. Drag a field here to hide it, or use the eye button of a row.", "info", Brand));
                    break;
                default:
                    propsTitle.text = "HIDDEN FIELD";
                    propsBody.Add(VeneerStyle.Hint("This field is hidden. Press its eye button (or Delete) to show it again, or drag it into a card.", "info", Brand));
                    break;
            }

            propsBody.TrackSerializedObjectValue(layoutSo, OnLayoutSoChanged);
        }

        // ------------------------------------------------------------------ the kinds

        private void BuildHeaderProperties()
        {
            BuildThemeCard();

            VisualElement header = VeneerStyle.Card("Header", "text", Brand);
            header.Add(ToggleRow("Show header", "ShowHeader", "Draw the title block (icon, title, subtitle) at the top of the Inspector."));
            header.Add(TextRow("Title", "Title", "Empty uses the type's name split into words."));
            header.Add(TextRow("Subtitle", "Subtitle", "A muted line under the title."));

            // The icon comes from the chosen look (its logo) or is the user's own (a kit icon or a picture).
            VisualElement logoView = ThemePictureView(true, out Action refreshLogo);
            VisualElement ownIcon = IconRow("Kit icon", "HeaderIcon", "HeaderIconTexture");
            bool themeLogo = layout.UseThemeLogo;
            Action logoRows = () =>
            {
                VeneerStyle.SetDisplay(logoView, themeLogo);
                VeneerStyle.SetDisplay(ownIcon, !themeLogo);
            };
            VisualElement logoChoice = BoolChoice(
                "Theme logo", "Own", themeLogo, "UseThemeLogo",
                "Use the logo of the chosen theme or system as the header icon.",
                "Use a kit icon or your own picture.",
                on =>
                {
                    themeLogo = on;
                    refreshLogo();
                    logoRows();
                    if (bannerRefresh != null)
                    {
                        bannerRefresh();
                    }
                });
            header.Add(VeneerStyle.FieldRow("Icon", logoChoice, "Where the header icon comes from.", out _));
            header.Add(logoView);
            header.Add(ownIcon);
            logoRows();
            lookRefresh = () =>
            {
                if (layout == null)
                {
                    return;
                }

                themeLogo = layout.UseThemeLogo;
                refreshLogo();
                logoRows();
            };
            propsBody.Add(header);

            VisualElement scriptCard = VeneerStyle.Card("Script icon", "image", Brand);
            scriptCard.Add(ObjectRow("Picture", "ScriptIcon", typeof(Texture2D), "Applied to the script itself: Project window, Add Component menu, Inspector title. Clear it to go back."));
            Label note = VeneerStyle.MutedLabel("Stored in the script's .meta file; removing the picture here removes it from the script.");
            note.AddToClassList("vnd-note");
            scriptCard.Add(note);
            propsBody.Add(scriptCard);

            BuildBannerCard();

            VisualElement colour = VeneerStyle.Card("Accent colour", "target", Brand);
            Toggle use = MakeToggle("UseAccent");
            colour.Add(VeneerStyle.FieldRow("Custom accent", use, "Replace the kit's accent (tab underline, chips that are on, icons) in this Inspector.", out _));
            var accent = new ColorField { showAlpha = false };
            accent.AddToClassList("vnd-control--capped");
            Bind(accent, "Accent");
            VisualElement accentRow = VeneerStyle.FieldRow("Accent", accent, null, out _);
            colour.Add(accentRow);
            VeneerStyle.SetDisplay(accentRow, layout.UseAccent);
            use.RegisterValueChangedCallback(evt => VeneerStyle.SetDisplay(accentRow, evt.newValue));
            propsBody.Add(colour);

            VisualElement others = VeneerStyle.Card("Other fields", "list", Brand);
            Toggle showOthers = MakeToggle("ShowOthers");
            others.Add(VeneerStyle.FieldRow(
                "Show unplaced fields", showOthers, "Fields that are in no card appear in an automatic Other card, so a field added to the script later is never lost.", out _));
            VisualElement lastTabRow = ToggleRow("Put them in the last tab", "OtherInLastPage", "On: the Other card sits at the end of the last tab. Off: under the whole tab view.");
            var otherTitle = new TextField();
            Bind(otherTitle, "OtherTitle");
            otherTitle.textEdition.placeholder = "Other";
            VisualElement otherTitleRow = VeneerStyle.FieldRow("Title", otherTitle, "The title of the Other card. Empty uses Other.", out _);
            others.Add(lastTabRow);
            others.Add(otherTitleRow);
            VeneerStyle.SetDisplay(lastTabRow, layout.ShowOthers);
            VeneerStyle.SetDisplay(otherTitleRow, layout.ShowOthers);
            showOthers.RegisterValueChangedCallback(evt =>
            {
                VeneerStyle.SetDisplay(lastTabRow, evt.newValue);
                VeneerStyle.SetDisplay(otherTitleRow, evt.newValue);
            });
            propsBody.Add(others);
        }

        // The banner is part of the header: a picture behind the header's icon, title and subtitle (or, with the text off the picture, a plain picture above).
        private void BuildBannerCard()
        {
            VeneerLayoutBanner banner = layout.Banner;
            if (banner == null)
            {
                return;
            }

            VisualElement card = VeneerStyle.Card("Banner", "image", Brand);
            card.Add(ToggleRow("Enabled", "Banner.Enabled", "Show a picture as the header's background."));

            // The picture is the chosen look's banner or the user's own image.
            bool themePicture = banner.UseThemePicture;
            Action rows = null;
            VisualElement themeView = ThemePictureView(false, out Action refreshPicture);
            VisualElement imageRow = ObjectRow("Image", "Banner.Image", typeof(Texture2D), null);
            VisualElement pictureChoice = BoolChoice(
                "Theme", "Own", themePicture, "Banner.UseThemePicture",
                "Use the banner picture of the chosen theme or system.",
                "Use your own image.",
                on =>
                {
                    themePicture = on;
                    rows();
                });
            card.Add(VeneerStyle.FieldRow("Picture", pictureChoice, "Where the banner picture comes from: the chosen theme or your own image.", out _));
            card.Add(themeView);
            card.Add(imageRow);
            card.Add(FloatRow("Height", "Banner.Height", "Height of the banner in pixels."));
            card.Add(VeneerStyle.FieldRow("Fit", BuildFitChoice(banner.Fit), null, out _));

            Toggle over = MakeToggle("Banner.OverHeader");
            card.Add(VeneerStyle.FieldRow(
                "Header text on banner", over, "On: the header's icon, title and subtitle are drawn on the picture. Off: the picture sits above the header.", out _));

            VisualElement iconSizeRow = FloatRow("Icon size", "Banner.IconSize", "Size in pixels of the header icon on the banner.");
            card.Add(iconSizeRow);

            var dim = new Slider(0f, 1f) { showInputField = true };
            Bind(dim, "Banner.Dim");
            VisualElement dimRow = VeneerStyle.FieldRow("Dim", dim, "Darkens the picture so the header text stays readable (0 none, 1 black).", out _);
            card.Add(dimRow);

            var tint = new ColorField { showAlpha = true };
            tint.AddToClassList("vnd-control--capped");
            Bind(tint, "Banner.Tint");
            card.Add(VeneerStyle.FieldRow("Tint", tint, "Colour multiplied with the picture; white keeps it as it is.", out _));

            // Only used while the text is NOT on the picture.
            Toggle full = MakeToggle("Banner.FullWidth");
            VisualElement fullRow = VeneerStyle.FieldRow("Full width", full, "On: the banner spans the Inspector. Off: it is Width pixels wide.", out _);
            card.Add(fullRow);
            VisualElement widthRow = FloatRow("Width", "Banner.Width", "Width in pixels when Full width is off.");
            card.Add(widthRow);
            VisualElement alignRow = EnumRow("Align", "Banner.Align", VeneerBannerAlign.Left, "Where a banner that is narrower than the Inspector sits.");
            card.Add(alignRow);
            propsBody.Add(card);

            // Visibility follows the values; the toggles' callbacks pass their new value because the bound write may not have landed yet.
            bool overOn = banner.OverHeader;
            bool fullOn = banner.FullWidth;
            rows = () =>
            {
                VeneerStyle.SetDisplay(themeView, themePicture);
                VeneerStyle.SetDisplay(imageRow, !themePicture);
                bool hasIcon = layout.UseThemeLogo || !string.IsNullOrEmpty(layout.HeaderIcon) || layout.HeaderIconTexture != null;
                VeneerStyle.SetDisplay(iconSizeRow, overOn && hasIcon);
                VeneerStyle.SetDisplay(dimRow, overOn);
                VeneerStyle.SetDisplay(fullRow, !overOn);
                VeneerStyle.SetDisplay(widthRow, !overOn && !fullOn);
                VeneerStyle.SetDisplay(alignRow, !overOn);
            };
            over.RegisterValueChangedCallback(evt =>
            {
                overOn = evt.newValue;
                rows();
            });
            full.RegisterValueChangedCallback(evt =>
            {
                fullOn = evt.newValue;
                rows();
            });
            bannerRefresh = () =>
            {
                if (layout == null || layout.Banner == null)
                {
                    return;
                }

                overOn = layout.Banner.OverHeader;
                fullOn = layout.Banner.FullWidth;
                themePicture = layout.Banner.UseThemePicture;
                refreshPicture();
                rows();
            };
            rows();
        }

        // Cover / Fit / Stretch as one segmented choice; the long explanations are the tooltips.
        private VisualElement BuildFitChoice(VeneerBannerFit current)
        {
            var labels = new[] { "Cover", "Fit", "Stretch" };
            var tips = new[]
            {
                "Cover: fills the box and crops what does not fit.",
                "Fit: shows the whole picture with its aspect ratio kept (like Image's Preserve Aspect).",
                "Stretch: fills the box and distorts the picture.",
            };
            VisualElement group = VeneerStyle.SegmentedIcons(new string[3], labels, (int)current, index =>
            {
                SerializedProperty property = layoutSo.FindProperty("Banner.Fit");
                if (property == null)
                {
                    return;
                }

                property.enumValueIndex = index;
                layoutSo.ApplyModifiedProperties();
            }, Brand);
            for (int i = 0; i < group.childCount && i < tips.Length; i++)
            {
                group[i].tooltip = tips[i];
            }

            return group;
        }

        private void BuildPageProperties()
        {
            string prefix = PagePrefix(selPage);
            VisualElement card = VeneerStyle.Card("Tab", "layers", Brand);
            card.Add(TextRow("Title", prefix + "Title", "Text on the tab button."));
            card.Add(IconRow("Icon", prefix + "Icon", prefix + "IconTexture"));
            propsBody.Add(card);
            VeneerLayoutPage page = layout.Pages[selPage];
            VisualElement pageRule = VeneerStyle.Card("Show only when", "filter", Brand);
            BuildShowIf(pageRule, prefix, string.Empty, page.ShowIfPath, page.ShowIf);
            propsBody.Add(pageRule);
            if (layout.Pages.Count == 1)
            {
                propsBody.Add(VeneerStyle.Hint("With a single tab no tab bar is drawn.", "info", Brand));
            }
        }

        private void BuildBlockProperties()
        {
            string prefix = BlockPrefix(selPage, selBlock);
            VisualElement card = VeneerStyle.Card("Card", "grid", Brand);
            card.Add(TextRow("Title", prefix + "Title", "Title of the card."));
            card.Add(ToggleRow("Show title", prefix + "ShowTitle", "Off draws the card without its title bar."));
            card.Add(IconRow("Icon", prefix + "Icon", prefix + "IconTexture"));
            propsBody.Add(card);

            VeneerLayoutBlock block = layout.Pages[selPage].Blocks[selBlock];
            VisualElement rule = VeneerStyle.Card("Show only when", "filter", Brand);
            BuildShowIf(rule, prefix, string.Empty, block.ShowIfPath, block.ShowIf);
            propsBody.Add(rule);
        }

        private void BuildEntryProperties()
        {
            string prefix = EntryPrefix(selPage, selBlock, selEntry);
            VeneerLayoutEntry entry = layout.Pages[selPage].Blocks[selBlock].Entries[selEntry];
            if (entry.IsNote)
            {
                BuildNoteProperties(prefix, entry);
                return;
            }

            if (entry.IsCustom)
            {
                BuildCustomProperties(prefix, entry);
                return;
            }

            string path = entry.Path ?? string.Empty;

            VisualElement card = VeneerStyle.Card("Field", "dot", Brand);
            Label pathLabel = VeneerStyle.MutedLabel(path + (PathExists(path) ? string.Empty : "  (missing from the script)"));
            // One line with an ellipsis (a property path has no spaces to wrap at); the full path is the tooltip.
            pathLabel.AddToClassList("vn-ellipsis");
            pathLabel.tooltip = pathLabel.text;
            card.Add(pathLabel);
            card.Add(TextRow("Label", prefix + "Label", "Replaces the field's own name. Empty keeps the default."));
            card.Add(TextRow("Tooltip", prefix + "Tooltip", "Replaces the field's own tooltip. Empty keeps the default."));
            card.Add(TextRow("Unit", prefix + "Unit", "Written next to a number, for example m or hp."));

            // The widget popup is built from the enum (so every member, Tag included, is listed); Tag reads "Tag dropdown".
            // (EnumField has no item formatter, so this is a dropdown over the enum's members; written like the show-if dropdowns.)
            var widgetField = new DropdownField(new List<string>(WidgetNames), Mathf.Clamp((int)entry.Widget, 0, WidgetNames.Length - 1));
            widgetField.AddToClassList("vnd-control--capped");
            card.Add(VeneerStyle.FieldRow("Widget", widgetField, "How the field is drawn. Auto picks from the field's type and attributes.", out _));

            // Widget-specific texts: slider end captions and enum button icons. Shown by the widget (Auto: by the field's own type).
            SerializedProperty fieldProperty = tempSo != null ? tempSo.FindProperty(path) : null;
            bool autoNumber = fieldProperty == null
                || fieldProperty.propertyType == SerializedPropertyType.Float
                || fieldProperty.propertyType == SerializedPropertyType.Integer;
            bool autoEnum = fieldProperty != null && fieldProperty.propertyType == SerializedPropertyType.Enum;
            bool autoBool = fieldProperty != null && fieldProperty.propertyType == SerializedPropertyType.Boolean;
            bool autoVector2 = fieldProperty != null && fieldProperty.propertyType == SerializedPropertyType.Vector2;
            VisualElement leftRow = TextRow("Left end text", prefix + "SliderLeft", "Text under the left end of the slider, for example Short.");
            VisualElement rightRow = TextRow("Right end text", prefix + "SliderRight", "Text under the right end of the slider, for example Long.");
            VisualElement iconsRow = TextRow(
                "Button icons",
                prefix + "EnumIcons",
                "Kit icon names for the buttons of the enum, comma separated, one per value in order (for example: sword,shield,bow). Empty shows the value names.");
            VisualElement chipIconRow = IconRow("Chip icon", prefix + "Icon", null);
            VisualElement sameRow = ToggleRow("Same row as the previous item", prefix + "SameRow", SameRowTip);
            Toggle rangeToggle = MakeToggle(prefix + "HasRange");
            VisualElement rangeRow = VeneerStyle.FieldRow(
                "Own range", rangeToggle, "Replaces the field's own [Range] or [Min] with the limits below; the value is clamped here.", out _);
            VisualElement minRow = FloatRow("Min", prefix + "Min", "The lowest value allowed.");
            VisualElement maxRow = FloatRow("Max", prefix + "Max", "The highest value allowed.");
            bool rangeApplies = false;
            bool rangeOn = entry.HasRange;
            Action rangeRows = () =>
            {
                VeneerStyle.SetDisplay(rangeRow, rangeApplies);
                VeneerStyle.SetDisplay(minRow, rangeApplies && rangeOn);
                VeneerStyle.SetDisplay(maxRow, rangeApplies && rangeOn);
            };
            rangeToggle.RegisterValueChangedCallback(evt =>
            {
                rangeOn = evt.newValue;
                rangeRows();
            });
            card.Add(leftRow);
            card.Add(rightRow);
            card.Add(rangeRow);
            card.Add(minRow);
            card.Add(maxRow);
            card.Add(iconsRow);
            card.Add(chipIconRow);
            card.Add(sameRow);
            Action<VeneerWidget> widgetRows = widget =>
            {
                bool slider = widget == VeneerWidget.Slider || widget == VeneerWidget.MinMax || (widget == VeneerWidget.Auto && autoNumber);
                rangeApplies = widget == VeneerWidget.Slider || widget == VeneerWidget.Number || widget == VeneerWidget.MinMax
                    || (widget == VeneerWidget.Auto && (autoNumber || autoVector2));
                rangeRows();
                bool enumButtons = widget == VeneerWidget.Enum || (widget == VeneerWidget.Auto && autoEnum);
                VeneerStyle.SetDisplay(leftRow, slider);
                VeneerStyle.SetDisplay(rightRow, slider);
                VeneerStyle.SetDisplay(iconsRow, enumButtons);
                bool chip = widget == VeneerWidget.Toggle || (widget == VeneerWidget.Auto && autoBool);
                VeneerStyle.SetDisplay(chipIconRow, chip);
                VeneerStyle.SetDisplay(sameRow, chip);
            };
            widgetRows(entry.Widget);
            widgetField.RegisterValueChangedCallback(evt =>
            {
                int selected = widgetField.index;
                if (selected < 0)
                {
                    return;
                }

                SerializedProperty widgetProperty = layoutSo.FindProperty(prefix + "Widget");
                if (widgetProperty != null)
                {
                    widgetProperty.enumValueIndex = selected;
                    layoutSo.ApplyModifiedProperties();
                }

                widgetRows((VeneerWidget)selected);
            });

            card.Add(ToggleRow("Read only", prefix + "ReadOnly", "Shown but cannot be edited."));
            propsBody.Add(card);

            VisualElement rule = VeneerStyle.Card("Show only when", "filter", Brand);
            BuildShowIf(rule, prefix, path, entry.ShowIfPath, entry.ShowIf);
            propsBody.Add(rule);
        }

        // A system item: its label (an override), its description (read-only), and the same "Show only when" rule as a field.
        private void BuildCustomProperties(string prefix, VeneerLayoutEntry entry)
        {
            propsTitle.text = "SYSTEM ITEM";
            VeneerCustomItem item = FindItem(entry.CustomId);
            VisualElement card = VeneerStyle.Card("System item", string.IsNullOrEmpty(item != null ? item.Icon : null) ? "cube" : item.Icon, Brand);
            Label idLabel = VeneerStyle.MutedLabel((entry.CustomId ?? string.Empty) + (item == null ? "  (not registered by the system)" : string.Empty));
            idLabel.AddToClassList("vn-ellipsis");
            idLabel.tooltip = idLabel.text;
            card.Add(idLabel);
            card.Add(TextRow("Label", prefix + "Label", "Replaces the item's own title where it shows one. Empty keeps the default."));
            card.Add(ToggleRow("Same row as the previous item", prefix + "SameRow", SameRowTip));
            if (item != null && !string.IsNullOrEmpty(item.Description))
            {
                Label description = VeneerStyle.MutedLabel(item.Description);
                description.AddToClassList("vnd-note");
                card.Add(description);
            }

            propsBody.Add(card);

            VisualElement rule = VeneerStyle.Card("Show only when", "filter", Brand);
            BuildShowIf(rule, prefix, string.Empty, entry.ShowIfPath, entry.ShowIf);
            propsBody.Add(rule);
        }

        // Readable names of the widget popup, built on first use (not in a static initialiser: the window is a ScriptableObject and
        // Unity forbids ObjectNames.NicifyVariableName while one is being constructed) from the enum in its own order (Tag reads "Tag dropdown").
        private static string[] widgetNames;

        private static string[] WidgetNames => widgetNames ??= BuildWidgetNames();

        private static string[] BuildWidgetNames()
        {
            Array values = Enum.GetValues(typeof(VeneerWidget));
            var names = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                var widget = (VeneerWidget)values.GetValue(i);
                names[i] = widget == VeneerWidget.Tag ? "Tag dropdown" : (widget == VeneerWidget.MinMax ? "Min-max slider" : ObjectNames.NicifyVariableName(widget.ToString()));
            }

            return names;
        }

        // A note: free text in a message box (info / warning / error), with the same "Show only when" rule as a field.
        private void BuildNoteProperties(string prefix, VeneerLayoutEntry entry)
        {
            propsTitle.text = "NOTE";
            VisualElement card = VeneerStyle.Card("Note", "info", Brand);
            var text = new TextField { multiline = true };
            text.AddToClassList("vnd-control--multiline");
            Bind(text, prefix + "NoteText");
            card.Add(VeneerStyle.FieldRow("Text", text, "The message shown in the card. Several lines are allowed.", out _));

            VisualElement kind = VeneerStyle.SegmentedIcons(
                new[] { "info", "warning", "error", "text", "list" },
                new[] { "Info", "Warning", "Error", "Text: plain grey text without a box, for explanations between fields", "Heading: a small section title inside the card" },
                (int)entry.NoteKind,
                index =>
                {
                    SerializedProperty property = layoutSo.FindProperty(prefix + "NoteKind");
                    if (property == null)
                    {
                        return;
                    }

                    property.enumValueIndex = index;
                    layoutSo.ApplyModifiedProperties();
                },
                Brand);
            card.Add(VeneerStyle.FieldRow("Kind", kind, "Sets the colour and icon of the message box.", out _));
            propsBody.Add(card);

            VisualElement rule = VeneerStyle.Card("Show only when", "filter", Brand);
            BuildShowIf(rule, prefix, string.Empty, entry.ShowIfPath, entry.ShowIf);
            propsBody.Add(rule);
        }

        private void BuildShowIf(VisualElement parent, string prefix, string ownPath, string currentPath, VeneerCondition currentCondition)
        {
            string current = currentPath ?? string.Empty;
            var choices = new List<string>(fields.Count + 2) { NoShowIf };
            bool found = current.Length == 0;
            for (int i = 0; i < fields.Count; i++)
            {
                if (fields[i].Path == ownPath || fields[i].IsStruct)
                {
                    continue;
                }

                choices.Add(fields[i].Path);
                found |= fields[i].Path == current;
            }

            if (!found)
            {
                choices.Add(current);
            }

            int index = current.Length == 0 ? 0 : choices.IndexOf(current);
            var dropdown = new DropdownField(choices, index);
            dropdown.AddToClassList("vnd-control--capped");
            VisualElement conditionBox = new VisualElement();
            parent.Add(VeneerStyle.FieldRow("Field", dropdown, "The field whose value decides whether this one is shown.", out _));
            parent.Add(conditionBox);

            // Readable names in the enum's own order (IsNull = "is not assigned", NotNull = "is assigned").
            var conditionDropdown = new DropdownField(new List<string>(ConditionNames), Mathf.Clamp((int)currentCondition, 0, ConditionNames.Length - 1));
            conditionDropdown.AddToClassList("vnd-control--capped");
            conditionBox.Add(VeneerStyle.FieldRow("Condition", conditionDropdown, "The test applied to that field.", out _));
            VisualElement valueRow = TextRow("Value", prefix + "ShowIfValue", "What to compare with (a number, text, or an enum's name).");
            conditionBox.Add(valueRow);
            VeneerStyle.SetDisplay(conditionBox, current.Length > 0);
            VeneerStyle.SetDisplay(valueRow, current.Length > 0 && NeedsValue(currentCondition));

            dropdown.RegisterValueChangedCallback(evt =>
            {
                SerializedProperty property = layoutSo.FindProperty(prefix + "ShowIfPath");
                if (property == null)
                {
                    return;
                }

                bool none = dropdown.index <= 0;
                property.stringValue = none ? string.Empty : evt.newValue;
                layoutSo.ApplyModifiedProperties();
                VeneerStyle.SetDisplay(conditionBox, !none);
                SerializedProperty condition = layoutSo.FindProperty(prefix + "ShowIf");
                VeneerStyle.SetDisplay(valueRow, !none && condition != null && NeedsValue((VeneerCondition)condition.enumValueIndex));
            });

            conditionDropdown.RegisterValueChangedCallback(evt =>
            {
                int selected = conditionDropdown.index;
                if (selected < 0)
                {
                    return;
                }

                SerializedProperty property = layoutSo.FindProperty(prefix + "ShowIf");
                if (property != null)
                {
                    property.enumValueIndex = selected;
                    layoutSo.ApplyModifiedProperties();
                }

                // Conditions that need no value (is true / false / assigned / not assigned / empty / not empty) hide the value box.
                VeneerStyle.SetDisplay(valueRow, dropdown.index > 0 && NeedsValue((VeneerCondition)selected));
            });
        }

        private static readonly string[] ConditionNames =
        {
            "equals", "does not equal", "is greater than", "is less than", "is true", "is false",
            "is assigned", "is not assigned", "is empty", "is not empty",
        };

        private static bool NeedsValue(VeneerCondition condition)
        {
            return condition == VeneerCondition.Equals || condition == VeneerCondition.NotEquals
                || condition == VeneerCondition.Greater || condition == VeneerCondition.Less;
        }

        // ------------------------------------------------------------------ property paths

        private static string PagePrefix(int page)
        {
            return "Pages.Array.data[" + CountText(page) + "].";
        }

        private static string BlockPrefix(int page, int block)
        {
            return PagePrefix(page) + "Blocks.Array.data[" + CountText(block) + "].";
        }

        private static string EntryPrefix(int page, int block, int entry)
        {
            return BlockPrefix(page, block) + "Entries.Array.data[" + CountText(entry) + "].";
        }

        // ------------------------------------------------------------------ bound controls

        private bool Bind(IBindable field, string path)
        {
            SerializedProperty property = layoutSo.FindProperty(path);
            if (property == null)
            {
                return false;
            }

            field.BindProperty(property);
            return true;
        }

        private VisualElement TextRow(string label, string path, string tooltip)
        {
            var field = new TextField();
            Bind(field, path);
            return VeneerStyle.FieldRow(label, field, tooltip, out _);
        }

        private Toggle MakeToggle(string path)
        {
            var toggle = new Toggle();
            Bind(toggle, path);
            return toggle;
        }

        private VisualElement ToggleRow(string label, string path, string tooltip)
        {
            return VeneerStyle.FieldRow(label, MakeToggle(path), tooltip, out _);
        }

        private VisualElement FloatRow(string label, string path, string tooltip)
        {
            var field = new FloatField();
            field.AddToClassList("vn-number");
            Bind(field, path);
            return VeneerStyle.FieldRow(label, field, tooltip, out _);
        }

        private VisualElement EnumRow(string label, string path, Enum defaultValue, string tooltip)
        {
            var field = new EnumField(defaultValue);
            field.AddToClassList("vnd-control--capped");
            Bind(field, path);
            return VeneerStyle.FieldRow(label, field, tooltip, out _);
        }

        private VisualElement ObjectRow(string label, string path, Type type, string tooltip)
        {
            var field = new ObjectField { objectType = type, allowSceneObjects = false };
            field.AddToClassList("vnd-control--capped");
            Bind(field, path);
            return VeneerStyle.FieldRow(label, field, tooltip, out _);
        }

        // An icon: the current kit glyph, a button that opens the shared picker, a clear button, and the user's own picture below.
        private VisualElement IconRow(string label, string iconPath, string texturePath)
        {
            var box = new VisualElement();
            SerializedProperty iconProperty = layoutSo.FindProperty(iconPath);
            string currentName = iconProperty != null ? iconProperty.stringValue : string.Empty;

            var line = new VisualElement();
            line.AddToClassList("vnd-icon-row");
            VisualElement preview = VeneerStyle.IconElement(currentName, "vn-icon--accent", Brand);
            Label nameLabel = VeneerStyle.EllipsisLabel(string.IsNullOrEmpty(currentName) ? "(no icon)" : currentName);
            line.Add(preview);
            line.Add(nameLabel);

            Action<string> write = chosenName =>
            {
                SerializedProperty property = layoutSo.FindProperty(iconPath);
                if (property == null)
                {
                    return;
                }

                property.stringValue = chosenName ?? string.Empty;
                layoutSo.ApplyModifiedProperties();
                VeneerStyle.SetIcon(preview, chosenName, Brand);
                nameLabel.text = string.IsNullOrEmpty(chosenName) ? "(no icon)" : chosenName;
            };

            Button pick = VeneerStyle.Button("Pick", "grid", "Choose one of the kit's icons.", null, false, Brand);
            pick.clicked += () =>
            {
                if (icons == null)
                {
                    icons = new VeneerDesignerIcons(Brand);
                }

                if (icons.Element.parent == box)
                {
                    icons.Element.RemoveFromHierarchy();
                    return;
                }

                SerializedProperty property = layoutSo.FindProperty(iconPath);
                icons.Show(property != null ? property.stringValue : string.Empty, chosenName =>
                {
                    write(chosenName);
                    icons.Element.RemoveFromHierarchy();
                });
                box.Add(icons.Element);
            };
            line.Add(pick);
            line.Add(VeneerStyle.IconButton("close", "No icon", () => write(string.Empty), Brand));
            box.Add(VeneerStyle.FieldRow(label, line, "A kit icon, or your own picture below (the picture wins).", out _));
            if (texturePath != null)
            {
                box.Add(ObjectRow("Own picture", texturePath, typeof(Texture2D), "Your own icon picture (square is best). It replaces the kit icon."));
            }

            return box;
        }
    }
}
