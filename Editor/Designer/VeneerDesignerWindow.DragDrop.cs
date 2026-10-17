using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    // Mouse dragging done with pointer events and a small ghost element (UI Toolkit's own drag and drop is for objects from outside the
    // window). A row captures the pointer on press; after a few pixels of movement the drag starts, the row under the pointer decides the
    // drop (a line before/after a row, or a highlight for "into"), and releasing performs it as ONE Undo step.
    public sealed partial class VeneerDesignerWindow
    {
        private enum DragKind
        {
            Field,
            Page,
            Block,
            Entry,

            // A system (custom) item dragged from the left pane.
            Item
        }

        private enum DropMode
        {
            None,
            Before,
            After,
            Into,
            Hide
        }

        private struct DragSource
        {
            public DragKind Kind;
            public Node Node;
            public string Path;
            public string Label;

            // Notes can be moved between cards but never hidden.
            public bool IsNote;

            // Custom items (placed entries or the list's rows) can be moved but never hidden either; CustomId names the item.
            public bool IsCustom;
            public string CustomId;
        }

        private struct DropTarget
        {
            public DropMode Mode;
            public Node Node;
            public int HighlightRow;
            public int LineRow;
            public bool LineAfter;
            public bool IntoEmptyLayout;
        }

        private const float DragThresholdSquared = 36f;
        private const float AutoScrollEdge = 24f;
        private const float AutoScrollStep = 14f;

        private Label ghost;
        private bool dragPending;
        private bool dragActive;
        private bool suppressClick;
        private DragSource dragSource;
        private Vector2 dragOrigin;
        private VisualElement dragCapturer;
        private int dragPointer;
        private int highlightRow = -1;

        // ------------------------------------------------------------------ pointer handlers shared by the field rows and the tree rows

        private void BeginPending(DragSource source, VisualElement capturer, PointerDownEvent evt)
        {
            dragSource = source;
            dragPending = true;
            dragActive = false;
            suppressClick = false;
            dragOrigin = evt.position;
            dragCapturer = capturer;
            dragPointer = evt.pointerId;
            capturer.CapturePointer(evt.pointerId);
        }

        private void OnRowPointerMove(PointerMoveEvent evt)
        {
            if (!dragPending && !dragActive)
            {
                return;
            }

            Vector2 world = evt.position;
            if ((evt.pressedButtons & 1) == 0)
            {
                // The release was missed (for example the pointer left the window): drop nothing.
                EndDrag();
                return;
            }

            if (dragPending)
            {
                if ((world - dragOrigin).sqrMagnitude < DragThresholdSquared)
                {
                    return;
                }

                StartDrag();
            }

            UpdateDrag(world);
        }

        private void OnRowPointerUp(PointerUpEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            if (dragActive)
            {
                DragSource source = dragSource;
                DropTarget target = ResolveTarget(evt.position, source);
                suppressClick = true;
                EndDrag();
                PerformDrop(source, target);
                return;
            }

            EndDrag();
        }

        private void OnRowCaptureOut(PointerCaptureOutEvent evt)
        {
            if (dragActive || dragPending)
            {
                EndDrag();
            }
        }

        private void OnRootKeyDown(KeyDownEvent evt)
        {
            if (dragActive && evt.keyCode == KeyCode.Escape)
            {
                EndDrag();
                evt.StopPropagation();
            }
        }

        private void StartDrag()
        {
            dragPending = false;
            dragActive = true;
            ghost.text = dragSource.Label;
            VeneerStyle.SetDisplay(ghost, true);
            ghost.BringToFront();
            treeHost.Focus();
        }

        private void EndDrag()
        {
            dragPending = false;
            dragActive = false;
            if (ghost != null)
            {
                VeneerStyle.SetDisplay(ghost, false);
            }

            if (dropLine != null)
            {
                VeneerStyle.SetDisplay(dropLine, false);
            }

            SetHighlight(-1);
            VisualElement capturer = dragCapturer;
            dragCapturer = null;
            if (capturer != null && capturer.HasPointerCapture(dragPointer))
            {
                capturer.ReleasePointer(dragPointer);
            }
        }

        // ------------------------------------------------------------------ while dragging

        private void UpdateDrag(Vector2 world)
        {
            Vector2 local = RootElement.WorldToLocal(world);
            ghost.style.left = local.x + 14f;
            ghost.style.top = local.y + 8f;

            Rect view = treeScroll.worldBound;
            if (view.Contains(world))
            {
                Vector2 offset = treeScroll.scrollOffset;
                if (world.y < view.y + AutoScrollEdge)
                {
                    treeScroll.scrollOffset = new Vector2(offset.x, Mathf.Max(0f, offset.y - AutoScrollStep));
                }
                else if (world.y > view.yMax - AutoScrollEdge)
                {
                    treeScroll.scrollOffset = new Vector2(offset.x, offset.y + AutoScrollStep);
                }
            }

            DropTarget target = ResolveTarget(world, dragSource);
            ShowFeedback(target);
        }

        private void ShowFeedback(DropTarget target)
        {
            bool line = (target.Mode == DropMode.Before || target.Mode == DropMode.After) && target.LineRow >= 0 && target.LineRow < treeRows.Count;
            if (line)
            {
                Rect rowRect = treeRows[target.LineRow].Element.worldBound;
                Vector2 topLeft = treeHost.WorldToLocal(rowRect.position);
                float y = target.LineAfter ? topLeft.y + rowRect.height : topLeft.y;
                Rect host = treeHost.contentRect;
                line = y >= -1f && y <= host.height + 1f;
                if (line)
                {
                    dropLine.style.top = y - 1f;
                    dropLine.style.left = topLeft.x + BaseIndent;
                }
            }

            VeneerStyle.SetDisplay(dropLine, line);
            SetHighlight(target.HighlightRow);
        }

        private void SetHighlight(int row)
        {
            if (highlightRow == row)
            {
                return;
            }

            if (highlightRow >= 0 && highlightRow < treeRows.Count)
            {
                treeRows[highlightRow].Element.RemoveFromClassList("vnd-row--drop");
            }

            highlightRow = row;
            if (row >= 0 && row < treeRows.Count)
            {
                treeRows[row].Element.AddToClassList("vnd-row--drop");
            }
        }

        // ------------------------------------------------------------------ what is under the pointer

        // Rows have one fixed height and sit in one column, so the row under the pointer is plain arithmetic on the first row's bounds.
        private DropTarget ResolveTarget(Vector2 world, DragSource source)
        {
            var target = new DropTarget { Mode = DropMode.None, HighlightRow = -1, LineRow = -1 };
            if (layout == null || treeRows.Count == 0 || nodes.Count == 0 || !treeScroll.worldBound.Contains(world))
            {
                return target;
            }

            Rect first = treeRows[0].Element.worldBound;
            if (first.height <= 0f)
            {
                return target;
            }

            float relative = (world.y - first.y) / first.height;
            int index = Mathf.FloorToInt(relative);
            if (index < 0 || index >= nodes.Count)
            {
                if (layout.Pages.Count == 0 && IsEntryLike(source))
                {
                    target.Mode = DropMode.Into;
                    target.IntoEmptyLayout = true;
                }

                return target;
            }

            bool lower = relative - index >= 0.5f;
            Node hover = nodes[index];
            switch (source.Kind)
            {
                case DragKind.Field:
                case DragKind.Entry:
                case DragKind.Item:
                    ResolveEntryTarget(ref target, source, hover, index, lower);
                    break;
                case DragKind.Page:
                    ResolvePageTarget(ref target, source, hover, index);
                    break;
                case DragKind.Block:
                    ResolveBlockTarget(ref target, source, hover, index);
                    break;
            }

            return target;
        }

        private static bool IsEntryLike(DragSource source)
        {
            return source.Kind == DragKind.Field || source.Kind == DragKind.Entry || source.Kind == DragKind.Item;
        }

        private void ResolveEntryTarget(ref DropTarget target, DragSource source, Node hover, int index, bool lower)
        {
            switch (hover.Kind)
            {
                case NodeKind.Entry:
                    if (source.Kind == DragKind.Entry && source.Node.Page == hover.Page && source.Node.Block == hover.Block && source.Node.Entry == hover.Entry)
                    {
                        return;
                    }

                    target.Mode = lower ? DropMode.After : DropMode.Before;
                    target.Node = hover;
                    target.LineRow = index;
                    target.LineAfter = lower;
                    return;
                case NodeKind.Block:
                case NodeKind.Page:
                    target.Mode = DropMode.Into;
                    target.Node = hover;
                    target.HighlightRow = index;
                    return;
                case NodeKind.HiddenHeader:
                case NodeKind.HiddenItem:
                    if (source.IsNote || source.IsCustom || layout.IsHidden(source.Path))
                    {
                        return;
                    }

                    target.Mode = DropMode.Hide;
                    target.HighlightRow = nodes.Count - 1 - layout.HiddenPaths.Count;
                    return;
                default:
                    if (layout.Pages.Count == 0)
                    {
                        target.Mode = DropMode.Into;
                        target.IntoEmptyLayout = true;
                    }

                    return;
            }
        }

        private void ResolvePageTarget(ref DropTarget target, DragSource source, Node hover, int index)
        {
            if (hover.Kind != NodeKind.Page && hover.Kind != NodeKind.Block && hover.Kind != NodeKind.Entry)
            {
                return;
            }

            if (hover.Page == source.Node.Page)
            {
                return;
            }

            SpanOf(NodeKind.Page, hover.Page, 0, out int first, out int last);
            bool after = (index - first) * 2 >= last - first + 1;
            target.Mode = after ? DropMode.After : DropMode.Before;
            target.Node = new Node(NodeKind.Page, hover.Page, 0, 0);
            target.LineRow = after ? last : first;
            target.LineAfter = after;
        }

        private void ResolveBlockTarget(ref DropTarget target, DragSource source, Node hover, int index)
        {
            if (hover.Kind == NodeKind.Page)
            {
                target.Mode = DropMode.Into;
                target.Node = hover;
                target.HighlightRow = index;
                return;
            }

            if (hover.Kind != NodeKind.Block && hover.Kind != NodeKind.Entry)
            {
                return;
            }

            if (hover.Page == source.Node.Page && hover.Block == source.Node.Block)
            {
                return;
            }

            SpanOf(NodeKind.Block, hover.Page, hover.Block, out int first, out int last);
            bool after = (index - first) * 2 >= last - first + 1;
            target.Mode = after ? DropMode.After : DropMode.Before;
            target.Node = new Node(NodeKind.Block, hover.Page, hover.Block, 0);
            target.LineRow = after ? last : first;
            target.LineAfter = after;
        }

        // The first and last row of a tab (its cards and fields included) or of a card (its fields included).
        private void SpanOf(NodeKind kind, int page, int block, out int first, out int last)
        {
            first = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                Node node = nodes[i];
                if (node.Kind == kind && node.Page == page && (kind == NodeKind.Page || node.Block == block))
                {
                    first = i;
                    break;
                }
            }

            last = first;
            for (int i = first + 1; i < nodes.Count; i++)
            {
                NodeKind next = nodes[i].Kind;
                bool inside = kind == NodeKind.Page
                    ? next == NodeKind.Block || next == NodeKind.Entry
                    : next == NodeKind.Entry;
                if (!inside)
                {
                    break;
                }

                last = i;
            }
        }

        // ------------------------------------------------------------------ performing the drop

        private void PerformDrop(DragSource source, DropTarget target)
        {
            if (!Editable || target.Mode == DropMode.None)
            {
                return;
            }

            switch (source.Kind)
            {
                case DragKind.Field:
                case DragKind.Entry:
                case DragKind.Item:
                    if (target.Mode == DropMode.Hide && !source.IsNote && !source.IsCustom)
                    {
                        Undo.RecordObject(layout, "Hide field");
                        RemoveEntryByPath(source.Path);
                        if (!layout.IsHidden(source.Path))
                        {
                            layout.HiddenPaths.Add(source.Path);
                        }

                        SetSelection(NodeKind.HiddenHeader, 0, 0, 0);
                    }
                    else
                    {
                        DropEntry(source, target);
                    }

                    break;
                case DragKind.Block:
                    DropBlock(source.Node, target);
                    break;
                case DragKind.Page:
                    DropPage(source.Node, target);
                    break;
            }

            LayoutEdited();
        }

        // Puts the field at the target. An existing entry is moved (keeping its label, unit, rules); a hidden or unplaced field gets a new entry.
        private void DropEntry(DragSource source, DropTarget target)
        {
            Undo.RecordObject(layout, "Place field");
            List<VeneerLayoutEntry> list;
            int index;
            if (target.IntoEmptyLayout)
            {
                layout.Pages.Add(new VeneerLayoutPage { Title = "General" });
                layout.Pages[0].Blocks.Add(new VeneerLayoutBlock { Title = "Fields" });
                list = layout.Pages[0].Blocks[0].Entries;
                index = 0;
            }
            else
            {
                Node node = target.Node;
                switch (target.Mode)
                {
                    case DropMode.Before:
                    case DropMode.After:
                        list = layout.Pages[node.Page].Blocks[node.Block].Entries;
                        index = node.Entry + (target.Mode == DropMode.After ? 1 : 0);
                        break;
                    default:
                        if (node.Kind == NodeKind.Page)
                        {
                            List<VeneerLayoutBlock> blocks = layout.Pages[node.Page].Blocks;
                            if (blocks.Count == 0)
                            {
                                blocks.Add(new VeneerLayoutBlock { Title = "Fields" });
                            }

                            list = blocks[blocks.Count - 1].Entries;
                        }
                        else
                        {
                            list = layout.Pages[node.Page].Blocks[node.Block].Entries;
                        }

                        index = list.Count;
                        break;
                }
            }

            // A dragged tree entry (field or note) is found by its row, so notes (empty path) work; a field from the left pane or the Hidden list by its path.
            VeneerLayoutEntry entry = null;
            List<VeneerLayoutEntry> oldList = null;
            bool placedWhole = false;
            if (source.Kind == DragKind.Entry)
            {
                oldList = layout.Pages[source.Node.Page].Blocks[source.Node.Block].Entries;
                entry = oldList[source.Node.Entry];
            }
            else if (source.Kind == DragKind.Item)
            {
                // A custom item is placed once: dragging a placed one moves its entry.
                entry = FindCustomEntry(source.CustomId, out int itemPage, out int itemBlock);
                if (entry != null)
                {
                    oldList = layout.Pages[itemPage].Blocks[itemBlock].Entries;
                }
            }
            else
            {
                entry = layout.FindEntry(source.Path, out int oldPage, out int oldBlock);
                if (entry != null)
                {
                    oldList = layout.Pages[oldPage].Blocks[oldBlock].Entries;
                }
            }

            if (entry != null)
            {
                int oldIndex = oldList.IndexOf(entry);
                oldList.RemoveAt(oldIndex);
                if (ReferenceEquals(oldList, list) && oldIndex < index)
                {
                    index--;
                }
            }
            else if (source.Kind == DragKind.Item)
            {
                entry = new VeneerLayoutEntry { IsCustom = true, CustomId = source.CustomId };
            }
            else
            {
                entry = new VeneerLayoutEntry { Path = source.Path };
                layout.HiddenPaths.Remove(source.Path);
                placedWhole = true;
            }

            index = Mathf.Clamp(index, 0, list.Count);
            list.Insert(index, entry);

            // A struct placed whole replaces the single fields of it that were placed or hidden before, so nothing is drawn twice.
            if (placedWhole)
            {
                RemoveDescendantRefs(source.Path, entry);
            }

            SelectEntryObject(entry);
        }

        // Selects an entry by reference (a note has no path to look it up by).
        private void SelectEntryObject(VeneerLayoutEntry entry)
        {
            List<VeneerLayoutPage> pages = layout.Pages;
            for (int p = 0; p < pages.Count; p++)
            {
                List<VeneerLayoutBlock> blocks = pages[p].Blocks;
                for (int b = 0; b < blocks.Count; b++)
                {
                    int found = blocks[b].Entries.IndexOf(entry);
                    if (found >= 0)
                    {
                        SetSelection(NodeKind.Entry, p, b, found);
                        return;
                    }
                }
            }
        }

        private void DropBlock(Node source, DropTarget target)
        {
            Undo.RecordObject(layout, "Move card");
            VeneerLayoutBlock block = layout.Pages[source.Page].Blocks[source.Block];
            List<VeneerLayoutBlock> oldList = layout.Pages[source.Page].Blocks;
            Node node = target.Node;
            List<VeneerLayoutBlock> list = layout.Pages[node.Page].Blocks;
            int index = target.Mode == DropMode.Into ? list.Count : node.Block + (target.Mode == DropMode.After ? 1 : 0);
            oldList.RemoveAt(source.Block);
            if (ReferenceEquals(oldList, list) && source.Block < index)
            {
                index--;
            }

            index = Mathf.Clamp(index, 0, list.Count);
            list.Insert(index, block);
            SetSelection(NodeKind.Block, node.Page, index, 0);
        }

        private void DropPage(Node source, DropTarget target)
        {
            Undo.RecordObject(layout, "Move tab");
            VeneerLayoutPage page = layout.Pages[source.Page];
            int index = target.Node.Page + (target.Mode == DropMode.After ? 1 : 0);
            layout.Pages.RemoveAt(source.Page);
            if (source.Page < index)
            {
                index--;
            }

            index = Mathf.Clamp(index, 0, layout.Pages.Count);
            layout.Pages.Insert(index, page);
            SetSelection(NodeKind.Page, index, 0, 0);
        }
    }
}
