using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    // Left pane: the serialized fields of the script (top level and, expandable, the fields inside nested serializable structs; read from a
    // temporary instance), the system's custom items, the warnings, and the temporary instance itself.
    public sealed partial class VeneerDesignerWindow
    {
        private sealed class FieldItem
        {
            public string Path;
            public string Display;
            public string TypeLabel;

            // Position in the nesting: 0 = top level; Parent is the index of the parent item in the flat list (-1 for none).
            public int Depth;
            public int Parent = -1;

            // A nested struct with at least one listed child (it gets the expand arrow); Expanded is the arrow's state.
            public bool IsStruct;
            public bool Expanded;

            // Filled by UpdateFieldMarks: where this very path sits, and what it inherits from its ancestors.
            public string OwnCard;
            public bool OwnHidden;
            public string InheritedCard;
            public bool InheritedHidden;

            // Leaf fields below (itself when it is a leaf) and how many of them are placed or hidden (directly or through an ancestor).
            public int LeafTotal;
            public int LeafResolved;
        }

        private sealed class FieldRow
        {
            public VisualElement Element;
            public Button Arrow;
            public Label Name;
            public Label Mark;
            public Button Hide;
            public string ArrowIconName;
            public string HideIconName;
            public int Index;
            public int Depth = -1;
        }

        private sealed class ItemRow
        {
            public VisualElement Element;
            public VisualElement Icon;
            public Label Name;
            public Label Mark;
            public Toggle Header;
            public string IconName;
            public int Index;
        }

        private const string NoteHidden = "hidden";
        private const string NoteUnplaced = "unplaced";
        private const string NotePartly = "partly";
        private const string NoteInParts = "in parts";
        private const string MarkPlaced = "vnd-mark--placed";
        private const string MarkHidden = "vnd-mark--hidden";
        private const string MarkUnplaced = "vnd-mark--unplaced";
        private const string MarkPartly = "vnd-mark--partly";

        // Same limit as the renderer: a struct at nesting depth 0..2 shows its fields, so children sit at depth 1..3.
        private const int MaxFieldDepth = 3;
        private const float FieldIndent = 14f;

        private readonly List<FieldItem> fields = new List<FieldItem>(32);
        private readonly List<FieldRow> fieldRows = new List<FieldRow>(32);
        private readonly HashSet<string> fieldPaths = new HashSet<string>();
        private readonly HashSet<string> expandedPaths = new HashSet<string>();
        private readonly Dictionary<string, string> fieldNames = new Dictionary<string, string>(32);
        private readonly Dictionary<string, string> placedCards = new Dictionary<string, string>(32);
        private readonly Dictionary<string, string> customCards = new Dictionary<string, string>(8);
        private readonly Dictionary<string, string> claimedPaths = new Dictionary<string, string>(8);
        private readonly List<VeneerCustomItem> customItems = new List<VeneerCustomItem>(8);
        private readonly List<ItemRow> itemRows = new List<ItemRow>(8);
        private readonly StringBuilder warnBuilder = new StringBuilder(128);

        private bool[] fieldMatch = new bool[32];
        private bool[] fieldShown = new bool[32];

        private ScrollView fieldScroll;
        private TextField fieldSearch;
        private Label fieldsEmpty;
        private Button placeAllButton;
        private VisualElement itemsBox;
        private ScrollView itemsScroll;
        private string fieldFilter = string.Empty;

        private VisualElement problemHint;
        private Label problemLabel;
        private VisualElement ownEditorHint;
        private Label ownEditorLabel;
        private VisualElement missingScriptHint;
        private VisualElement baseLayoutHint;
        private Label baseLayoutLabel;
        private VisualElement staleHint;
        private Label staleLabel;

        // ------------------------------------------------------------------ the temporary instance

        // Makes the hidden instance whose SerializedObject lists the fields and feeds the preview. User scripts can throw: never let that escape.
        private void BuildTemp()
        {
            fields.Clear();
            fieldPaths.Clear();
            fieldNames.Clear();
            tempProblem = null;
            ownEditorName = null;

            Type type = targetType;
            if (type == null)
            {
                if (script != null && layout == null)
                {
                    tempProblem = "This script has no class Unity can use. The class must be public in a file with the same name, and the project must compile.";
                }

                return;
            }

            if (!typeof(MonoBehaviour).IsAssignableFrom(type) && !typeof(ScriptableObject).IsAssignableFrom(type))
            {
                tempProblem = type.Name + " is not a MonoBehaviour or a ScriptableObject, so it has no Inspector to design.";
                return;
            }

            if (typeof(EditorWindow).IsAssignableFrom(type) || typeof(Editor).IsAssignableFrom(type))
            {
                tempProblem = type.Name + " is an editor class: editors and windows have no layout.";
                return;
            }

            if (type.IsAbstract)
            {
                tempProblem = type.Name + " is abstract: pick a script of a concrete class (a layout on a base class is not supported).";
                return;
            }

            if (type.ContainsGenericParameters)
            {
                tempProblem = type.Name + " is a generic class: Unity cannot create it, so it cannot be designed.";
                return;
            }

            try
            {
                if (typeof(ScriptableObject).IsAssignableFrom(type))
                {
                    ScriptableObject created = CreateInstance(type);
                    if (created != null)
                    {
                        created.hideFlags = HideFlags.HideAndDontSave;
                    }

                    tempObject = created;
                }
                else
                {
                    // CreateGameObjectWithHideFlags keeps the scene from being marked dirty.
                    tempHost = EditorUtility.CreateGameObjectWithHideFlags("Veneer Designer Temp", HideFlags.HideAndDontSave);
                    tempObject = tempHost.AddComponent(type);
                }
            }
            catch (Exception exception)
            {
                tempProblem = "The script threw while an instance was created: " + exception.Message;
                ReleaseTemp();
                return;
            }

            if (tempObject == null)
            {
                tempProblem = "Unity could not create an instance of " + type.Name + ".";
                ReleaseTemp();
                return;
            }

            try
            {
                tempSo = new SerializedObject(tempObject);
                CollectFields();
                ownEditorName = FindOwnEditorName(tempObject);
            }
            catch (Exception exception)
            {
                tempProblem = "The fields of " + type.Name + " could not be read: " + exception.Message;
                ReleaseTemp();
            }
        }

        private void CollectFields()
        {
            SerializedProperty iterator = tempSo.GetIterator();
            bool enter = true;
            while (iterator.NextVisible(enter))
            {
                enter = false;
                if (iterator.propertyPath == "m_Script")
                {
                    continue;
                }

                AddFieldItem(iterator, 0, -1);
            }
        }

        // Adds the property to the flat list (depth-first), and below it the fields of a nested struct (up to MaxFieldDepth levels).
        private void AddFieldItem(SerializedProperty property, int depth, int parent)
        {
            string path = property.propertyPath;
            int index = fields.Count;
            var item = new FieldItem
            {
                Path = path,
                Display = property.displayName,
                TypeLabel = property.isArray && property.propertyType != SerializedPropertyType.String ? "list" : property.propertyType.ToString(),
                Depth = depth,
                Parent = parent,
                Expanded = expandedPaths.Contains(path),
            };
            fields.Add(item);
            fieldPaths.Add(path);
            fieldNames[path] = item.Display;

            if (depth >= MaxFieldDepth || !IsNestedStructProperty(property))
            {
                return;
            }

            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            bool enterChildren = true;
            while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
            {
                enterChildren = false;
                AddFieldItem(child, depth + 1, index);
            }

            if (fields.Count > index + 1)
            {
                item.IsStruct = true;
                item.TypeLabel = "struct";
            }
        }

        // The same test as VeneerAutoInspector.IsNestedStruct: a plain [Serializable] class or struct, not a list, UnityEvent, object or managed reference.
        private static bool IsNestedStructProperty(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.Generic || property.isArray)
            {
                return false;
            }

            if (!VeneerFieldInfo.TryResolve(property, out System.Reflection.FieldInfo field, out Type type) || type == null)
            {
                return false;
            }

            return Attribute.IsDefined(type, typeof(SerializableAttribute), false)
                && !typeof(UnityEngine.Object).IsAssignableFrom(type)
                && !typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(type)
                && field != null;
        }

        // The name of the custom editor Unity picks for the instance, or null when it is one of ours (the generic layout editors or a
        // layout-driven system editor) or Unity's default.
        private static string FindOwnEditorName(UnityEngine.Object target)
        {
            Editor editor = null;
            try
            {
                editor = Editor.CreateEditor(target);
                if (editor == null)
                {
                    return null;
                }

                Type editorType = editor.GetType();
                if (typeof(IVeneerLayoutDriven).IsAssignableFrom(editorType))
                {
                    return null;
                }

                string typeName = editorType.Name;
                // The kit's generic editors are VeneerLayoutBehaviourEditor / VeneerLayoutAssetEditor (any "VeneerLayout*" name counts as ours).
                if (typeName.StartsWith("VeneerLayout", StringComparison.Ordinal) || typeName == "GenericInspector")
                {
                    return null;
                }

                return typeName;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (editor != null)
                {
                    DestroyImmediate(editor);
                }
            }
        }

        private void ReleaseTemp()
        {
            if (tempSo != null)
            {
                tempSo.Dispose();
                tempSo = null;
            }

            if (tempHost != null)
            {
                DestroyImmediate(tempHost);
            }
            else if (tempObject != null)
            {
                DestroyImmediate(tempObject);
            }

            tempHost = null;
            tempObject = null;
        }

        // True when the path names a field of the script: a listed one, or any other the SerializedObject can find (deeper than the list goes).
        private bool PathExists(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return fieldPaths.Contains(path) || (tempSo != null && tempSo.FindProperty(path) != null);
        }

        // The registered custom items of the type (empty without any); read after the lookup of the system's default layout registered them.
        private void CollectItems()
        {
            customItems.Clear();
            if (targetType == null)
            {
                return;
            }

            try
            {
                IReadOnlyList<VeneerCustomItem> registered = VeneerCustomItems.For(targetType);
                if (registered != null)
                {
                    for (int i = 0; i < registered.Count; i++)
                    {
                        customItems.Add(registered[i]);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

                private static bool IsFooterItem(VeneerCustomItem item)
        {
            return item != null && item.Slot == VeneerCustomItemSlot.Footer;
        }

        private static bool IsAfterHeaderItem(VeneerCustomItem item)
        {
            return item != null && item.Slot == VeneerCustomItemSlot.AfterHeader;
        }

        // The word shown at the right of a placed custom entry in the tree: item, header, under header or footer.
        private static string SlotWord(VeneerCustomItem item)
        {
            if (item == null || item.Slot == VeneerCustomItemSlot.Item)
            {
                return "item";
            }

            if (IsFooterItem(item))
            {
                return "footer";
            }

            return IsAfterHeaderItem(item) ? "under header" : "header";
        }

        private VeneerCustomItem FindItem(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (int i = 0; i < customItems.Count; i++)
            {
                if (string.Equals(customItems[i].Id, id, StringComparison.Ordinal))
                {
                    return customItems[i];
                }
            }

            return null;
        }

        // ------------------------------------------------------------------ the pane

        private VisualElement BuildFieldsPane()
        {
            var pane = new VisualElement();
            pane.AddToClassList("vnd-pane");
            pane.Add(VeneerStyle.Section("Fields"));

            VisualElement bar = VeneerStyle.Toolbar();
            bar.Add(VeneerStyle.SearchField(OnFieldSearch, out fieldSearch, "Search fields", Brand));
            pane.Add(bar);

            fieldScroll = new ScrollView(ScrollViewMode.Vertical);
            fieldScroll.AddToClassList("vnd-scroll");
            fieldScroll.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.14f));
            fieldScroll.style.borderTopLeftRadius = 5f;
            fieldScroll.style.borderTopRightRadius = 5f;
            fieldScroll.style.borderBottomLeftRadius = 5f;
            fieldScroll.style.borderBottomRightRadius = 5f;
            pane.Add(fieldScroll);

            fieldsEmpty = VeneerStyle.MutedLabel("No serialized fields.");
            fieldsEmpty.style.marginTop = 6f;
            pane.Add(fieldsEmpty);

            // The system's own pieces (buttons, state pills, computed hints) that are not fields; only when the type registered some.
            itemsBox = new VisualElement();
            itemsBox.AddToClassList("vnd-items");
            itemsBox.Add(VeneerStyle.Section("System items"));
            itemsScroll = new ScrollView(ScrollViewMode.Vertical);
            itemsScroll.AddToClassList("vnd-items__scroll");
            itemsBox.Add(itemsScroll);
            VeneerStyle.SetDisplay(itemsBox, false);
            pane.Add(itemsBox);

            placeAllButton = VeneerStyle.Button(
                "Place all unplaced",
                "plus-circle",
                "Puts every field that is neither placed nor hidden into the selected card (a struct that is partly placed gets its unplaced fields one by one).",
                PlaceAllUnplaced,
                false,
                Brand);
            placeAllButton.style.marginTop = 6f;
            pane.Add(placeAllButton);
            return pane;
        }

        private void OnFieldSearch(string text)
        {
            fieldFilter = text ?? string.Empty;
            ApplyFieldFilter();
        }

        private FieldRow CreateFieldRow()
        {
            var row = new FieldRow();
            var element = new VisualElement();
            element.AddToClassList("vnd-row");
            element.userData = row;
            row.Element = element;

            row.Arrow = VeneerStyle.IconButton("chevron-right", "Show the fields inside", () => ToggleExpanded(row.Index), Brand);
            row.Arrow.AddToClassList("vnd-row__arrow");
            row.ArrowIconName = "chevron-right";
            element.Add(row.Arrow);

            VisualElement grip = VeneerStyle.IconElement("drag-handle", "vn-icon--sm", Brand);
            element.Add(grip);

            row.Name = new Label();
            row.Name.AddToClassList("vnd-row__label");
            row.Name.pickingMode = PickingMode.Ignore;
            element.Add(row.Name);

            row.Mark = new Label();
            row.Mark.AddToClassList("vnd-row__mark");
            row.Mark.pickingMode = PickingMode.Ignore;
            element.Add(row.Mark);

            row.Hide = VeneerStyle.IconButton("eye-off", "Hide this field", () => ToggleHidden(fields[row.Index].Path), Brand);
            element.Add(row.Hide);
            row.HideIconName = "eye-off";

            element.RegisterCallback<PointerDownEvent>(OnFieldRowDown);
            element.RegisterCallback<PointerMoveEvent>(OnRowPointerMove);
            element.RegisterCallback<PointerUpEvent>(OnRowPointerUp);
            element.RegisterCallback<PointerCaptureOutEvent>(OnRowCaptureOut);
            element.RegisterCallback<ClickEvent>(OnFieldRowClick);
            return row;
        }

        // Re-uses the pooled rows: only the count and the texts change when the script changes.
        private void RefreshFieldRows()
        {
            if (fieldScroll == null)
            {
                return;
            }

            while (fieldRows.Count < fields.Count)
            {
                FieldRow created = CreateFieldRow();
                fieldScroll.Add(created.Element);
                fieldRows.Add(created);
            }

            for (int i = 0; i < fieldRows.Count; i++)
            {
                FieldRow row = fieldRows[i];
                row.Index = i;
                if (i < fields.Count)
                {
                    FieldItem item = fields[i];
                    row.Name.text = item.Display;
                    row.Element.tooltip = item.Path + " (" + item.TypeLabel + ")";
                    if (row.Depth != item.Depth)
                    {
                        row.Depth = item.Depth;
                        row.Element.style.paddingLeft = 4f + item.Depth * FieldIndent;
                    }

                    // Hidden (not removed) for a leaf, so the names of siblings line up.
                    row.Arrow.visible = item.IsStruct;
                }
                else
                {
                    VeneerStyle.SetDisplay(row.Element, false);
                }
            }

            ApplyFieldFilter();
        }

        // Which rows show: a row of a collapsed struct is hidden; a search shows every match with the structs that hold it (expanded).
        private void ApplyFieldFilter()
        {
            int count = Math.Min(fields.Count, fieldRows.Count);
            if (fieldMatch.Length < count)
            {
                fieldMatch = new bool[count * 2];
                fieldShown = new bool[count * 2];
            }

            bool filtering = !string.IsNullOrEmpty(fieldFilter);
            if (filtering)
            {
                for (int i = 0; i < count; i++)
                {
                    FieldItem item = fields[i];
                    fieldMatch[i] = item.Display.IndexOf(fieldFilter, StringComparison.OrdinalIgnoreCase) >= 0
                        || item.Path.IndexOf(fieldFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                }

                // Depth-first order: children come after their parent, so one backward pass lifts a match to every ancestor.
                for (int i = count - 1; i >= 0; i--)
                {
                    if (fieldMatch[i] && fields[i].Parent >= 0)
                    {
                        fieldMatch[fields[i].Parent] = true;
                    }
                }
            }

            int shown = 0;
            for (int i = 0; i < count; i++)
            {
                FieldItem item = fields[i];
                bool visible;
                if (filtering)
                {
                    visible = fieldMatch[i];
                }
                else
                {
                    visible = item.Parent < 0 || (fieldShown[item.Parent] && fields[item.Parent].Expanded);
                }

                fieldShown[i] = visible;
                FieldRow row = fieldRows[i];
                VeneerStyle.SetDisplay(row.Element, visible);
                if (visible)
                {
                    shown++;
                }

                if (item.IsStruct)
                {
                    string arrowIcon = filtering || item.Expanded ? "chevron-down" : "chevron-right";
                    if (!string.Equals(row.ArrowIconName, arrowIcon, StringComparison.Ordinal))
                    {
                        row.ArrowIconName = arrowIcon;
                        VeneerStyle.SetIcon(row.Arrow[0], arrowIcon, Brand);
                    }
                }
            }

            fieldsEmpty.text = fields.Count == 0 ? "No serialized fields." : "No field matches.";
            VeneerStyle.SetDisplay(fieldsEmpty, shown == 0);
        }

        private void ToggleExpanded(int index)
        {
            if (index < 0 || index >= fields.Count || !fields[index].IsStruct)
            {
                return;
            }

            FieldItem item = fields[index];
            item.Expanded = !item.Expanded;
            if (item.Expanded)
            {
                expandedPaths.Add(item.Path);
            }
            else
            {
                expandedPaths.Remove(item.Path);
            }

            ApplyFieldFilter();
        }

        // Updates the mark of every row (placed in a card / partly placed / hidden / unplaced) and of every system item. One pass over the
        // layout and two over the field list, no allocation after warm-up.
        private void UpdateFieldMarks()
        {
            if (layout == null)
            {
                return;
            }

            placedCards.Clear();
            customCards.Clear();
            claimedPaths.Clear();
            List<VeneerLayoutPage> pages = layout.Pages;
            for (int p = 0; p < pages.Count; p++)
            {
                List<VeneerLayoutBlock> blocks = pages[p].Blocks;
                for (int b = 0; b < blocks.Count; b++)
                {
                    List<VeneerLayoutEntry> entries = blocks[b].Entries;
                    string card = string.IsNullOrEmpty(blocks[b].Title) ? "(card)" : blocks[b].Title;
                    for (int e = 0; e < entries.Count; e++)
                    {
                        VeneerLayoutEntry entry = entries[e];
                        if (entry.IsNote)
                        {
                            continue;
                        }

                        if (entry.IsCustom)
                        {
                            if (!string.IsNullOrEmpty(entry.CustomId))
                            {
                                customCards[entry.CustomId] = card;
                            }

                            continue;
                        }

                        if (!string.IsNullOrEmpty(entry.Path))
                        {
                            placedCards[entry.Path] = card;
                        }
                    }
                }
            }

            // Fields a placed custom item is responsible for count as placed (marked with the item's label) and are not dragged on their own.
            foreach (KeyValuePair<string, string> placedItem in customCards)
            {
                VeneerCustomItem owner = FindItem(placedItem.Key);
                IReadOnlyList<string> claims = owner != null ? owner.Claims : null;
                for (int c = 0; claims != null && c < claims.Count; c++)
                {
                    if (!string.IsNullOrEmpty(claims[c]))
                    {
                        claimedPaths[claims[c]] = owner.Label;
                    }
                }
            }

            int count = fields.Count;
            bool editable = Editable;

            // Forward pass (a parent comes before its children): own state and what is inherited from the ancestors.
            for (int i = 0; i < count; i++)
            {
                FieldItem item = fields[i];
                item.OwnCard = placedCards.TryGetValue(item.Path, out string own) ? own : null;
                item.OwnHidden = layout.IsHidden(item.Path);
                if (item.Parent >= 0)
                {
                    FieldItem parent = fields[item.Parent];
                    item.InheritedCard = parent.OwnCard ?? parent.InheritedCard;
                    item.InheritedHidden = parent.OwnHidden || parent.InheritedHidden;
                }
                else
                {
                    item.InheritedCard = null;
                    item.InheritedHidden = false;
                }

                if (item.InheritedCard == null && claimedPaths.TryGetValue(item.Path, out string claimant))
                {
                    item.InheritedCard = claimant;
                }

                item.LeafTotal = 0;
                item.LeafResolved = 0;
            }

            // Backward pass: count the leaf fields below every struct and how many of them are settled.
            for (int i = count - 1; i >= 0; i--)
            {
                FieldItem item = fields[i];
                if (!item.IsStruct)
                {
                    item.LeafTotal = 1;
                    item.LeafResolved = item.OwnCard != null || item.OwnHidden || item.InheritedCard != null || item.InheritedHidden ? 1 : 0;
                }

                if (item.Parent >= 0)
                {
                    fields[item.Parent].LeafTotal += item.LeafTotal;
                    fields[item.Parent].LeafResolved += item.LeafResolved;
                }
            }

            for (int i = 0; i < count && i < fieldRows.Count; i++)
            {
                FieldRow row = fieldRows[i];
                FieldItem item = fields[i];
                string iconName = "eye-off";
                string markClass;
                string text;
                if (item.OwnCard != null)
                {
                    markClass = MarkPlaced;
                    text = item.OwnCard;
                }
                else if (item.InheritedCard != null)
                {
                    markClass = MarkPlaced;
                    text = item.InheritedCard;
                }
                else if (item.OwnHidden || item.InheritedHidden)
                {
                    markClass = MarkHidden;
                    text = NoteHidden;
                    iconName = item.OwnHidden ? "eye" : "eye-off";
                }
                else if (item.IsStruct && item.LeafResolved > 0)
                {
                    bool all = item.LeafResolved >= item.LeafTotal;
                    markClass = all ? MarkPlaced : MarkPartly;
                    text = all ? NoteInParts : NotePartly;
                }
                else
                {
                    markClass = MarkUnplaced;
                    text = NoteUnplaced;
                }

                row.Mark.text = text;
                row.Mark.EnableInClassList(MarkPlaced, markClass == MarkPlaced);
                row.Mark.EnableInClassList(MarkHidden, markClass == MarkHidden);
                row.Mark.EnableInClassList(MarkUnplaced, markClass == MarkUnplaced);
                row.Mark.EnableInClassList(MarkPartly, markClass == MarkPartly);
                if (!string.Equals(row.HideIconName, iconName, StringComparison.Ordinal))
                {
                    row.HideIconName = iconName;
                    VeneerStyle.SetIcon(row.Hide[0], iconName, Brand);
                    row.Hide.tooltip = iconName == "eye" ? "Show this field again" : "Hide this field";
                }

                // A field that is part of a placed or hidden struct follows that struct: change the struct instead.
                row.Hide.SetEnabled(editable && item.InheritedCard == null && !item.InheritedHidden);
            }

            UpdateItemMarks();
        }

        private void OnFieldRowDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || evt.target is Button)
            {
                return;
            }

            var element = (VisualElement)evt.currentTarget;
            var row = (FieldRow)element.userData;
            if (row.Index >= fields.Count || !Editable)
            {
                return;
            }

            FieldItem item = fields[row.Index];
            // A field inside a placed (or hidden) struct is already handled by its struct.
            if (item.InheritedCard != null || item.InheritedHidden)
            {
                return;
            }

            BeginPending(new DragSource { Kind = DragKind.Field, Path = item.Path, Label = item.Display }, element, evt);
        }

        private void OnFieldRowClick(ClickEvent evt)
        {
            if (suppressClick)
            {
                suppressClick = false;
                return;
            }

            if (evt.target is Button)
            {
                return;
            }

            var element = (VisualElement)evt.currentTarget;
            var row = (FieldRow)element.userData;
            if (row.Index < fields.Count)
            {
                SelectEntryByPath(fields[row.Index].Path);
            }
        }

        // ------------------------------------------------------------------ system items (custom items of the type)

        private ItemRow CreateItemRow()
        {
            var row = new ItemRow();
            var element = new VisualElement();
            element.AddToClassList("vnd-row");
            element.userData = row;
            row.Element = element;

            row.Icon = VeneerStyle.IconElement("dot", null, Brand);
            element.Add(row.Icon);

            row.Name = new Label();
            row.Name.AddToClassList("vnd-row__label");
            row.Name.pickingMode = PickingMode.Ignore;
            element.Add(row.Name);

            row.Mark = new Label();
            row.Mark.AddToClassList("vnd-row__mark");
            row.Mark.pickingMode = PickingMode.Ignore;
            element.Add(row.Mark);

            // Header and footer items cannot be dragged into a card: they have this switch instead.
            row.Header = new Toggle { text = "Show in header", tooltip = "Draws this item at the right of the header." };
            row.Header.AddToClassList("vnd-row__toggle");
            row.Header.RegisterValueChangedCallback(evt => OnHeaderItemToggled(row.Index, evt.newValue));
            element.Add(row.Header);

            element.RegisterCallback<PointerDownEvent>(OnItemRowDown);
            element.RegisterCallback<PointerMoveEvent>(OnRowPointerMove);
            element.RegisterCallback<PointerUpEvent>(OnRowPointerUp);
            element.RegisterCallback<PointerCaptureOutEvent>(OnRowCaptureOut);
            element.RegisterCallback<ClickEvent>(OnItemRowClick);
            return row;
        }

        private void RefreshItemRows()
        {
            if (itemsScroll == null)
            {
                return;
            }

            while (itemRows.Count < customItems.Count)
            {
                ItemRow created = CreateItemRow();
                itemsScroll.Add(created.Element);
                itemRows.Add(created);
            }

            for (int i = 0; i < itemRows.Count; i++)
            {
                ItemRow row = itemRows[i];
                row.Index = i;
                if (i >= customItems.Count)
                {
                    VeneerStyle.SetDisplay(row.Element, false);
                    continue;
                }

                VeneerCustomItem item = customItems[i];
                VeneerStyle.SetDisplay(row.Element, true);
                row.Name.text = item.Label;
                row.Element.tooltip = item.Description;
                string iconName = string.IsNullOrEmpty(item.Icon) ? "cube" : item.Icon;
                if (!string.Equals(row.IconName, iconName, StringComparison.Ordinal))
                {
                    row.IconName = iconName;
                    VeneerStyle.SetIcon(row.Icon, iconName, Brand);
                }

                // Header badges and footer items are not dragged into cards: they have a switch instead.
                bool header = item.Slot != VeneerCustomItemSlot.Item;
                if (header)
                {
                    bool footer = IsFooterItem(item);
                    bool under = IsAfterHeaderItem(item);
                    row.Header.text = footer ? "Show in footer" : (under ? "Show under the header" : "Show in header");
                    row.Header.tooltip = footer
                        ? "Draws this item at the very end of the Inspector, outside the tabs."
                        : (under ? "Draws this item right under the header, above the tabs." : "Draws this item at the right of the header.");
                }

                VeneerStyle.SetDisplay(row.Header, header);
                VeneerStyle.SetDisplay(row.Mark, !header);
            }

            VeneerStyle.SetDisplay(itemsBox, customItems.Count > 0);
        }

        private void UpdateItemMarks()
        {
            bool editable = Editable;
            for (int i = 0; i < customItems.Count && i < itemRows.Count; i++)
            {
                ItemRow row = itemRows[i];
                VeneerCustomItem item = customItems[i];
                bool placed = customCards.TryGetValue(item.Id, out string card);
                if (item.Slot != VeneerCustomItemSlot.Item)
                {
                    row.Header.SetValueWithoutNotify(placed);
                    row.Header.SetEnabled(editable);
                    continue;
                }

                row.Mark.text = placed ? card : NoteUnplaced;
                row.Mark.EnableInClassList(MarkPlaced, placed);
                row.Mark.EnableInClassList(MarkUnplaced, !placed);
            }
        }

        private void OnItemRowDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            var element = (VisualElement)evt.currentTarget;
            var row = (ItemRow)element.userData;
            if (row.Index >= customItems.Count || !Editable)
            {
                return;
            }

            VeneerCustomItem item = customItems[row.Index];
            if (item.Slot != VeneerCustomItemSlot.Item)
            {
                return;
            }

            BeginPending(new DragSource { Kind = DragKind.Item, IsCustom = true, CustomId = item.Id, Label = item.Label }, element, evt);
        }

        private void OnItemRowClick(ClickEvent evt)
        {
            if (suppressClick)
            {
                suppressClick = false;
                return;
            }

            var element = (VisualElement)evt.currentTarget;
            var row = (ItemRow)element.userData;
            if (row.Index < customItems.Count)
            {
                SelectCustomEntry(customItems[row.Index].Id);
            }
        }

        // The "Show in header" / "Show in footer" switch: adds or removes the item's entry (it lives in the layout's first card; the engine draws it in the header or at the end).
        private void OnHeaderItemToggled(int index, bool on)
        {
            if (!Editable || index < 0 || index >= customItems.Count)
            {
                return;
            }

            string id = customItems[index].Id;
            Undo.RecordObject(layout, "Show item in header");
            VeneerLayoutEntry existing = FindCustomEntry(id, out int page, out int block);
            if (on && existing == null)
            {
                EnsureFirstBlock().Entries.Add(new VeneerLayoutEntry { IsCustom = true, CustomId = id });
            }
            else if (!on && existing != null)
            {
                layout.Pages[page].Blocks[block].Entries.Remove(existing);
            }

            LayoutEdited();
        }

        // ------------------------------------------------------------------ commands

        private void ToggleHidden(string path)
        {
            if (!Editable)
            {
                return;
            }

            Undo.RecordObject(layout, "Hide field");
            if (layout.IsHidden(path))
            {
                layout.HiddenPaths.Remove(path);
            }
            else
            {
                RemoveEntryByPath(path);
                layout.HiddenPaths.Add(path);
            }

            LayoutEdited();
        }

        private void RemoveEntryByPath(string path)
        {
            VeneerLayoutEntry entry = layout.FindEntry(path, out int page, out int block);
            if (entry != null)
            {
                layout.Pages[page].Blocks[block].Entries.Remove(entry);
            }
        }

        // The entry of a custom item, wherever it sits; null when the item is not placed.
        private VeneerLayoutEntry FindCustomEntry(string id, out int page, out int block)
        {
            page = -1;
            block = -1;
            if (layout == null || string.IsNullOrEmpty(id))
            {
                return null;
            }

            List<VeneerLayoutPage> pages = layout.Pages;
            for (int p = 0; p < pages.Count; p++)
            {
                List<VeneerLayoutBlock> blocks = pages[p].Blocks;
                for (int b = 0; b < blocks.Count; b++)
                {
                    List<VeneerLayoutEntry> entries = blocks[b].Entries;
                    for (int e = 0; e < entries.Count; e++)
                    {
                        if (entries[e].IsCustom && string.Equals(entries[e].CustomId, id, StringComparison.Ordinal))
                        {
                            page = p;
                            block = b;
                            return entries[e];
                        }
                    }
                }
            }

            return null;
        }

        // The layout's first card (the tab and the card are made when missing); the caller has recorded Undo.
        private VeneerLayoutBlock EnsureFirstBlock()
        {
            if (layout.Pages.Count == 0)
            {
                layout.Pages.Add(new VeneerLayoutPage { Title = "General" });
            }

            List<VeneerLayoutBlock> blocks = layout.Pages[0].Blocks;
            if (blocks.Count == 0)
            {
                blocks.Add(new VeneerLayoutBlock { Title = "Fields" });
            }

            return blocks[0];
        }

        // Removes the entries (and hidden marks) of fields inside the struct that was just placed whole, so nothing is drawn twice.
        private void RemoveDescendantRefs(string parentPath, VeneerLayoutEntry keep)
        {
            string prefix = parentPath + ".";
            List<VeneerLayoutPage> pages = layout.Pages;
            for (int p = 0; p < pages.Count; p++)
            {
                List<VeneerLayoutBlock> blocks = pages[p].Blocks;
                for (int b = 0; b < blocks.Count; b++)
                {
                    List<VeneerLayoutEntry> entries = blocks[b].Entries;
                    for (int e = entries.Count - 1; e >= 0; e--)
                    {
                        string path = entries[e].Path;
                        if (!ReferenceEquals(entries[e], keep) && !entries[e].IsNote && !entries[e].IsCustom
                            && !string.IsNullOrEmpty(path) && path.StartsWith(prefix, StringComparison.Ordinal))
                        {
                            entries.RemoveAt(e);
                        }
                    }
                }
            }

            for (int h = layout.HiddenPaths.Count - 1; h >= 0; h--)
            {
                string hidden = layout.HiddenPaths[h];
                if (!string.IsNullOrEmpty(hidden) && hidden.StartsWith(prefix, StringComparison.Ordinal))
                {
                    layout.HiddenPaths.RemoveAt(h);
                }
            }
        }

        private void PlaceAllUnplaced()
        {
            if (!Editable || fields.Count == 0)
            {
                return;
            }

            Undo.RecordObject(layout, "Place all fields");
            VeneerLayoutBlock block = EnsureSelectedBlock(out _, out _);
            for (int i = 0; i < fields.Count; i++)
            {
                if (fields[i].Depth == 0)
                {
                    PlaceUnplacedItem(i, block);
                }
            }

            LayoutEdited();
        }

        // A leaf or a fully unplaced struct is placed whole; a partly placed struct gets its unplaced children, one by one (recursively).
        private void PlaceUnplacedItem(int index, VeneerLayoutBlock block)
        {
            FieldItem item = fields[index];
            if (layout.IsPlaced(item.Path) || layout.IsHidden(item.Path))
            {
                return;
            }

            if (!item.IsStruct || item.LeafResolved == 0)
            {
                block.Entries.Add(new VeneerLayoutEntry { Path = item.Path });
                return;
            }

            if (item.LeafResolved >= item.LeafTotal)
            {
                return;
            }

            for (int c = index + 1; c < fields.Count && fields[c].Depth > item.Depth; c++)
            {
                if (fields[c].Parent == index)
                {
                    PlaceUnplacedItem(c, block);
                }
            }
        }

        private string FieldDisplayName(string path)
        {
            return fieldNames.TryGetValue(path, out string display) ? display : path;
        }

        // ------------------------------------------------------------------ warnings (hints, never dialogs)

        private void BuildWarnings(VisualElement content)
        {
            problemHint = VeneerStyle.Hint(string.Empty, "error", out problemLabel, Brand);
            ownEditorHint = VeneerStyle.Hint(string.Empty, "warn", out ownEditorLabel, Brand);
            missingScriptHint = VeneerStyle.Hint("The script of this layout is missing: the layout can be edited, but its fields cannot be listed or previewed.", "warn", Brand);
            baseLayoutHint = VeneerStyle.Hint(string.Empty, "info", out baseLayoutLabel, Brand);
            staleHint = VeneerStyle.Hint(string.Empty, "warn", out staleLabel, Brand);
            staleHint.Add(VeneerStyle.Button("Remove", null, "Removes those entries from the layout.", RemoveStale, false, Brand));
            // The hints sit above the panes in the content column: they keep their full height, the panes give way (and scroll).
            content.Add(problemHint);
            content.Add(ownEditorHint);
            content.Add(missingScriptHint);
            content.Add(baseLayoutHint);
            content.Add(staleHint);
            problemHint.style.flexShrink = 0f;
            ownEditorHint.style.flexShrink = 0f;
            missingScriptHint.style.flexShrink = 0f;
            baseLayoutHint.style.flexShrink = 0f;
            staleHint.style.flexShrink = 0f;
        }

        private void RefreshWarnings()
        {
            if (problemHint == null)
            {
                return;
            }

            bool hasProblem = tempProblem != null;
            VeneerStyle.SetDisplay(problemHint, hasProblem);
            if (hasProblem)
            {
                problemLabel.text = tempProblem;
            }

            // Never for the system's own (read-only) design, and never for a layout-driven editor (FindOwnEditorName leaves those out).
            bool own = layout != null && !readOnlyDefault && ownEditorName != null;
            VeneerStyle.SetDisplay(ownEditorHint, own);
            if (own)
            {
                ownEditorLabel.text = "This type has its own editor (" + ownEditorName + "): the layout is not used there.";
            }

            VeneerStyle.SetDisplay(missingScriptHint, layout != null && targetType == null);

            bool hasBase = layout == null && baseLayoutName != null;
            VeneerStyle.SetDisplay(baseLayoutHint, hasBase);
            if (hasBase)
            {
                baseLayoutLabel.text = "The layout \"" + baseLayoutName + "\" of a base class already applies to this type. Create a layout for this type to override it.";
            }

            int stale = layout != null && !readOnlyDefault && tempSo != null ? CountStale() : 0;
            VeneerStyle.SetDisplay(staleHint, stale > 0);
            if (stale > 0)
            {
                warnBuilder.Clear();
                warnBuilder.Append(stale).Append(stale == 1 ? " entry points" : " entries point").Append(" to a field that no longer exists in the script: ");
                AppendStaleNames(warnBuilder);
                staleLabel.text = warnBuilder.ToString();
            }
        }

        // An entry is stale when it names a field the script no longer has. Notes and custom items have no path and are never stale.
        private bool IsStaleEntry(VeneerLayoutEntry entry)
        {
            return !entry.IsNote && !entry.IsCustom && !PathExists(entry.Path);
        }

        private int CountStale()
        {
            int count = 0;
            List<VeneerLayoutPage> pages = layout.Pages;
            for (int p = 0; p < pages.Count; p++)
            {
                List<VeneerLayoutBlock> blocks = pages[p].Blocks;
                for (int b = 0; b < blocks.Count; b++)
                {
                    List<VeneerLayoutEntry> entries = blocks[b].Entries;
                    for (int e = 0; e < entries.Count; e++)
                    {
                        if (IsStaleEntry(entries[e]))
                        {
                            count++;
                        }
                    }
                }
            }

            return count;
        }

        private void AppendStaleNames(StringBuilder builder)
        {
            int added = 0;
            List<VeneerLayoutPage> pages = layout.Pages;
            for (int p = 0; p < pages.Count && added < 4; p++)
            {
                List<VeneerLayoutBlock> blocks = pages[p].Blocks;
                for (int b = 0; b < blocks.Count && added < 4; b++)
                {
                    List<VeneerLayoutEntry> entries = blocks[b].Entries;
                    for (int e = 0; e < entries.Count && added < 4; e++)
                    {
                        if (IsStaleEntry(entries[e]))
                        {
                            builder.Append(added > 0 ? ", " : string.Empty).Append(entries[e].Path ?? string.Empty);
                            added++;
                        }
                    }
                }
            }
        }

        private void RemoveStale()
        {
            if (!Editable || tempSo == null)
            {
                return;
            }

            Undo.RecordObject(layout, "Remove missing fields");
            List<VeneerLayoutPage> pages = layout.Pages;
            for (int p = 0; p < pages.Count; p++)
            {
                List<VeneerLayoutBlock> blocks = pages[p].Blocks;
                for (int b = 0; b < blocks.Count; b++)
                {
                    List<VeneerLayoutEntry> entries = blocks[b].Entries;
                    for (int e = entries.Count - 1; e >= 0; e--)
                    {
                        if (IsStaleEntry(entries[e]))
                        {
                            entries.RemoveAt(e);
                        }
                    }
                }
            }

            LayoutEdited();
        }
    }
}
