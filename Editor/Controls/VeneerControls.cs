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
            SliderVisuals visuals;
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
                SliderVisuals made = SliderVisuals.Attach(bar, bar.lowValue, bar.highValue);
                bar.RegisterValueChangedCallback(evt => made.Show(evt.newValue));
                visuals = made;
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
                SliderVisuals made = SliderVisuals.Attach(bar, bar.lowValue, bar.highValue);
                bar.RegisterValueChangedCallback(evt => made.Show(evt.newValue));
                visuals = made;
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

            // The fill follows the property itself (not the slider's value), so Undo, Redo, prefab reverts and the first bind are covered.
            VeneerBinding.Hook(block, property, title, block, () => visuals.Sync(property));
            return block;
        }

        /// <summary>
        /// The moving parts a <see cref="SliderRow"/> adds to a stock slider: a fill inside the track whose width follows the value, and the
        /// <c>vn-slider--held</c> class while the pointer is down on the slider (the style sheet grows and lights the handle with it; the
        /// stock slider does not reliably expose <c>:active</c> on its handle). Nothing runs per frame: every update is an event.
        /// </summary>
        private sealed class SliderVisuals
        {
            private const string TrackerClass = "unity-base-slider__tracker";
            private const string HeldClass = "vn-slider--held";

            private readonly VisualElement slider;
            private readonly VisualElement fill;
            private readonly float low;
            private readonly float high;
            private float shown = -1f;

            private SliderVisuals(VisualElement slider, VisualElement fill, float low, float high)
            {
                this.slider = slider;
                this.fill = fill;
                this.low = low;
                this.high = high;
            }

            /// <summary>Marks the slider, adds the fill to its track (when the stock slider has one) and starts following the pointer.</summary>
            public static SliderVisuals Attach(VisualElement slider, float low, float high)
            {
                slider.AddToClassList("vn-slider");
                VisualElement fill = null;
                VisualElement tracker = slider.Q<VisualElement>(className: TrackerClass);
                if (tracker != null)
                {
                    fill = new VisualElement { name = "vn-slider-fill", pickingMode = PickingMode.Ignore };
                    fill.AddToClassList("vn-slider__fill");
                    tracker.Add(fill);
                }

                var visuals = new SliderVisuals(slider, fill, low, high);
                // Trickle phase: the slider's own dragger stops the event before it would bubble up to this element.
                slider.RegisterCallback<PointerDownEvent>(visuals.OnPointerDown, TrickleDown.TrickleDown);
                slider.RegisterCallback<PointerUpEvent>(visuals.OnPointerUp, TrickleDown.TrickleDown);
                slider.RegisterCallback<PointerCancelEvent>(visuals.OnPointerCancel, TrickleDown.TrickleDown);
                slider.RegisterCallback<PointerCaptureOutEvent>(visuals.OnCaptureOut, TrickleDown.TrickleDown);
                slider.RegisterCallback<PointerMoveEvent>(visuals.OnPointerMove, TrickleDown.TrickleDown);
                slider.RegisterCallback<DetachFromPanelEvent>(visuals.OnDetach);
                return visuals;
            }

            /// <summary>Sets the fill from the property: hidden when the selected objects differ, else the property's value.</summary>
            public void Sync(SerializedProperty property)
            {
                if (property.hasMultipleDifferentValues)
                {
                    Apply(0f);
                    return;
                }

                Show(property.propertyType == SerializedPropertyType.Integer ? property.intValue : property.floatValue);
            }

            /// <summary>Sets the fill width for a value (clamped to the slider's ends).</summary>
            public void Show(float value)
            {
                float range = high - low;
                float fraction = range > 0f && !float.IsNaN(value) ? Mathf.Clamp01((value - low) / range) : 0f;
                Apply(fraction);
            }

            private void Apply(float fraction)
            {
                if (fill == null || Mathf.Approximately(fraction, shown))
                {
                    return;
                }

                shown = fraction;
                fill.style.width = new Length(fraction * 100f, LengthUnit.Percent);
            }

            private void OnPointerDown(PointerDownEvent evt)
            {
                if (evt.button == 0)
                {
                    slider.AddToClassList(HeldClass);
                }
            }

            private void OnPointerUp(PointerUpEvent evt)
            {
                if (evt.button == 0)
                {
                    Release();
                }
            }

            private void OnPointerCancel(PointerCancelEvent evt)
            {
                Release();
            }

            private void OnCaptureOut(PointerCaptureOutEvent evt)
            {
                Release();
            }

            // A missed release (the pointer let go outside the window) is repaired by the first move without a pressed button.
            private void OnPointerMove(PointerMoveEvent evt)
            {
                if (evt.pressedButtons == 0 && slider.ClassListContains(HeldClass))
                {
                    Release();
                }
            }

            private void OnDetach(DetachFromPanelEvent evt)
            {
                Release();
            }

            private void Release()
            {
                slider.RemoveFromClassList(HeldClass);
            }
        }

        /// <summary>
        /// A two-handle slider block that edits a Vector2 property as a lowest (x) and a highest (y) value: the label and the two numbers
        /// ("x to y", with the unit) on the first line, the slider below it and optional end captions under it. It follows Undo, Redo, prefab
        /// reverts and several selected objects (mixed values show "different values").
        /// </summary>
        /// <param name="property">A Vector2 property.</param>
        /// <param name="label">The label; null uses the property's display name.</param>
        /// <param name="low">The lowest value the slider can reach.</param>
        /// <param name="high">The highest value the slider can reach.</param>
        /// <param name="leftEnd">Caption under the slider's start; may be null.</param>
        /// <param name="rightEnd">Caption under the slider's end; may be null.</param>
        /// <param name="unit">Unit text after the numbers (for example <c>deg</c>); may be null.</param>
        public static VisualElement MinMaxRow(
            SerializedProperty property, string label = null, float low = 0f, float high = 1f, string leftEnd = null, string rightEnd = null, string unit = null)
        {
            SerializedProperty tracked = property.Copy();
            if (high <= low)
            {
                high = low + 1f;
            }

            var block = new VisualElement();
            block.AddToClassList("vn-slider-row");
            block.tooltip = tracked.tooltip;

            var top = new VisualElement();
            top.AddToClassList("vn-slider-row__top");
            var title = new Label(label ?? tracked.displayName);
            title.AddToClassList("vn-field-row__label");
            top.Add(title);
            var readout = new Label();
            readout.AddToClassList("vn-label-muted");
            top.Add(readout);
            block.Add(top);

            Vector2 start = tracked.vector2Value;
            var slider = new MinMaxSlider(start.x, start.y, low, high);
            slider.AddToClassList("vn-slider-row__slider");
            slider.tooltip = tracked.tooltip;
            slider.RegisterValueChangedCallback(evt =>
            {
                tracked.vector2Value = evt.newValue;
                tracked.serializedObject.ApplyModifiedProperties();
            });
            block.Add(slider);

            if (!string.IsNullOrEmpty(leftEnd) || !string.IsNullOrEmpty(rightEnd))
            {
                var ends = new VisualElement();
                ends.AddToClassList("vn-slider-row__ends");
                ends.Add(VeneerStyle.MutedLabel(leftEnd ?? string.Empty));
                ends.Add(VeneerStyle.MutedLabel(rightEnd ?? string.Empty));
                block.Add(ends);
            }

            // The readout text is made only when the shown numbers change, so a refresh that finds the same value allocates nothing.
            Vector2 shown = new Vector2(float.NaN, float.NaN);
            bool mixedShown = false;
            string suffix = string.IsNullOrEmpty(unit) ? string.Empty : " " + unit;
            VeneerBinding.Hook(block, tracked, title, block, () =>
            {
                bool mixed = tracked.hasMultipleDifferentValues;
                Vector2 value = tracked.vector2Value;
                slider.SetValueWithoutNotify(value);
                if (mixed != mixedShown || value != shown)
                {
                    mixedShown = mixed;
                    shown = value;
                    readout.text = mixed ? "different values" : value.x.ToString("0.##") + " to " + value.y.ToString("0.##") + suffix;
                }
            });
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

        /// <summary>
        /// A kit field row (label plus an animated <see cref="VeneerDropdown"/>) bound to an enum property. It shows the enum's display names
        /// and writes the chosen member (for an enum with non-sequential values the real value is written, looked up through the member
        /// names). With several selected objects that differ it shows an em dash and no selection. Undo, prefab overrides and the tooltip work
        /// like the other rows. A <c>[Flags]</c> enum is not handled here (use <see cref="FlagChips"/> or the native field).
        /// </summary>
        /// <param name="property">An enum property that is not a flags enum.</param>
        /// <param name="label">The label; null uses the property's display name.</param>
        /// <param name="brand">Brand for icon lookup; may be null.</param>
        public static VisualElement EnumDropdown(SerializedProperty property, string label = null, VeneerBrand brand = null)
        {
            if (property.propertyType != SerializedPropertyType.Enum)
            {
                throw new ArgumentException("EnumDropdown needs an enum property, not '" + property.propertyPath + "'.", nameof(property));
            }

            property = property.Copy();
            string[] display = property.enumDisplayNames;
            int[] values = EnumValueTable(property, display.Length);
            VisualElement dropdown = VeneerDropdown.Create(display, -1, index =>
            {
                if (values != null)
                {
                    property.intValue = values[index];
                }
                else
                {
                    property.enumValueIndex = index;
                }

                property.serializedObject.ApplyModifiedProperties();
            }, brand);
            dropdown.tooltip = property.tooltip;
            var field = (VeneerDropdownField)dropdown;

            VisualElement row = VeneerStyle.FieldRow(label ?? property.displayName, dropdown, property.tooltip, out Label title);
            VeneerBinding.Hook(row, property, title, row, () =>
            {
                field.SetValue(property.hasMultipleDifferentValues ? -1 : EnumIndexOf(property, values));
            });
            return row;
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
            StabilizeListLabels(list);
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

        private const float ListLabelWidth = 112f;
        private const long ListLabelTickMilliseconds = 80;
        private const int ListLabelTicks = 8;

        // Unity aligns the label of a PropertyField (the "Element 0" of a list row) from the size of the inspector around it, and it does so when
        // the field is first laid out: a row added after the list was built measures a container that is not laid out yet, so its label ends up
        // wider or narrower than its neighbours until the Inspector is opened again. The labels of the rows are therefore given one fixed width and
        // the field's own alignment is switched off. The fix runs a few times after every change of the list's size, because the field inside a row
        // is built a moment after the row exists.
        private static void StabilizeListLabels(ListView list)
        {
            int remaining = 0;
            IVisualElementScheduledItem job = null;

            void Fix()
            {
                list.Query<Label>(className: "unity-base-field__label").ForEach(label =>
                {
                    VisualElement field = label.parent;
                    if (field == null || !field.ClassListContains("unity-base-field__aligned") || IsInsideCompositeField(field, list))
                    {
                        return;
                    }

                    field.RemoveFromClassList("unity-base-field__aligned");
                    label.style.minWidth = ListLabelWidth;
                    label.style.width = ListLabelWidth;
                    label.style.flexShrink = 0f;
                    label.style.flexGrow = 0f;
                });
            }

            void Tick()
            {
                Fix();
                remaining--;
                if (remaining <= 0 && job != null)
                {
                    job.Pause();
                }
            }

            list.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                remaining = ListLabelTicks;
                if (job == null)
                {
                    job = list.schedule.Execute(Tick).Every(ListLabelTickMilliseconds);
                }
                else
                {
                    job.Resume();
                }
            });
            list.RegisterCallback<DetachFromPanelEvent>(evt =>
            {
                remaining = 0;
                job?.Pause();
            });
        }

        // A vector or rect field has labels for its parts; only the label of a field of its own is fixed.
        private static bool IsInsideCompositeField(VisualElement field, VisualElement stopAt)
        {
            for (VisualElement current = field.parent; current != null && current != stopAt; current = current.parent)
            {
                if (current.ClassListContains("unity-composite-field") || current.ClassListContains("unity-base-compositefield__field"))
                {
                    return true;
                }
            }

            return false;
        }

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

        // The stored value of each display name, found through the C# enum (names are parsed, so the order of the lists does not matter).
        // Null when the enum type cannot be resolved or a value does not fit an int: the member index is then the stored value.
        private static int[] EnumValueTable(SerializedProperty property, int count)
        {
            Type enumType = VeneerFieldInfo.GetValueType(property);
            string[] names = property.enumNames;
            if (enumType == null || !enumType.IsEnum || names.Length != count)
            {
                return null;
            }

            var table = new int[count];
            bool sequential = true;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    table[i] = Convert.ToInt32(Enum.Parse(enumType, names[i]));
                    sequential &= table[i] == i;
                }
            }
            catch (Exception exception) when (exception is ArgumentException || exception is OverflowException || exception is FormatException)
            {
                return null;
            }

            return sequential ? null : table;
        }

        private static int EnumIndexOf(SerializedProperty property, int[] values)
        {
            if (values == null)
            {
                return property.enumValueIndex;
            }

            int stored = property.intValue;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == stored)
                {
                    return i;
                }
            }

            return -1;
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
