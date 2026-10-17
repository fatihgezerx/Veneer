using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// The field controls of the kit. Each one edits a <see cref="SerializedProperty"/> and returns a ready element: a label column on the
    /// left, the control to its right. They all (1) work with several selected objects (mixed values show as such), (2) show the
    /// prefab-override look (bold label, "Revert" in the context menu), and (3) follow Undo, Redo and other changes of the data. Pass the
    /// property from the serialized object of the Inspector; the property is copied, so the caller may keep iterating it. Controls that
    /// bind by path (<see cref="ListEditor"/>) bind themselves to the property's serialized object.
    /// </summary>
    public static class VeneerControls
    {
        private const float MixedOpacity = 0.55f;

        // ------------------------------------------------------------------ numbers

        /// <summary>
        /// A number row for a float or int property: the label at the left, an editable number that grows with the row, and an optional unit
        /// at the right. What the user types is clamped to the property's <c>[Min]</c> / <c>[Range]</c> when the field loses focus or Enter
        /// is pressed (a field bound from code does not apply those attributes on its own). The tooltip is the property's own.
        /// </summary>
        /// <param name="property">A float, double or integer property.</param>
        /// <param name="label">The label; null uses the property's display name.</param>
        /// <param name="unit">Short unit text such as <c>m</c> or <c>kg</c>; null shows none.</param>
        public static VisualElement NumberRow(SerializedProperty property, string label = null, string unit = null)
        {
            property = property.Copy();
            VeneerFieldInfo.GetLimits(property, out float min, out float max);
            VisualElement field;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                {
                    var integer = new IntegerField { isDelayed = true };
                    integer.BindProperty(property);
                    integer.RegisterValueChangedCallback(evt =>
                    {
                        int clamped = Mathf.Clamp(evt.newValue, ToInt(min, int.MinValue), ToInt(max, int.MaxValue));
                        if (clamped != evt.newValue)
                        {
                            property.intValue = clamped;
                            property.serializedObject.ApplyModifiedProperties();
                        }
                    });
                    field = integer;
                    break;
                }

                case SerializedPropertyType.Float when property.type == "double":
                {
                    var number = new DoubleField { isDelayed = true };
                    number.BindProperty(property);
                    number.RegisterValueChangedCallback(evt =>
                    {
                        double clamped = Math.Min(Math.Max(evt.newValue, min), max);
                        if (clamped != evt.newValue && !double.IsNaN(clamped))
                        {
                            property.doubleValue = clamped;
                            property.serializedObject.ApplyModifiedProperties();
                        }
                    });
                    field = number;
                    break;
                }

                default:
                {
                    var number = new FloatField { isDelayed = true };
                    number.BindProperty(property);
                    number.RegisterValueChangedCallback(evt => ClampFloat(property, evt.newValue, min, max));
                    field = number;
                    break;
                }
            }

            field.tooltip = property.tooltip;
            field.AddToClassList("vn-number");
            VisualElement row = VeneerStyle.FieldRow(label ?? property.displayName, field, property.tooltip, out Label title);
            if (!string.IsNullOrEmpty(unit))
            {
                var unitLabel = new Label(unit);
                unitLabel.AddToClassList("vn-unit");
                row.Add(unitLabel);
            }

            VeneerBinding.Hook(row, property, title, row, null);
            return row;
        }

        /// <summary>
        /// A slider block for a float or int property: the label, a number field and the unit on the first line, the slider below it and two
        /// soft end captions under the slider. The range comes from the property's <c>[Range]</c> unless <paramref name="low"/> and
        /// <paramref name="high"/> are given. The number field is limited only by <c>[Min]</c> / <c>[Range]</c>, not by the slider's ends.
        /// </summary>
        /// <param name="property">A float or integer property.</param>
        /// <param name="label">The label; null uses the property's display name.</param>
        /// <param name="leftEnd">Caption under the slider's start (for example <c>Dead</c>); may be null.</param>
        /// <param name="rightEnd">Caption under the slider's end (for example <c>Bouncy</c>); may be null.</param>
        /// <param name="low">Slider start; NaN reads the <c>[Range]</c> attribute (0 when there is none).</param>
        /// <param name="high">Slider end; NaN reads the <c>[Range]</c> attribute (1 when there is none).</param>
        /// <param name="unit">Unit text after the number; may be null.</param>
        public static VisualElement SliderRow(
            SerializedProperty property,
            string label = null,
            string leftEnd = null,
            string rightEnd = null,
            float low = float.NaN,
            float high = float.NaN,
            string unit = null)
        {
            property = property.Copy();
            VeneerFieldInfo.GetLimits(property, out float min, out float max);
            if (float.IsNaN(low) || float.IsNaN(high))
            {
                VeneerFieldInfo.TryGetRange(property, out float rangeLow, out float rangeHigh);
                low = float.IsNaN(low) ? rangeLow : low;
                high = float.IsNaN(high) ? rangeHigh : high;
            }

            var block = new VisualElement();
            block.AddToClassList("vn-slider-row");
            block.tooltip = property.tooltip;

            var top = new VisualElement();
            top.AddToClassList("vn-slider-row__top");
            var title = new Label(label ?? property.displayName);
            title.AddToClassList("vn-field-row__label");
            top.Add(title);

            VisualElement number;
            VisualElement slider;
            if (property.propertyType == SerializedPropertyType.Integer)
            {
                var integer = new IntegerField { isDelayed = true };
                integer.BindProperty(property);
                integer.RegisterValueChangedCallback(evt =>
                {
                    int clamped = Mathf.Clamp(evt.newValue, ToInt(min, int.MinValue), ToInt(max, int.MaxValue));
                    if (clamped != evt.newValue)
                    {
                        property.intValue = clamped;
                        property.serializedObject.ApplyModifiedProperties();
                    }
                });
                number = integer;
                var bar = new SliderInt(Mathf.RoundToInt(low), Mathf.RoundToInt(high));
                bar.BindProperty(property);
                slider = bar;
            }
            else
            {
                var floatField = new FloatField { isDelayed = true };
                floatField.BindProperty(property);
                floatField.RegisterValueChangedCallback(evt => ClampFloat(property, evt.newValue, min, max));
                number = floatField;
                var bar = new Slider(low, high);
                bar.BindProperty(property);
                slider = bar;
            }

            number.tooltip = property.tooltip;
            number.AddToClassList("vn-number");
            top.Add(number);
            if (!string.IsNullOrEmpty(unit))
            {
                var unitLabel = new Label(unit);
                unitLabel.AddToClassList("vn-unit");
                top.Add(unitLabel);
            }

            block.Add(top);
            slider.tooltip = property.tooltip;
            slider.AddToClassList("vn-slider-row__slider");
            block.Add(slider);

            if (!string.IsNullOrEmpty(leftEnd) || !string.IsNullOrEmpty(rightEnd))
            {
                var ends = new VisualElement();
                ends.AddToClassList("vn-slider-row__ends");
                ends.Add(VeneerStyle.MutedLabel(leftEnd ?? string.Empty));
                ends.Add(VeneerStyle.MutedLabel(rightEnd ?? string.Empty));
                block.Add(ends);
            }

            VeneerBinding.Hook(block, property, title, block, null);
            return block;
        }

        // ------------------------------------------------------------------ toggles, flags, enums

        /// <summary>
        /// A toggle chip that edits a bool property: a click writes the value to every selected object (one Undo step). When the selected
        /// objects differ the chip is lit but faded; the next click then switches them all off.
        /// </summary>
        /// <param name="property">A bool property.</param>
        /// <param name="label">The chip text; null uses the property's display name.</param>
        /// <param name="icon">Icon name; may be null.</param>
        /// <param name="brand">Brand for icon lookup; may be null.</param>
        public static VisualElement ToggleChip(SerializedProperty property, string label = null, string icon = null, VeneerBrand brand = null)
        {
            property = property.Copy();
            VisualElement chip = VeneerStyle.Chip(label ?? property.displayName, icon, property.boolValue, on =>
            {
                property.boolValue = on;
                property.serializedObject.ApplyModifiedProperties();
            }, brand);
            chip.tooltip = property.tooltip;
            VeneerBinding.Hook(chip, property, VeneerStyle.ChipLabel(chip), chip, () =>
            {
                bool mixed = property.hasMultipleDifferentValues;
                VeneerStyle.SetChipState(chip, mixed || property.boolValue, mixed);
            });
            return chip;
        }

        /// <summary>
        /// A wrapping row of toggle chips for a flags enum property, one chip per single-bit member (combined members and None are left
        /// out). A chip lights when the bit is set; with several selected objects a bit set on only some of them shows lit but faded, and
        /// a click changes just that bit on each object.
        /// </summary>
        /// <param name="property">A flags enum property.</param>
        /// <param name="names">Chip text per single-bit member in ascending order; null uses the member names.</param>
        /// <param name="icons">Icon name per chip in the same order; may be null or shorter.</param>
        /// <param name="brand">Brand for icon lookup; may be null.</param>
        public static VisualElement FlagChips(SerializedProperty property, string[] names = null, string[] icons = null, VeneerBrand brand = null)
        {
            property = property.Copy();
            var bits = new List<int>(8);
            var memberNames = new List<string>(8);
            CollectFlagBits(property, bits, memberNames);

            VisualElement row = VeneerStyle.Row(true);
            row.tooltip = property.tooltip;
            var chips = new VisualElement[bits.Count];
            for (int i = 0; i < bits.Count; i++)
            {
                int bit = bits[i];
                string text = names != null && i < names.Length ? names[i] : ObjectNames.NicifyVariableName(memberNames[i]);
                string icon = icons != null && i < icons.Length ? icons[i] : null;
                VisualElement chip = VeneerStyle.Chip(text, icon, false, on => SetFlag(property, bit, on), brand);
                chips[i] = chip;
                row.Add(chip);
                var menuChip = chip;
                chip.AddManipulator(new ContextualMenuManipulator(evt =>
                {
                    if (property.prefabOverride)
                    {
                        evt.menu.AppendAction("Revert", action => PrefabUtility.RevertPropertyOverride(property, InteractionMode.UserAction));
                    }
                }));
                menuChip.tooltip = property.tooltip;
            }

            VeneerBinding.Hook(row, property, null, null, () =>
            {
                bool overridden = property.prefabOverride;
                for (int i = 0; i < chips.Length; i++)
                {
                    int count = CountWithBit(property, bits[i]);
                    int total = property.serializedObject.targetObjects.Length;
                    VeneerStyle.SetChipState(chips[i], count > 0, count != 0 && count != total);
                    Label chipLabel = VeneerStyle.ChipLabel(chips[i]);
                    if (chipLabel != null)
                    {
                        chipLabel.style.unityFontStyleAndWeight = overridden ? FontStyle.Bold : FontStyle.Normal;
                    }
                }
            });
            return row;
        }

        /// <summary>
        /// A bound enum shown as a segmented group of icon buttons (an entry with no icon shows its name as text). Works for enums of 2-4
        /// members; with several selected objects that differ no button is selected.
        /// </summary>
        /// <param name="property">An enum property.</param>
        /// <param name="icons">Icon name per enum member, in member order; null entries (or a null array) show text.</param>
        /// <param name="tooltips">Tooltip per member; null uses the display names.</param>
        /// <param name="label">A label for the row; null returns just the segmented group without a label column.</param>
        /// <param name="brand">Brand for icon lookup; may be null.</param>
        public static VisualElement EnumSegmented(
            SerializedProperty property, string[] icons = null, string[] tooltips = null, string label = null, VeneerBrand brand = null)
        {
            property = property.Copy();
            string[] display = property.enumDisplayNames;
            var iconList = new string[display.Length];
            for (int i = 0; i < iconList.Length; i++)
            {
                iconList[i] = icons != null && i < icons.Length ? icons[i] : null;
            }

            string[] tips = tooltips ?? display;
            VisualElement group = VeneerStyle.SegmentedIcons(iconList, tips, -1, index =>
            {
                property.enumValueIndex = index;
                property.serializedObject.ApplyModifiedProperties();
            }, brand);
            group.tooltip = property.tooltip;

            VisualElement result = group;
            Label title = null;
            if (!string.IsNullOrEmpty(label))
            {
                result = VeneerStyle.FieldRow(label, group, property.tooltip, out title);
                group.style.flexGrow = 0f;
            }

            VeneerBinding.Hook(result, property, title, result, () =>
            {
                VeneerStyle.SetSegmentedSelected(group, property.hasMultipleDifferentValues ? -1 : property.enumValueIndex);
            });
            return result;
        }

        // ------------------------------------------------------------------ vectors, colours, curves

        /// <summary>A label plus a bound vector field for a Vector2 or Vector3 property.</summary>
        public static VisualElement Vector3Row(SerializedProperty property, string label = null)
        {
            property = property.Copy();
            VisualElement field;
            if (property.propertyType == SerializedPropertyType.Vector2)
            {
                var vector2 = new Vector2Field();
                vector2.BindProperty(property);
                field = vector2;
            }
            else
            {
                var vector3 = new Vector3Field();
                vector3.BindProperty(property);
                field = vector3;
            }

            return BoundRow(property, label, field);
        }

        /// <summary>A label plus a bound colour field.</summary>
        public static VisualElement ColorRow(SerializedProperty property, string label = null)
        {
            property = property.Copy();
            var field = new ColorField();
            field.BindProperty(property);
            return BoundRow(property, label, field);
        }

        /// <summary>A label plus a bound animation curve field.</summary>
        public static VisualElement CurveRow(SerializedProperty property, string label = null)
        {
            property = property.Copy();
            var field = new CurveField();
            field.BindProperty(property);
            return BoundRow(property, label, field);
        }

        // ------------------------------------------------------------------ object references

        /// <summary>
        /// An object reference slot: the label, a thumbnail (asset preview or the type's icon), the object field and a Ping button that
        /// highlights the object in the Project window or Hierarchy. Drag and drop work as in any object field.
        /// </summary>
        /// <param name="property">An object reference property.</param>
        /// <param name="label">The label; null uses the property's display name.</param>
        /// <param name="objectType">The accepted type; null reads it from the field's C# type.</param>
        /// <param name="brand">Brand for icon lookup; may be null.</param>
        public static VisualElement ObjectSlot(
            SerializedProperty property, string label = null, Type objectType = null, VeneerBrand brand = null)
        {
            property = property.Copy();
            if (objectType == null)
            {
                Type resolved = VeneerFieldInfo.GetValueType(property);
                objectType = resolved != null && typeof(UnityEngine.Object).IsAssignableFrom(resolved) ? resolved : typeof(UnityEngine.Object);
            }

            var inner = new VisualElement();
            inner.AddToClassList("vn-row");
            var thumb = new VisualElement();
            thumb.AddToClassList("vn-thumb");
            thumb.pickingMode = PickingMode.Ignore;
            inner.Add(thumb);

            var objectField = new ObjectField
            {
                objectType = objectType,
                allowSceneObjects = !EditorUtility.IsPersistent(property.serializedObject.targetObject),
            };
            objectField.style.flexGrow = 1f;
            objectField.style.flexShrink = 1f;
            objectField.style.marginLeft = 0f;
            objectField.style.marginRight = 0f;
            objectField.BindProperty(property);
            inner.Add(objectField);

            Button ping = VeneerStyle.IconButton("cursor", "Ping: highlight the object in the Project window or Hierarchy.", null, brand);
            ping.clicked += () =>
            {
                UnityEngine.Object current = property.objectReferenceValue;
                if (current != null)
                {
                    EditorGUIUtility.PingObject(current);
                }
            };
            inner.Add(ping);
            inner.style.marginRight = 0f;

            int attempts = 0;
            Action updateThumb = null;
            updateThumb = () =>
            {
                UnityEngine.Object current = property.hasMultipleDifferentValues ? null : property.objectReferenceValue;
                ping.SetEnabled(current != null);
                if (current == null)
                {
                    thumb.style.backgroundImage = StyleKeyword.None;
                    return;
                }

                Texture preview = AssetPreview.GetAssetPreview(current);
                if (preview == null)
                {
                    preview = AssetPreview.GetMiniThumbnail(current);
                    // Previews are made in the background; look again a few times.
                    if (attempts < 12 && AssetPreview.IsLoadingAssetPreview(current.GetInstanceID()))
                    {
                        attempts++;
                        thumb.schedule.Execute(updateThumb).StartingIn(150);
                    }
                }

                thumb.style.backgroundImage = preview is Texture2D texture ? new StyleBackground(texture) : StyleKeyword.None;
            };

            VisualElement row = VeneerStyle.FieldRow(label ?? property.displayName, inner, property.tooltip, out Label title);
            VeneerBinding.Hook(row, property, title, row, () =>
            {
                attempts = 0;
                updateThumb();
            });
            return row;
        }

        // ------------------------------------------------------------------ lists

        /// <summary>
        /// A styled, reorderable list for an array or List property: a header with the title, an item-count pill and add / remove icon
        /// buttons, then a bound <see cref="ListView"/> whose items are the elements' own <see cref="PropertyField"/>s (structs show their
        /// fields). Drag an item's handle to reorder; Remove deletes the selected items (or the last one when none is selected). With several
        /// selected objects whose lists differ in size the list is replaced by a note, because one edit would resize them all.
        /// The list binds itself to the property's serialized object.
        /// </summary>
        /// <param name="property">An array or List property.</param>
        /// <param name="label">The title; null uses the property's display name.</param>
        /// <param name="brand">Brand for icon lookup; may be null.</param>
        public static VisualElement ListEditor(SerializedProperty property, string label = null, VeneerBrand brand = null)
        {
            property = property.Copy();
            if (!property.isArray || property.propertyType == SerializedPropertyType.String)
            {
                throw new ArgumentException("ListEditor needs an array or List property, not '" + property.propertyPath + "'.", nameof(property));
            }

            var root = new VisualElement();
            root.AddToClassList("vn-list");
            root.tooltip = property.tooltip;

            var header = new VisualElement();
            header.AddToClassList("vn-list__header");
            var title = new Label(label ?? property.displayName);
            title.AddToClassList("vn-list__title");
            header.Add(title);
            Label count = VeneerStyle.Pill("0", "info");
            header.Add(count);
            header.Add(VeneerStyle.Spacer());

            var list = new ListView
            {
                bindingPath = property.propertyPath,
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                selectionType = SelectionType.Multiple,
                showAddRemoveFooter = false,
                showFoldoutHeader = false,
                showBoundCollectionSize = false,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
            };
            list.AddToClassList("vn-list__view");

            Button add = VeneerStyle.IconButton("plus", "Add an item at the end.", null, brand);
            Button remove = VeneerStyle.IconButton("minus", "Remove the selected items (the last one when none is selected).", null, brand);
            add.clicked += () =>
            {
                property.arraySize++;
                property.serializedObject.ApplyModifiedProperties();
                list.RefreshItems();
            };
            remove.clicked += () => RemoveItems(property, list);
            header.Add(add);
            header.Add(remove);
            root.Add(header);

            Label notice = VeneerStyle.MutedLabel("The selected objects have lists of different sizes. Edit them one at a time.");
            notice.AddToClassList("vn-list__notice");
            VeneerStyle.SetDisplay(notice, false);
            root.Add(notice);
            root.Add(list);

            // Bound here so the list also works outside VeneerInspector; binding the same object again later is harmless.
            list.Bind(property.serializedObject);
            VeneerBinding.Hook(root, property, title, header, () =>
            {
                bool mixed = property.hasMultipleDifferentValues;
                VeneerStyle.SetDisplay(list, !mixed);
                VeneerStyle.SetDisplay(notice, mixed);
                add.SetEnabled(!mixed);
                remove.SetEnabled(!mixed);
                string text = mixed ? "-" : property.arraySize.ToString();
                VeneerStyle.SetPill(count, "info", text);
            });
            return root;
        }

        // ------------------------------------------------------------------ internals

        private static VisualElement BoundRow(SerializedProperty property, string label, VisualElement field)
        {
            field.tooltip = property.tooltip;
            VisualElement row = VeneerStyle.FieldRow(label ?? property.displayName, field, property.tooltip, out Label title);
            VeneerBinding.Hook(row, property, title, row, null);
            return row;
        }

        private static void ClampFloat(SerializedProperty property, float value, float min, float max)
        {
            bool invalid = float.IsNaN(value);
            if (!invalid && value >= min && value <= max)
            {
                return;
            }

            property.floatValue = invalid ? Mathf.Clamp(0f, min, max) : Mathf.Clamp(value, min, max);
            property.serializedObject.ApplyModifiedProperties();
        }

        private static int ToInt(float value, int fallback)
        {
            if (float.IsNegativeInfinity(value) || float.IsPositiveInfinity(value) || float.IsNaN(value))
            {
                return fallback;
            }

            return Mathf.RoundToInt(value);
        }

        private static void RemoveItems(SerializedProperty property, ListView list)
        {
            var indices = new List<int>(list.selectedIndices);
            if (indices.Count == 0 && property.arraySize > 0)
            {
                indices.Add(property.arraySize - 1);
            }

            indices.Sort((a, b) => b.CompareTo(a));
            for (int i = 0; i < indices.Count; i++)
            {
                int index = indices[i];
                if (index < 0 || index >= property.arraySize)
                {
                    continue;
                }

                SerializedProperty element = property.GetArrayElementAtIndex(index);
                // Deleting a non-null object reference only clears it; delete again to remove the slot.
                if (element.propertyType == SerializedPropertyType.ObjectReference && element.objectReferenceValue != null)
                {
                    element.objectReferenceValue = null;
                }

                property.DeleteArrayElementAtIndex(index);
            }

            property.serializedObject.ApplyModifiedProperties();
            list.ClearSelection();
            list.RefreshItems();
        }

        private static void CollectFlagBits(SerializedProperty property, List<int> bits, List<string> names)
        {
            Type enumType = VeneerFieldInfo.GetValueType(property);
            if (enumType != null && enumType.IsEnum)
            {
                Array values = Enum.GetValues(enumType);
                string[] memberNames = Enum.GetNames(enumType);
                for (int i = 0; i < values.Length; i++)
                {
                    int value = Convert.ToInt32(values.GetValue(i));
                    if (value > 0 && (value & (value - 1)) == 0 && !bits.Contains(value))
                    {
                        bits.Add(value);
                        names.Add(memberNames[i]);
                    }
                }

                return;
            }

            string[] fallback = property.enumNames;
            for (int i = 0; i < fallback.Length && i < 31; i++)
            {
                bits.Add(1 << i);
                names.Add(fallback[i]);
            }
        }

        private static int CountWithBit(SerializedProperty property, int bit)
        {
            if (!property.hasMultipleDifferentValues)
            {
                return (property.enumValueFlag & bit) != 0 ? property.serializedObject.targetObjects.Length : 0;
            }

            return VeneerBinding.CountTargets(property, own => (own.enumValueFlag & bit) != 0);
        }

        private static void SetFlag(SerializedProperty property, int bit, bool on)
        {
            if (!property.hasMultipleDifferentValues)
            {
                int value = property.enumValueFlag;
                property.enumValueFlag = on ? value | bit : value & ~bit;
                property.serializedObject.ApplyModifiedProperties();
                return;
            }

            // The objects differ, so one shared value would overwrite their other bits: change the single bit on each object.
            VeneerBinding.ForEachTarget(
                property,
                own => own.enumValueFlag = on ? own.enumValueFlag | bit : own.enumValueFlag & ~bit,
                "Change " + property.displayName);
        }
    }
}
