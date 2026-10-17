using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    // Centre pane: the flat tree (Header, Pages > Cards > Entries, then the Hidden bucket) and its commands.
    // The tree is one pooled list of fixed-height rows bound to a list of Node values, so a refresh only changes texts and classes.
    public sealed partial class VeneerDesignerWindow
    {
        private sealed class TreeRow
        {
            public VisualElement Element;
            public VisualElement Icon;
            public Label Name;
            public Label Hint;
            public Button Unhide;
            public TextField Rename;
            public string IconName;
            public int Depth = -1;
            public int Index;
            public bool Renaming;
        }

        private const float IndentPixels = 14f;
        private const float BaseIndent = 6f;

        private static readonly string[] SmallInts = BuildSmallInts(100);

        private readonly List<Node> nodes = new List<Node>(64);
        private readonly List<TreeRow> treeRows = new List<TreeRow>(64);

        private ScrollView treeScroll;
        private VisualElement treeHost;
        private VisualElement dropLine;
        private Label treeEmpty;
        private TreeRow renaming;
        private Button noteButton;
        private Button deleteButton;
        private Button pageButton;
        private Button blockButton;
        private VisualElement readOnlyBar;
        private Button upButton;
        private Button downButton;

        private static string[] BuildSmallInts(int count)
        {
            var texts = new string[count];
            for (int i = 0; i < count; i++)
            {
                texts[i] = i.ToString();
            }

            return texts;
        }

        private const string NoteHint = "note";
        private const string SameRowSuffix = " · same row";

        private static string NoteIconName(VeneerNoteKind kind)
        {
            switch (kind)
            {
                case VeneerNoteKind.Warning:
                    return "warning";
                case VeneerNoteKind.Error:
                    return "error";
                case VeneerNoteKind.Text:
                    return "text";
                case VeneerNoteKind.Heading:
                    return "hash";
                default:
                    return "info";
            }
        }

        // The first line of a note's text for its tree row (the row cuts it with an ellipsis); no allocation for one-line notes.
        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "(empty note)";
            }

            int end = text.IndexOf('\n');
            if (end < 0)
            {
                return text;
            }

            return end == 0 ? "(empty first line)" : text.Substring(0, end).TrimEnd('\r');
        }

        private static string CountText(int value)
        {
            return value >= 0 && value < SmallInts.Length ? SmallInts[value] : value.ToString();
        }

        // ------------------------------------------------------------------ the pane

        private VisualElement BuildLayoutPane()
        {
            var pane = new VisualElement();
            pane.AddToClassList("vnd-pane");
            pane.Add(VeneerStyle.Section("Layout"));

            // Shown while the system's own design is open: it cannot be edited, the button makes an editable copy.
            readOnlyBar = VeneerStyle.Hint("This is the system's own design (read-only). Press Customize to edit your own copy.", "info", Brand);
            readOnlyBar.Add(VeneerStyle.Button("Customize", "edit", "Makes your own editable copy of this design in Assets/Veneer Layouts.", CustomizeDefault, true, Brand));
            readOnlyBar.style.marginBottom = 6f;
            VeneerStyle.SetDisplay(readOnlyBar, false);
            pane.Add(readOnlyBar);

            var bar = new VisualElement();
            bar.AddToClassList("vnd-pane__bar");
            pageButton = VeneerStyle.IconButton("plus-circle", "Add a tab (page)", AddPage, Brand);
            bar.Add(pageButton);
            blockButton = VeneerStyle.IconButton("plus", "Add a card to the selected tab", AddBlock, Brand);
            bar.Add(blockButton);
            noteButton = VeneerStyle.IconButton("info", "Add a note (a message box) to the selected card, or right after the selected field or note", AddNote, Brand);
            bar.Add(noteButton);
            deleteButton = VeneerStyle.IconButton("trash", "Delete the selection (Delete key)", DeleteSelection, Brand);
            bar.Add(deleteButton);
            upButton = VeneerStyle.IconButton("arrow-up", "Move up (Alt+Up)", () => MoveSelection(-1), Brand);
            bar.Add(upButton);
            downButton = VeneerStyle.IconButton("arrow-down", "Move down (Alt+Down)", () => MoveSelection(1), Brand);
            bar.Add(downButton);
            bar.Add(VeneerStyle.Spacer());
            bool previewOn = SessionState.GetBool(PreviewKey, true);
            bar.Add(VeneerStyle.Chip("Preview", "eye", previewOn, SetPreviewOn, Brand));
            pane.Add(bar);

            // The tree and the preview share one column: the tree takes the room the preview does not (both have a minimum height).
            splitArea = new VisualElement();
            splitArea.AddToClassList("vnd-split");

            treeHost = new VisualElement();
            treeHost.AddToClassList("vnd-tree-host");
            treeHost.focusable = true;
            treeHost.RegisterCallback<KeyDownEvent>(OnTreeKeyDown);

            treeScroll = new ScrollView(ScrollViewMode.Vertical);
            treeScroll.AddToClassList("vnd-scroll");
            treeHost.Add(treeScroll);

            treeEmpty = VeneerStyle.MutedLabel("Add a tab, then drag fields from the left into a card.");
            treeEmpty.AddToClassList("vnd-tree-empty");
            treeEmpty.pickingMode = PickingMode.Ignore;
            treeHost.Add(treeEmpty);

            dropLine = new VisualElement();
            dropLine.AddToClassList("vnd-drop-line");
            dropLine.pickingMode = PickingMode.Ignore;
            treeHost.Add(dropLine);
            splitArea.Add(treeHost);

            BuildPreviewPane(splitArea, previewOn);
            pane.Add(splitArea);
            return pane;
        }

        private TreeRow CreateTreeRow()
        {
            var row = new TreeRow();
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

            row.Rename = new TextField();
            row.Rename.AddToClassList("vnd-rename");
            VeneerStyle.SetDisplay(row.Rename, false);
            row.Rename.RegisterCallback<KeyDownEvent>(evt => OnRenameKey(row, evt), TrickleDown.TrickleDown);
            row.Rename.RegisterCallback<FocusOutEvent>(evt => CommitRename(row));
            element.Add(row.Rename);

            row.Hint = new Label();
            row.Hint.AddToClassList("vnd-row__hint");
            row.Hint.pickingMode = PickingMode.Ignore;
            element.Add(row.Hint);

            row.Unhide = VeneerStyle.IconButton("eye", "Show this field again", () => UnhideAt(row.Index), Brand);
            VeneerStyle.SetDisplay(row.Unhide, false);
            element.Add(row.Unhide);

            element.RegisterCallback<PointerDownEvent>(OnTreeRowDown);
            element.RegisterCallback<PointerMoveEvent>(OnRowPointerMove);
            element.RegisterCallback<PointerUpEvent>(OnRowPointerUp);
            element.RegisterCallback<PointerCaptureOutEvent>(OnRowCaptureOut);
            element.RegisterCallback<ClickEvent>(OnTreeRowClick);
            return row;
        }

        // ------------------------------------------------------------------ refresh

        private void BuildNodes()
        {
            nodes.Clear();
            nodes.Add(new Node(NodeKind.Header, 0, 0, 0));
            List<VeneerLayoutPage> pages = layout.Pages;
            for (int p = 0; p < pages.Count; p++)
            {
                nodes.Add(new Node(NodeKind.Page, p, 0, 0));
                List<VeneerLayoutBlock> blocks = pages[p].Blocks;
                for (int b = 0; b < blocks.Count; b++)
                {
                    nodes.Add(new Node(NodeKind.Block, p, b, 0));
                    int entryCount = blocks[b].Entries.Count;
                    for (int e = 0; e < entryCount; e++)
                    {
                        nodes.Add(new Node(NodeKind.Entry, p, b, e));
                    }
                }
            }

            nodes.Add(new Node(NodeKind.HiddenHeader, 0, 0, 0));
            for (int h = 0; h < layout.HiddenPaths.Count; h++)
            {
                nodes.Add(new Node(NodeKind.HiddenItem, 0, 0, h));
            }
        }

        private void RefreshTree()
        {
            if (treeScroll == null || layout == null)
            {
                return;
            }

            BuildNodes();
            while (treeRows.Count < nodes.Count)
            {
                TreeRow created = CreateTreeRow();
                treeScroll.Add(created.Element);
                treeRows.Add(created);
            }

            for (int i = 0; i < treeRows.Count; i++)
            {
                TreeRow row = treeRows[i];
                if (i < nodes.Count)
                {
                    row.Index = i;
                    BindTreeRow(row, nodes[i]);
                    VeneerStyle.SetDisplay(row.Element, true);
                }
                else
                {
                    VeneerStyle.SetDisplay(row.Element, false);
                }
            }

            VeneerStyle.SetDisplay(treeEmpty, layout.Pages.Count == 0);
            UpdateCommandButtons();
        }

        private void BindTreeRow(TreeRow row, Node node)
        {
            string name;
            string hint = string.Empty;
            string icon;
            int depth;
            bool missing = false;
            bool showUnhide = false;
            switch (node.Kind)
            {
                case NodeKind.Header:
                    name = "Header";
                    icon = "text";
                    depth = 0;
                    hint = !layout.ShowHeader
                        ? "off"
                        : (layout.Banner != null && layout.Banner.Enabled ? "banner" : (string.IsNullOrEmpty(layout.Title) ? string.Empty : layout.Title));
                    break;
                case NodeKind.Page:
                {
                    VeneerLayoutPage page = layout.Pages[node.Page];
                    name = string.IsNullOrEmpty(page.Title) ? "(tab)" : page.Title;
                    icon = string.IsNullOrEmpty(page.Icon) ? "layers" : page.Icon;
                    depth = 0;
                    hint = string.IsNullOrEmpty(page.ShowIfPath) ? CountText(page.Blocks.Count) : CountText(page.Blocks.Count) + " \u00b7 if " + page.ShowIfPath;
                    break;
                }

                case NodeKind.Block:
                {
                    VeneerLayoutBlock block = layout.Pages[node.Page].Blocks[node.Block];
                    name = string.IsNullOrEmpty(block.Title) ? "(card)" : block.Title;
                    icon = string.IsNullOrEmpty(block.Icon) ? "grid" : block.Icon;
                    depth = 1;
                    hint = string.IsNullOrEmpty(block.ShowIfPath) ? CountText(block.Entries.Count) : CountText(block.Entries.Count) + " \u00b7 if " + block.ShowIfPath;
                    break;
                }

                case NodeKind.Entry:
                {
                    VeneerLayoutEntry entry = layout.Pages[node.Page].Blocks[node.Block].Entries[node.Entry];
                    depth = 2;
                    if (entry.IsNote)
                    {
                        name = FirstLine(entry.NoteText);
                        icon = NoteIconName(entry.NoteKind);
                        hint = NoteHint;
                        break;
                    }

                    if (entry.IsCustom)
                    {
                        // A system item: its own icon and title, and the word of its slot (item, header, footer) at the right.
                        VeneerCustomItem item = FindItem(entry.CustomId);
                        name = !string.IsNullOrEmpty(entry.Label) ? entry.Label : (item != null ? item.Label : (entry.CustomId ?? "(item)"));
                        icon = item != null && !string.IsNullOrEmpty(item.Icon) ? item.Icon : "cube";
                        hint = entry.SameRow ? SlotWord(item) + SameRowSuffix : SlotWord(item);
                        missing = item == null;
                        break;
                    }

                    string path = entry.Path ?? string.Empty;
                    name = string.IsNullOrEmpty(entry.Label) ? FieldDisplayName(path) : entry.Label;
                    icon = "dot";
                    hint = entry.SameRow ? path + SameRowSuffix : path;
                    missing = tempSo != null && !PathExists(path);
                    break;
                }

                case NodeKind.HiddenHeader:
                    name = "Hidden";
                    icon = "eye-off";
                    depth = 0;
                    hint = CountText(layout.HiddenPaths.Count);
                    break;
                default:
                {
                    string path = layout.HiddenPaths[node.Entry] ?? string.Empty;
                    name = FieldDisplayName(path);
                    icon = "eye-off";
                    depth = 1;
                    hint = path;
                    showUnhide = true;
                    missing = tempSo != null && !PathExists(path);
                    break;
                }
            }

            row.Name.text = name;
            row.Hint.text = hint;
            if (!string.Equals(row.IconName, icon, StringComparison.Ordinal))
            {
                row.IconName = icon;
                VeneerStyle.SetIcon(row.Icon, icon, Brand);
            }

            if (row.Depth != depth)
            {
                row.Depth = depth;
                row.Element.style.paddingLeft = BaseIndent + depth * IndentPixels;
            }

            row.Element.EnableInClassList("vnd-row--selected", IsSelected(node));
            row.Element.EnableInClassList("vnd-row--missing", missing);
            VeneerStyle.SetDisplay(row.Unhide, showUnhide);
            row.Unhide.SetEnabled(Editable);
        }

        private void UpdateSelectionVisuals()
        {
            for (int i = 0; i < nodes.Count && i < treeRows.Count; i++)
            {
                treeRows[i].Element.EnableInClassList("vnd-row--selected", IsSelected(nodes[i]));
            }

            UpdateCommandButtons();
        }

        private void UpdateCommandButtons()
        {
            if (deleteButton == null)
            {
                return;
            }

            bool editable = Editable;
            bool deletable = editable && (selKind == NodeKind.Page || selKind == NodeKind.Block || selKind == NodeKind.Entry || selKind == NodeKind.HiddenItem);
            bool movable = editable && (selKind == NodeKind.Page || selKind == NodeKind.Block || selKind == NodeKind.Entry);
            pageButton.SetEnabled(editable);
            blockButton.SetEnabled(editable);
            noteButton.SetEnabled(editable && (selKind == NodeKind.Block || selKind == NodeKind.Entry));
            deleteButton.SetEnabled(deletable);
            upButton.SetEnabled(movable);
            downButton.SetEnabled(movable);
            VeneerStyle.SetDisplay(readOnlyBar, readOnlyDefault);
        }

        // ------------------------------------------------------------------ selection by pointer and keyboard

        private void OnTreeRowDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || evt.target is Button || evt.target is TextElement)
            {
                return;
            }

            var element = (VisualElement)evt.currentTarget;
            var row = (TreeRow)element.userData;
            if (row.Renaming || row.Index >= nodes.Count)
            {
                return;
            }

            Node node = nodes[row.Index];
            Select(node);
            treeHost.Focus();
            if (!Editable)
            {
                return;
            }

            switch (node.Kind)
            {
                case NodeKind.Page:
                    BeginPending(new DragSource { Kind = DragKind.Page, Node = node, Label = row.Name.text }, element, evt);
                    break;
                case NodeKind.Block:
                    BeginPending(new DragSource { Kind = DragKind.Block, Node = node, Label = row.Name.text }, element, evt);
                    break;
                case NodeKind.Entry:
                {
                    VeneerLayoutEntry dragged = layout.Pages[node.Page].Blocks[node.Block].Entries[node.Entry];
                    BeginPending(new DragSource { Kind = DragKind.Entry, Node = node, Path = dragged.Path, IsNote = dragged.IsNote, IsCustom = dragged.IsCustom, Label = row.Name.text }, element, evt);
                    break;
                }

                case NodeKind.HiddenItem:
                    BeginPending(new DragSource { Kind = DragKind.Field, Node = node, Path = layout.HiddenPaths[node.Entry], Label = row.Name.text }, element, evt);
                    break;
            }
        }

        private void OnTreeRowClick(ClickEvent evt)
        {
            if (suppressClick)
            {
                suppressClick = false;
                return;
            }

            if (evt.clickCount == 2)
            {
                var element = (VisualElement)evt.currentTarget;
                BeginRename((TreeRow)element.userData);
            }
        }

        private void OnTreeKeyDown(KeyDownEvent evt)
        {
            if (layout == null || renaming != null || dragActive)
            {
                return;
            }

            switch (evt.keyCode)
            {
                case KeyCode.UpArrow:
                    if (evt.altKey)
                    {
                        MoveSelection(-1);
                    }
                    else
                    {
                        StepSelection(-1);
                    }

                    evt.StopPropagation();
                    break;
                case KeyCode.DownArrow:
                    if (evt.altKey)
                    {
                        MoveSelection(1);
                    }
                    else
                    {
                        StepSelection(1);
                    }

                    evt.StopPropagation();
                    break;
                case KeyCode.Delete:
                    DeleteSelection();
                    evt.StopPropagation();
                    break;
                case KeyCode.F2:
                    for (int i = 0; i < nodes.Count && i < treeRows.Count; i++)
                    {
                        if (IsSelected(nodes[i]))
                        {
                            BeginRename(treeRows[i]);
                            break;
                        }
                    }

                    evt.StopPropagation();
                    break;
            }
        }

        private void StepSelection(int direction)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (IsSelected(nodes[i]))
                {
                    int next = Mathf.Clamp(i + direction, 0, nodes.Count - 1);
                    Select(nodes[next]);
                    treeScroll.ScrollTo(treeRows[next].Element);
                    return;
                }
            }
        }

        // ------------------------------------------------------------------ rename in place

        private void BeginRename(TreeRow row)
        {
            if (!Editable || row.Index >= nodes.Count)
            {
                return;
            }

            Node node = nodes[row.Index];
            string current;
            switch (node.Kind)
            {
                case NodeKind.Page:
                    current = layout.Pages[node.Page].Title;
                    break;
                case NodeKind.Block:
                    current = layout.Pages[node.Page].Blocks[node.Block].Title;
                    break;
                case NodeKind.Entry:
                    // A note's text is edited in the Properties pane, not renamed in the row.
                    if (layout.Pages[node.Page].Blocks[node.Block].Entries[node.Entry].IsNote)
                    {
                        return;
                    }

                    current = row.Name.text;
                    break;
                default:
                    return;
            }

            if (renaming != null && renaming != row)
            {
                CommitRename(renaming);
            }

            renaming = row;
            row.Renaming = true;
            row.Rename.SetValueWithoutNotify(current ?? string.Empty);
            VeneerStyle.SetDisplay(row.Name, false);
            VeneerStyle.SetDisplay(row.Rename, true);
            row.Rename.schedule.Execute(() =>
            {
                row.Rename.Focus();
                row.Rename.SelectAll();
            });
        }

        private void OnRenameKey(TreeRow row, KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                CommitRename(row);
                treeHost.Focus();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                EndRename(row);
                treeHost.Focus();
                evt.StopPropagation();
            }
        }

        private void EndRename(TreeRow row)
        {
            row.Renaming = false;
            if (renaming == row)
            {
                renaming = null;
            }

            VeneerStyle.SetDisplay(row.Rename, false);
            VeneerStyle.SetDisplay(row.Name, true);
        }

        private void CommitRename(TreeRow row)
        {
            if (!row.Renaming)
            {
                return;
            }

            string text = row.Rename.value;
            EndRename(row);
            if (!Editable || row.Index >= nodes.Count)
            {
                return;
            }

            Node node = nodes[row.Index];
            Undo.RecordObject(layout, "Rename");
            switch (node.Kind)
            {
                case NodeKind.Page:
                    layout.Pages[node.Page].Title = text;
                    break;
                case NodeKind.Block:
                    layout.Pages[node.Page].Blocks[node.Block].Title = text;
                    break;
                case NodeKind.Entry:
                    // Typing the field's own name back clears the override, so the default label keeps following the field.
                    VeneerLayoutEntry entry = layout.Pages[node.Page].Blocks[node.Block].Entries[node.Entry];
                    string defaultName = entry.IsCustom
                        ? (FindItem(entry.CustomId) != null ? FindItem(entry.CustomId).Label : string.Empty)
                        : FieldDisplayName(entry.Path ?? string.Empty);
                    entry.Label = string.Equals(text, defaultName, StringComparison.Ordinal) ? null : text;
                    break;
            }

            LayoutEdited();
        }

        // ------------------------------------------------------------------ commands

        private void AddPage()
        {
            if (!Editable)
            {
                return;
            }

            Undo.RecordObject(layout, "Add tab");
            int at = selKind == NodeKind.Page || selKind == NodeKind.Block || selKind == NodeKind.Entry ? selPage + 1 : layout.Pages.Count;
            layout.Pages.Insert(at, new VeneerLayoutPage { Title = "Tab " + CountText(layout.Pages.Count + 1) });
            SetSelection(NodeKind.Page, at, 0, 0);
            LayoutEdited();
        }

        private void AddBlock()
        {
            if (!Editable)
            {
                return;
            }

            Undo.RecordObject(layout, "Add card");
            if (layout.Pages.Count == 0)
            {
                layout.Pages.Add(new VeneerLayoutPage { Title = "General" });
            }

            bool onPage = selKind == NodeKind.Page || selKind == NodeKind.Block || selKind == NodeKind.Entry;
            int page = onPage ? selPage : layout.Pages.Count - 1;
            List<VeneerLayoutBlock> blocks = layout.Pages[page].Blocks;
            int at = selKind == NodeKind.Block || selKind == NodeKind.Entry ? selBlock + 1 : blocks.Count;
            blocks.Insert(at, new VeneerLayoutBlock { Title = "Card " + CountText(blocks.Count + 1) });
            SetSelection(NodeKind.Block, page, at, 0);
            LayoutEdited();
        }

        // Adds a note (info by default) at the end of the selected card, or right after the selected field or note.
        private void AddNote()
        {
            if (!Editable || (selKind != NodeKind.Block && selKind != NodeKind.Entry))
            {
                return;
            }

            Undo.RecordObject(layout, "Add note");
            List<VeneerLayoutEntry> list = layout.Pages[selPage].Blocks[selBlock].Entries;
            int at = selKind == NodeKind.Entry ? selEntry + 1 : list.Count;
            list.Insert(at, new VeneerLayoutEntry { IsNote = true, NoteText = "Note", NoteKind = VeneerNoteKind.Info });
            SetSelection(NodeKind.Entry, selPage, selBlock, at);
            LayoutEdited();
        }

        private void DeleteSelection()
        {
            if (!Editable)
            {
                return;
            }

            switch (selKind)
            {
                case NodeKind.Page:
                    Undo.RecordObject(layout, "Delete tab");
                    layout.Pages.RemoveAt(selPage);
                    SetSelection(NodeKind.Header, 0, 0, 0);
                    break;
                case NodeKind.Block:
                    Undo.RecordObject(layout, "Delete card");
                    layout.Pages[selPage].Blocks.RemoveAt(selBlock);
                    SetSelection(NodeKind.Page, selPage, 0, 0);
                    break;
                case NodeKind.Entry:
                    Undo.RecordObject(layout, "Delete field entry");
                    layout.Pages[selPage].Blocks[selBlock].Entries.RemoveAt(selEntry);
                    SetSelection(NodeKind.Block, selPage, selBlock, 0);
                    break;
                case NodeKind.HiddenItem:
                    Undo.RecordObject(layout, "Show field");
                    layout.HiddenPaths.RemoveAt(selEntry);
                    SetSelection(NodeKind.HiddenHeader, 0, 0, 0);
                    break;
                default:
                    return;
            }

            LayoutEdited();
        }

        private void UnhideAt(int rowIndex)
        {
            if (!Editable || rowIndex >= nodes.Count || nodes[rowIndex].Kind != NodeKind.HiddenItem)
            {
                return;
            }

            Undo.RecordObject(layout, "Show field");
            layout.HiddenPaths.RemoveAt(nodes[rowIndex].Entry);
            LayoutEdited();
        }

        // Moves the selected tab, card or field one place up (-1) or down (+1) inside its own list.
        private void MoveSelection(int direction)
        {
            if (!Editable)
            {
                return;
            }

            switch (selKind)
            {
                case NodeKind.Page:
                {
                    int to = selPage + direction;
                    if (to < 0 || to >= layout.Pages.Count)
                    {
                        return;
                    }

                    Undo.RecordObject(layout, "Move tab");
                    SwapAt(layout.Pages, selPage, to);
                    selPage = to;
                    break;
                }

                case NodeKind.Block:
                {
                    List<VeneerLayoutBlock> blocks = layout.Pages[selPage].Blocks;
                    int to = selBlock + direction;
                    if (to < 0 || to >= blocks.Count)
                    {
                        return;
                    }

                    Undo.RecordObject(layout, "Move card");
                    SwapAt(blocks, selBlock, to);
                    selBlock = to;
                    break;
                }

                case NodeKind.Entry:
                {
                    List<VeneerLayoutEntry> entries = layout.Pages[selPage].Blocks[selBlock].Entries;
                    int to = selEntry + direction;
                    if (to < 0 || to >= entries.Count)
                    {
                        return;
                    }

                    Undo.RecordObject(layout, "Move field entry");
                    SwapAt(entries, selEntry, to);
                    selEntry = to;
                    break;
                }

                default:
                    return;
            }

            LayoutEdited();
        }

        private static void SwapAt<T>(List<T> list, int from, int to)
        {
            T item = list[from];
            list[from] = list[to];
            list[to] = item;
        }

        private void SetSelection(NodeKind kind, int page, int block, int entry)
        {
            selKind = kind;
            selPage = page;
            selBlock = block;
            selEntry = entry;
        }

        // Selects the entry that holds a field path (used by clicking a placed field in the left pane).
        private void SelectEntryByPath(string path)
        {
            if (layout == null)
            {
                return;
            }

            VeneerLayoutEntry entry = layout.FindEntry(path, out int page, out int block);
            // A field inside a placed struct has no entry of its own: select the struct's entry.
            while (entry == null)
            {
                int dot = path.LastIndexOf('.');
                if (dot <= 0)
                {
                    return;
                }

                path = path.Substring(0, dot);
                entry = layout.FindEntry(path, out page, out block);
            }

            Select(new Node(NodeKind.Entry, page, block, layout.Pages[page].Blocks[block].Entries.IndexOf(entry)));
        }

        // Selects the entry of a custom item (used by clicking a placed item in the left pane).
        private void SelectCustomEntry(string id)
        {
            VeneerLayoutEntry entry = FindCustomEntry(id, out int page, out int block);
            if (entry != null)
            {
                Select(new Node(NodeKind.Entry, page, block, layout.Pages[page].Blocks[block].Entries.IndexOf(entry)));
            }
        }

        // The card "Place all" and similar commands put fields into: the selected card, the last card of the selected tab, or the first card.
        // Missing tabs and cards are created (the caller has recorded Undo).
        private VeneerLayoutBlock EnsureSelectedBlock(out int pageIndex, out int blockIndex)
        {
            if (layout.Pages.Count == 0)
            {
                layout.Pages.Add(new VeneerLayoutPage { Title = "General" });
            }

            switch (selKind)
            {
                case NodeKind.Block:
                case NodeKind.Entry:
                    pageIndex = selPage;
                    blockIndex = selBlock;
                    break;
                case NodeKind.Page:
                    pageIndex = selPage;
                    blockIndex = Mathf.Max(0, layout.Pages[pageIndex].Blocks.Count - 1);
                    break;
                default:
                    pageIndex = 0;
                    blockIndex = 0;
                    break;
            }

            List<VeneerLayoutBlock> blocks = layout.Pages[pageIndex].Blocks;
            if (blocks.Count == 0)
            {
                blocks.Add(new VeneerLayoutBlock { Title = "Fields" });
                blockIndex = 0;
            }

            return blocks[blockIndex];
        }
    }
}
