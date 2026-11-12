using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    // The "Item design" card of the Properties pane: designs the inside of a list whose elements are a [Serializable] class
    // (VeneerLayout.Items). The element's fields are read from the sample element of the temporary instance. Edits of the model are direct
    // (Undo.RecordObject + LayoutEdited), the text and switch rows of the design are bound to the layout like every other property.
    public sealed partial class VeneerDesignerWindow
    {
        // One top-level field of a list element (a leaf, a nested struct or a nested list).
        private sealed class ElementField
        {
            public string Relative;
            public string Display;
            public bool IsString;
            public bool IsObject;
            public bool IsDesignableList;
        }

        private const string ItemsPrefix = "Items.Array.data[";
        private const string NoTitleField = "(none: Item n)";

        // The list of a class chosen in the Fields pane that is in no card (NodeKind.OtherList); not saved with the window.
        private string selFieldPath;

        // What the Item design card shows: the placed list it started from, the list being designed now (the root or a nested one) and the
        // selected field of its element (relative path, or null).
        private string idRoot;
        private string idList;
        private string idField;

        private readonly List<ElementField> elementFields = new List<ElementField>(16);

        // ------------------------------------------------------------------ what is designable

        // An array or List whose elements are a plain [Serializable] class or struct.
        private static bool IsDesignableProperty(SerializedProperty property)
        {
            return property != null
                && property.isArray
                && property.propertyType != SerializedPropertyType.String
                && IsSampleElementType(VeneerFieldInfo.GetElementType(property));
        }

        private bool IsDesignableList(string path)
        {
            return tempSo != null && !string.IsNullOrEmpty(path) && IsDesignableProperty(tempSo.FindProperty(path));
        }

        // True when the path (indices ignored) lies inside the element of a list that has an item design: such a path is handled by the design.
        private bool IsInsideDesignedItem(string path)
        {
            if (layout == null || layout.Items == null || layout.Items.Count == 0 || string.IsNullOrEmpty(path))
            {
                return false;
            }

            string normalized = VeneerItemPaths.Normalize(path);
            for (int i = 0; i < layout.Items.Count; i++)
            {
                if (layout.Items[i] != null && VeneerItemPaths.IsInside(normalized, layout.Items[i].ListPath))
                {
                    return true;
                }
            }

            return false;
        }

        // The list at <listPath> (indices removed) found through the sample elements of the temporary instance. Null with missing = false
        // when it cannot be told (a list on the way has no sample element); null with missing = true when a part of the path does not exist.
        private SerializedProperty ResolveItemList(string listPath, out bool missing)
        {
            missing = false;
            if (tempSo == null || string.IsNullOrEmpty(listPath))
            {
                return null;
            }

            string[] parts = listPath.Split('.');
            SerializedProperty property = null;
            for (int k = 0; k < parts.Length; k++)
            {
                string actual;
                if (k == 0)
                {
                    actual = parts[0];
                }
                else if (property.isArray && property.propertyType != SerializedPropertyType.String)
                {
                    if (property.arraySize == 0)
                    {
                        return null;
                    }

                    actual = property.GetArrayElementAtIndex(0).propertyPath + "." + parts[k];
                }
                else
                {
                    actual = property.propertyPath + "." + parts[k];
                }

                property = tempSo.FindProperty(actual);
                if (property == null)
                {
                    missing = true;
                    return null;
                }
            }

            return property;
        }

        // Fills elementFields with the top-level fields of the sample element of the list; returns the sample element (null when unknown).
        private SerializedProperty CollectElementFields(string listPath)
        {
            elementFields.Clear();
            SerializedProperty list = ResolveItemList(listPath, out _);
            if (list == null || !list.isArray || list.arraySize == 0)
            {
                return null;
            }

            SerializedProperty element = list.GetArrayElementAtIndex(0);
            SerializedProperty child = element.Copy();
            SerializedProperty end = element.GetEndProperty();
            bool enter = true;
            while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
            {
                enter = false;
                elementFields.Add(new ElementField
                {
                    Relative = child.name,
                    Display = child.displayName,
                    IsString = child.propertyType == SerializedPropertyType.String,
                    IsObject = child.propertyType == SerializedPropertyType.ObjectReference,
                    IsDesignableList = IsDesignableProperty(child),
                });
            }

            return element;
        }

        private ElementField FindElementField(string relative)
        {
            for (int i = 0; i < elementFields.Count; i++)
            {
                if (string.Equals(elementFields[i].Relative, relative, StringComparison.Ordinal))
                {
                    return elementFields[i];
                }
            }

            return null;
        }

        // ------------------------------------------------------------------ selection from the Fields pane

        private void SelectFieldList(string path)
        {
            selKind = NodeKind.OtherList;
            selFieldPath = path;
            selPage = 0;
            selBlock = 0;
            selEntry = 0;
            UpdateSelectionVisuals();
            BuildProperties();
        }

        // A click on an informational row (groups[].header): show the item design of the list that holds the field, with the field selected.
        private void SelectItemField(int fieldIndex)
        {
            int root = fieldIndex;
            while (root >= 0 && fields[root].Info)
            {
                root = fields[root].Parent;
            }

            if (root < 0)
            {
                return;
            }

            FieldItem info = fields[fieldIndex];
            string rootPath = fields[root].Path;
            idRoot = VeneerItemPaths.Normalize(rootPath);
            idList = info.ListPath;
            idField = info.RelPath;
            if (layout != null && layout.FindEntry(rootPath, out _, out _) != null)
            {
                SelectEntryByPath(rootPath);
            }
            else
            {
                SelectFieldList(rootPath);
            }

            BuildProperties();
        }

        // The mark of an informational row: whether the item design shows that field.
        private void InfoMark(FieldItem item, out string markClass, out string text)
        {
            VeneerItemLayout design = layout != null ? layout.FindItem(item.ListPath) : null;
            if (design == null)
            {
                markClass = MarkPartly;
                text = "list item";
                return;
            }

            bool shown = ItemCovers(design, item.RelPath);
            markClass = shown ? MarkPlaced : MarkHidden;
            text = shown ? "item design" : "not shown";
        }

        // True when an entry of the design is the field, a struct around it, or a field inside it.
        private static bool ItemCovers(VeneerItemLayout design, string relative)
        {
            for (int i = 0; i < design.Entries.Count; i++)
            {
                VeneerLayoutEntry entry = design.Entries[i];
                if (entry == null || entry.IsNote || entry.IsCustom || string.IsNullOrEmpty(entry.Path))
                {
                    continue;
                }

                if (entry.Path == relative
                    || relative.StartsWith(entry.Path + ".", StringComparison.Ordinal)
                    || entry.Path.StartsWith(relative + ".", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ the card

        private void BuildItemDesign(string rootPath)
        {
            if (layout == null || string.IsNullOrEmpty(rootPath) || tempSo == null)
            {
                return;
            }

            string root = VeneerItemPaths.Normalize(rootPath);
            bool listInRoot = !string.IsNullOrEmpty(idList)
                && (string.Equals(idList, root, StringComparison.Ordinal) || idList.StartsWith(root + ".", StringComparison.Ordinal));
            if (!string.Equals(idRoot, root, StringComparison.Ordinal) || !listInRoot)
            {
                idRoot = root;
                idList = root;
                idField = null;
            }

            VisualElement card = VeneerStyle.Card("Item design", "list", Brand);
            card.Add(BuildBreadcrumb());

            SerializedProperty sample = CollectElementFields(idList);
            VeneerItemLayout design = layout.FindItem(idList, out int designIndex);
            if (sample == null && design == null)
            {
                card.Add(VeneerStyle.Hint(
                    "The designer has no sample element for this list (it is empty or too deep), so its fields cannot be listed.", "info", Brand));
                propsBody.Add(card);
                return;
            }

            card.Add(VeneerStyle.Chip(
                "Design the items", "layers", design != null, on => SetItemDesign(on), Brand));
            if (design == null)
            {
                Label note = VeneerStyle.MutedLabel("Off: the list is drawn by the plain list editor.");
                note.AddToClassList("vnd-note");
                card.Add(note);
                propsBody.Add(card);
                return;
            }

            string prefix = ItemsPrefix + CountText(designIndex) + "].";
            card.Add(EnumRow("Look", prefix + "Look", VeneerItemLook.Card, "Card: a box per item with a head row. Row: one flat row per item."));
            card.Add(ToggleRow("Drag handle", prefix + "ShowHandle", "A handle that reorders the items by dragging."));
            card.Add(ToggleRow("Remove button", prefix + "ShowRemove", "A button that removes the item."));
            card.Add(ToggleRow("Fold arrow", prefix + "ShowFold", "A card can be folded to its head (Card look only)."));
            card.Add(ToggleRow("Count pill", prefix + "ShowCount", "The size of the item's first list, in the card head (Card look only)."));
            card.Add(ToggleRow("Add button under the items", prefix + "AddAtEnd", "Off: the list has a header line with its title, count and Add button. On: no header, the Add button sits under the items."));
            card.Add(BuildTitleChoice(prefix, design));
            card.Add(TextRow("Add button", prefix + "AddLabel", "Text of the button that adds an item. Empty uses Add."));
            card.Add(TextRow("Empty text", prefix + "EmptyText", "Shown while the list has no item. Empty uses a standard text."));

            card.Add(VeneerStyle.Section("Fields of an item"));
            BuildFieldRows(card, design, designIndex);
            propsBody.Add(card);

            BuildSelectedItemField(sample, design, designIndex);
        }

        // "Groups > Entries": one button per list on the way from the placed list to the list being designed; the last one is the current list.
        private VisualElement BuildBreadcrumb()
        {
            VisualElement bar = VeneerStyle.Row(true);
            string[] parts = idList.Split('.');
            string accumulated = string.Empty;
            for (int i = 0; i < parts.Length; i++)
            {
                accumulated = i == 0 ? parts[0] : accumulated + "." + parts[i];
                string target = accumulated;
                string display = i == 0 && fieldNames.TryGetValue(accumulated, out string known) ? known : ObjectNames.NicifyVariableName(parts[i]);
                bool beforeRoot = accumulated.Length < idRoot.Length;
                if (i == parts.Length - 1 || beforeRoot)
                {
                    Label last = VeneerStyle.MutedLabel(display);
                    if (i == parts.Length - 1)
                    {
                        last.style.unityFontStyleAndWeight = FontStyle.Bold;
                    }

                    bar.Add(last);
                }
                else
                {
                    bar.Add(VeneerStyle.Button(display, null, "Back to " + display, () => GoToItemList(target), false, Brand));
                }

                if (i < parts.Length - 1)
                {
                    bar.Add(VeneerStyle.IconElement("chevron-right", "vn-icon--sm", Brand));
                }
            }

            return bar;
        }

        private void GoToItemList(string listPath)
        {
            idList = listPath;
            idField = null;
            BuildProperties();
        }

        // The title field of a card head: a dropdown over the element's text fields.
        private VisualElement BuildTitleChoice(string prefix, VeneerItemLayout design)
        {
            var choices = new List<string>(elementFields.Count + 1) { NoTitleField };
            for (int i = 0; i < elementFields.Count; i++)
            {
                if (elementFields[i].IsString)
                {
                    choices.Add(elementFields[i].Relative);
                }
            }

            int selected = string.IsNullOrEmpty(design.TitlePath) ? 0 : choices.IndexOf(design.TitlePath);
            if (selected < 0)
            {
                choices.Add(design.TitlePath);
                selected = choices.Count - 1;
            }

            var dropdown = (VeneerDropdownField)VeneerDropdown.Create(choices, selected, null, Brand);
            dropdown.AddToClassList("vnd-control--capped");
            dropdown.Changed += picked =>
            {
                SerializedProperty property = layoutSo.FindProperty(prefix + "TitlePath");
                if (property == null || picked < 0)
                {
                    return;
                }

                property.stringValue = picked == 0 ? string.Empty : choices[picked];
                layoutSo.ApplyModifiedProperties();
            };
            return VeneerStyle.FieldRow("Title field", dropdown, "The text field edited in the head of a card. None shows \"Item n\".", out _);
        }

        // The ordered list of the element's fields: first the ones the design shows (in drawing order), then the ones it does not.
        private void BuildFieldRows(VisualElement card, VeneerItemLayout design, int designIndex)
        {
            List<VeneerLayoutEntry> entries = design.Entries;
            for (int e = 0; e < entries.Count; e++)
            {
                VeneerLayoutEntry entry = entries[e];
                if (entry == null)
                {
                    continue;
                }

                string relative = entry.Path ?? string.Empty;
                ElementField field = FindElementField(relative);
                string name = entry.IsNote
                    ? "Note: " + FirstLine(entry.NoteText)
                    : (!string.IsNullOrEmpty(entry.Label) ? entry.Label : (field != null ? field.Display : relative));
                card.Add(MakeFieldRow(design, designIndex, relative, name, e, entries.Count, field == null && !entry.IsNote));
            }

            for (int f = 0; f < elementFields.Count; f++)
            {
                if (!DesignHasPath(design, elementFields[f].Relative))
                {
                    card.Add(MakeFieldRow(design, designIndex, elementFields[f].Relative, elementFields[f].Display, -1, entries.Count, false));
                }
            }
        }

        private static bool DesignHasPath(VeneerItemLayout design, string relative)
        {
            for (int i = 0; i < design.Entries.Count; i++)
            {
                if (design.Entries[i] != null && string.Equals(design.Entries[i].Path, relative, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // One row: the field's name (click to select), shown, up, down, same row.
        private VisualElement MakeFieldRow(
            VeneerItemLayout design, int designIndex, string relative, string name, int entryIndex, int entryCount, bool missing)
        {
            bool shown = entryIndex >= 0;
            bool selected = shown && string.Equals(idField, relative, StringComparison.Ordinal) && !string.IsNullOrEmpty(relative);
            var row = new VisualElement();
            row.AddToClassList("vnd-row");
            row.EnableInClassList("vnd-row--selected", selected);
            row.EnableInClassList("vnd-row--missing", missing);
            row.tooltip = relative + (missing ? " (missing from the script)" : string.Empty);

            var label = new Label(name);
            label.AddToClassList("vnd-row__label");
            if (!shown)
            {
                label.style.opacity = 0.55f;
            }

            row.Add(label);
            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is Button || !shown || string.IsNullOrEmpty(relative))
                {
                    return;
                }

                idField = relative;
                BuildProperties();
            });

            Button eye = VeneerStyle.IconButton(shown ? "eye" : "eye-off", shown ? "Shown: press to hide it from the items" : "Not shown: press to show it", () => ToggleItemField(designIndex, relative), Brand);
            eye.EnableInClassList("vn-icon-button--on", shown);
            row.Add(eye);
            Button up = VeneerStyle.IconButton("arrow-up", "Move up", () => MoveItemField(designIndex, entryIndex, -1), Brand);
            up.SetEnabled(shown && entryIndex > 0);
            row.Add(up);
            Button down = VeneerStyle.IconButton("arrow-down", "Move down", () => MoveItemField(designIndex, entryIndex, 1), Brand);
            down.SetEnabled(shown && entryIndex < entryCount - 1);
            row.Add(down);
            bool same = shown && design.Entries[entryIndex].SameRow;
            Button link = VeneerStyle.IconButton(same ? "link" : "unlink", "Same line as the field before it", () => ToggleSameRow(designIndex, entryIndex), Brand);
            link.EnableInClassList("vn-icon-button--on", same);
            link.SetEnabled(shown && entryIndex > 0);
            row.Add(link);
            return row;
        }

        // The property controls of the selected field row: the same as a card's field, plus a way down into a nested list's design.
        private void BuildSelectedItemField(SerializedProperty sample, VeneerItemLayout design, int designIndex)
        {
            if (string.IsNullOrEmpty(idField))
            {
                return;
            }

            int entryIndex = -1;
            for (int i = 0; i < design.Entries.Count; i++)
            {
                if (design.Entries[i] != null && string.Equals(design.Entries[i].Path, idField, StringComparison.Ordinal))
                {
                    entryIndex = i;
                    break;
                }
            }

            if (entryIndex < 0)
            {
                return;
            }

            VeneerLayoutEntry entry = design.Entries[entryIndex];
            string prefix = ItemsPrefix + CountText(designIndex) + "].Entries.Array.data[" + CountText(entryIndex) + "].";
            SerializedProperty fieldProperty = sample != null ? sample.FindPropertyRelative(idField) : null;
            ElementField field = FindElementField(idField);

            VisualElement card = VeneerStyle.Card("Item field", "dot", Brand);
            Label pathLabel = VeneerStyle.MutedLabel(design.ListPath + "[]." + idField + (fieldProperty == null && sample != null ? "  (missing from the script)" : string.Empty));
            pathLabel.AddToClassList("vn-ellipsis");
            pathLabel.tooltip = pathLabel.text;
            card.Add(pathLabel);
            if (field != null && field.IsDesignableList)
            {
                string nested = VeneerItemPaths.Join(idList, idField);
                card.Add(VeneerStyle.Button(
                    layout.FindItem(nested) != null ? "Edit the items of " + field.Display : "Design the items of " + field.Display,
                    "list",
                    "Opens the item design of this nested list.",
                    () => GoToItemList(nested),
                    false,
                    Brand));
            }

            AddEntryControls(card, prefix, entry, fieldProperty, true);
            propsBody.Add(card);

            var siblings = new List<string>(elementFields.Count);
            for (int i = 0; i < elementFields.Count; i++)
            {
                siblings.Add(elementFields[i].Relative);
            }

            VisualElement rule = VeneerStyle.Card("Show only when", "filter", Brand);
            BuildShowIf(rule, prefix, idField, entry.ShowIfPath, entry.ShowIf, siblings);
            propsBody.Add(rule);
        }

        // ------------------------------------------------------------------ commands

        private void SetItemDesign(bool on)
        {
            if (!Editable || string.IsNullOrEmpty(idList))
            {
                return;
            }

            VeneerItemLayout existing = layout.FindItem(idList, out int index);
            Undo.RecordObject(layout, "Item design");
            if (on && existing == null)
            {
                CollectElementFields(idList);
                var created = new VeneerItemLayout { ListPath = idList };
                for (int i = 0; i < elementFields.Count; i++)
                {
                    ElementField field = elementFields[i];
                    var entry = new VeneerLayoutEntry { Path = field.Relative };
                    if (field.IsObject)
                    {
                        // The object widget shows the picture; the automatic one is a plain object field.
                        entry.Widget = VeneerWidget.Object;
                    }

                    created.Entries.Add(entry);
                    if (string.IsNullOrEmpty(created.TitlePath) && field.IsString && IsTitleName(field.Relative))
                    {
                        created.TitlePath = field.Relative;
                    }
                }

                layout.Items.Add(created);
            }
            else if (!on && existing != null)
            {
                layout.Items.RemoveAt(index);
                idField = null;
            }

            LayoutEdited();
        }

        private static bool IsTitleName(string name)
        {
            return name.Equals("name", StringComparison.OrdinalIgnoreCase)
                || name.Equals("header", StringComparison.OrdinalIgnoreCase)
                || name.Equals("title", StringComparison.OrdinalIgnoreCase)
                || name.Equals("label", StringComparison.OrdinalIgnoreCase);
        }

        private void ToggleItemField(int designIndex, string relative)
        {
            if (!Editable || designIndex < 0 || designIndex >= layout.Items.Count || string.IsNullOrEmpty(relative))
            {
                return;
            }

            VeneerItemLayout design = layout.Items[designIndex];
            Undo.RecordObject(layout, "Show item field");
            int existing = -1;
            for (int i = 0; i < design.Entries.Count; i++)
            {
                if (design.Entries[i] != null && string.Equals(design.Entries[i].Path, relative, StringComparison.Ordinal))
                {
                    existing = i;
                    break;
                }
            }

            if (existing >= 0)
            {
                design.Entries.RemoveAt(existing);
                if (string.Equals(idField, relative, StringComparison.Ordinal))
                {
                    idField = null;
                }
            }
            else
            {
                ElementField field = FindElementField(relative);
                var entry = new VeneerLayoutEntry { Path = relative };
                if (field != null && field.IsObject)
                {
                    entry.Widget = VeneerWidget.Object;
                }

                design.Entries.Add(entry);
            }

            LayoutEdited();
        }

        private void MoveItemField(int designIndex, int entryIndex, int direction)
        {
            if (!Editable || designIndex < 0 || designIndex >= layout.Items.Count)
            {
                return;
            }

            List<VeneerLayoutEntry> entries = layout.Items[designIndex].Entries;
            int to = entryIndex + direction;
            if (entryIndex < 0 || to < 0 || to >= entries.Count)
            {
                return;
            }

            Undo.RecordObject(layout, "Move item field");
            SwapAt(entries, entryIndex, to);
            LayoutEdited();
        }

        private void ToggleSameRow(int designIndex, int entryIndex)
        {
            if (!Editable || designIndex < 0 || designIndex >= layout.Items.Count)
            {
                return;
            }

            List<VeneerLayoutEntry> entries = layout.Items[designIndex].Entries;
            if (entryIndex <= 0 || entryIndex >= entries.Count)
            {
                return;
            }

            Undo.RecordObject(layout, "Same row");
            entries[entryIndex].SameRow = !entries[entryIndex].SameRow;
            LayoutEdited();
        }

        // ------------------------------------------------------------------ stale item designs

        // A design is stale when its list no longer exists; a field of it is stale when the sample element no longer has it. Nothing can be
        // said about a list on the way that has no sample element, so those are never counted.
        private bool IsStaleItemList(VeneerItemLayout item, out SerializedProperty list)
        {
            list = ResolveItemList(item.ListPath, out bool missing);
            if (missing)
            {
                return true;
            }

            return list != null && (!list.isArray || list.propertyType == SerializedPropertyType.String);
        }

        private static bool IsStaleItemEntry(SerializedProperty element, VeneerLayoutEntry entry)
        {
            return element != null && !entry.IsNote && !entry.IsCustom && !string.IsNullOrEmpty(entry.Path) && element.FindPropertyRelative(entry.Path) == null;
        }

        private int CountStaleItems()
        {
            int count = 0;
            for (int i = 0; i < layout.Items.Count; i++)
            {
                VeneerItemLayout item = layout.Items[i];
                if (item == null)
                {
                    continue;
                }

                if (IsStaleItemList(item, out SerializedProperty list))
                {
                    count++;
                    continue;
                }

                SerializedProperty element = list != null && list.arraySize > 0 ? list.GetArrayElementAtIndex(0) : null;
                for (int e = 0; element != null && e < item.Entries.Count; e++)
                {
                    if (item.Entries[e] != null && IsStaleItemEntry(element, item.Entries[e]))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private void AppendStaleItemNames(StringBuilder builder, ref int added)
        {
            for (int i = 0; i < layout.Items.Count && added < 4; i++)
            {
                VeneerItemLayout item = layout.Items[i];
                if (item == null)
                {
                    continue;
                }

                if (IsStaleItemList(item, out SerializedProperty list))
                {
                    builder.Append(added > 0 ? ", " : string.Empty).Append(item.ListPath ?? string.Empty);
                    added++;
                    continue;
                }

                SerializedProperty element = list != null && list.arraySize > 0 ? list.GetArrayElementAtIndex(0) : null;
                for (int e = 0; element != null && e < item.Entries.Count && added < 4; e++)
                {
                    if (item.Entries[e] != null && IsStaleItemEntry(element, item.Entries[e]))
                    {
                        builder.Append(added > 0 ? ", " : string.Empty).Append(item.ListPath).Append("[].").Append(item.Entries[e].Path);
                        added++;
                    }
                }
            }
        }

        private void RemoveStaleItems()
        {
            for (int i = layout.Items.Count - 1; i >= 0; i--)
            {
                VeneerItemLayout item = layout.Items[i];
                if (item == null)
                {
                    layout.Items.RemoveAt(i);
                    continue;
                }

                if (IsStaleItemList(item, out SerializedProperty list))
                {
                    layout.Items.RemoveAt(i);
                    continue;
                }

                SerializedProperty element = list != null && list.arraySize > 0 ? list.GetArrayElementAtIndex(0) : null;
                for (int e = item.Entries.Count - 1; element != null && e >= 0; e--)
                {
                    if (item.Entries[e] != null && IsStaleItemEntry(element, item.Entries[e]))
                    {
                        item.Entries.RemoveAt(e);
                    }
                }
            }
        }
    }
}
