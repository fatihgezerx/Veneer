using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// A list of a <c>[Serializable]</c> class whose elements are drawn from a <see cref="VeneerItemLayout"/>: a header (title, count pill, Add
    /// button) and one container per element, a bordered card with a head row or one flat row (see <see cref="VeneerItemLook"/>). The fields
    /// of an element are the design's entries, each drawn with the layout renderer's own widget logic on the element's property, so every
    /// widget (number, slider, enum, toggle, object slot with picture, a nested list...) works inside an element. A nested list that has its
    /// own design is another <see cref="VeneerItemList"/> (three levels deep at most). Elements reorder by their handle (a marker line shows
    /// the drop place), fold, and are added and removed through the <see cref="SerializedObject"/>, so Undo works. The structure is rebuilt
    /// only when the array size or the design's signature changes; every other data change only refreshes titles, pills, show-if rules and the
    /// optional <see cref="VeneerItemDecorators"/> hook. Nothing runs per frame: one <c>TrackSerializedObjectValue</c> drives it.
    /// </summary>
    public sealed class VeneerItemList : VisualElement
    {
        private const string SheetRelativePath = "/Styles/VeneerItemList.uss";
        private const string DefaultAddLabel = "Add";
        private const string DefaultEmptyText = "No items yet.";
        private const string MixedText = "The selected objects have lists of different sizes. Edit them one at a time.";
        private const string FoldOpenIcon = "chevron-down";
        private const string FoldClosedIcon = "chevron-right";
        private const float MarkerThickness = 2f;

        private static readonly string[] CountTexts = BuildCountTexts(100);

        private readonly VeneerInspectorBuilder builder;
        private readonly VeneerLayout designLayout;
        private readonly SerializedProperty list;
        private readonly VeneerItemLayout design;
        private readonly string listPath;
        private readonly int depth;
        private readonly VeneerItemList outer;
        private readonly VeneerItemContext context = new VeneerItemContext();
        private readonly List<ItemView> views = new List<ItemView>(8);
        private readonly List<CellRule> rules = new List<CellRule>(8);
        private readonly List<bool> folded = new List<bool>(8);

        private Action<VeneerItemContext> decorator;
        private Action<SerializedProperty> initializer;
        private Label headerCount;
        private Button addButton;
        private Label emptyLabel;
        private Label mixedNotice;
        private VisualElement itemsHost;
        private VisualElement marker;
        private int builtSize = -1;
        private int builtSignature;

        // Drag state of the element handle that is being dragged (-1 = none).
        private int dragFrom = -1;
        private int dragSlot;
        private VisualElement dragHandle;

        private VeneerItemList(
            VeneerInspectorBuilder builder, VeneerLayout layout, SerializedProperty list, VeneerItemLayout design, string label, int depth, VeneerItemList parent)
        {
            this.builder = builder;
            designLayout = layout;
            this.list = list.Copy();
            this.design = design;
            this.depth = depth;
            outer = parent;
            listPath = VeneerItemPaths.Normalize(list.propertyPath);
            tooltip = list.tooltip;
            AddToClassList("vni-list");

            StyleSheet sheet = Sheet;
            if (sheet != null)
            {
                styleSheets.Add(sheet);
            }

            UnityEngine.Object target = builder.SerializedObject.targetObject;
            if (target != null)
            {
                VeneerItemDecorators.TryGet(target.GetType(), listPath, out decorator);
                VeneerItemDecorators.TryGetNew(target.GetType(), listPath, out initializer);
            }

            BuildHeader(label ?? list.displayName);
            Rebuild();

            // One tracker for everything: size changes, edits of any field, Undo and Redo all arrive here.
            this.TrackSerializedObjectValue(builder.SerializedObject, OnChanged);

            // Data may have changed while the control was detached (a rebuilt Inspector, an Undo): catch up once when it is shown.
            RegisterCallback<AttachToPanelEvent>(OnAttached);
        }

        private void OnAttached(AttachToPanelEvent evt)
        {
            if (builder.SerializedObject.targetObject != null && !IsStale())
            {
                Sync();
            }
        }

        /// <summary>The style sheet of the item list (<c>Styles/VeneerItemList.uss</c>); null while the kit folder or the file cannot be found yet.</summary>
        public static StyleSheet Sheet
        {
            get
            {
                string kit = VeneerStyle.KitFolder;
                return kit == null ? null : VeneerStyle.LoadSheet(kit + SheetRelativePath);
            }
        }

        /// <summary>
        /// Makes an item list for <paramref name="list"/> (an array or List property of a serializable class). The renderer calls it for a list
        /// that has a <see cref="VeneerItemLayout"/>; add the result to a container of the builder.
        /// </summary>
        /// <param name="builder">The builder of the Inspector (its serialized object holds the list; its brand is used).</param>
        /// <param name="layout">The layout being drawn (for nested designs); may be an empty layout.</param>
        /// <param name="list">The list property; it is copied.</param>
        /// <param name="design">How an element is drawn.</param>
        /// <param name="label">The title of the list; null uses the property's display name.</param>
        /// <param name="depth">How many designed lists enclose this one (0 at the top). Callers should not exceed <c>2</c>.</param>
        public static VisualElement Create(
            VeneerInspectorBuilder builder, VeneerLayout layout, SerializedProperty list, VeneerItemLayout design, string label = null, int depth = 0)
        {
            return Create(builder, layout, list, design, label, depth, null);
        }

        internal static VisualElement Create(
            VeneerInspectorBuilder builder, VeneerLayout layout, SerializedProperty list, VeneerItemLayout design, string label, int depth, VeneerItemList parent)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (design == null)
            {
                throw new ArgumentNullException(nameof(design));
            }

            if (list == null || !list.isArray || list.propertyType == SerializedPropertyType.String)
            {
                throw new ArgumentException("VeneerItemList needs an array or List property.", nameof(list));
            }

            return new VeneerItemList(builder, layout, list, design, label, depth, parent);
        }

        // ------------------------------------------------------------------ header and frame

        private void BuildHeader(string title)
        {
            addButton = VeneerStyle.Button(
                string.IsNullOrEmpty(design.AddLabel) ? DefaultAddLabel : design.AddLabel, "plus", "Add an item at the end.", OnAdd, false, builder.CurrentBrand);
            if (!design.AddAtEnd)
            {
                var header = new VisualElement();
                header.AddToClassList("vni-list__header");
                var titleLabel = new Label(VeneerText.TitleCase(title));
                titleLabel.AddToClassList("vni-list__title");
                header.Add(titleLabel);
                headerCount = VeneerStyle.Pill("0", "info");
                header.Add(headerCount);
                header.Add(VeneerStyle.Spacer());
                header.Add(addButton);
                Add(header);
            }

            mixedNotice = VeneerStyle.MutedLabel(MixedText);
            mixedNotice.AddToClassList("vni-list__notice");
            VeneerStyle.SetDisplay(mixedNotice, false);
            Add(mixedNotice);

            emptyLabel = VeneerStyle.MutedLabel(string.IsNullOrEmpty(design.EmptyText) ? DefaultEmptyText : design.EmptyText);
            emptyLabel.AddToClassList("vni-list__empty");
            Add(emptyLabel);

            itemsHost = new VisualElement();
            itemsHost.AddToClassList("vni-list__items");
            Add(itemsHost);
            if (design.AddAtEnd)
            {
                // The button under the elements, at the left (like the Add buttons of a group's pools).
                addButton.style.alignSelf = Align.FlexStart;
                Add(addButton);
            }

            marker = new VisualElement();
            marker.AddToClassList("vni-marker");
            marker.pickingMode = PickingMode.Ignore;
            VeneerStyle.SetDisplay(marker, false);
        }

        // ------------------------------------------------------------------ structure

        // True while an enclosing list has changed size since it was built: this list's property path may not exist any more, so nothing
        // here may touch it (the enclosing list is about to rebuild and drop this control).
        private bool IsStale()
        {
            if (outer == null)
            {
                return false;
            }

            if (outer.IsStale())
            {
                return true;
            }

            int size = outer.list.hasMultipleDifferentValues ? 0 : outer.list.arraySize;
            return size != outer.builtSize;
        }

        private int Signature(bool mixed)
        {
            int signature = design.Entries.Count;
            signature = (signature << 1) | (design.Look == VeneerItemLook.Card ? 1 : 0);
            signature = (signature << 1) | (design.ShowHandle ? 1 : 0);
            signature = (signature << 1) | (design.ShowRemove ? 1 : 0);
            signature = (signature << 1) | (design.ShowFold ? 1 : 0);
            signature = (signature << 1) | (design.ShowCount ? 1 : 0);
            signature = (signature << 1) | (design.AddAtEnd ? 1 : 0);
            return (signature << 1) | (mixed ? 1 : 0);
        }

        private void OnChanged(SerializedObject changed)
        {
            if (panel == null || changed.targetObject == null || IsStale())
            {
                return;
            }

            Sync();
        }

        // Rebuilds when the size or the design changed, otherwise only refreshes what shows data.
        private void Sync()
        {
            bool mixed = list.hasMultipleDifferentValues;
            int size = mixed ? 0 : list.arraySize;
            if (size != builtSize || Signature(mixed) != builtSignature)
            {
                Rebuild();
                return;
            }

            Refresh(true);
        }

        private void Rebuild()
        {
            CancelDrag();
            views.Clear();
            rules.Clear();
            itemsHost.Clear();
            itemsHost.Add(marker);

            bool mixed = list.hasMultipleDifferentValues;
            int size = mixed ? 0 : list.arraySize;
            while (folded.Count < size)
            {
                folded.Add(false);
            }

            if (folded.Count > size)
            {
                folded.RemoveRange(size, folded.Count - size);
            }

            for (int i = 0; i < size; i++)
            {
                BuildView(i);
            }

            builtSize = size;
            builtSignature = Signature(mixed);
            VeneerStyle.SetDisplay(mixedNotice, mixed);
            addButton.SetEnabled(!mixed);

            // Fields made after the Inspector was bound need a bind of their own.
            itemsHost.Bind(builder.SerializedObject);
            Refresh(false);
        }

        private void BuildView(int index)
        {
            var view = new ItemView { Index = index };
            SerializedProperty element = list.GetArrayElementAtIndex(index).Copy();
            view.Element = element;
            bool card = design.Look == VeneerItemLook.Card;

            var container = new VisualElement();
            container.AddToClassList("vni-item");
            container.AddToClassList(card ? "vni-item--card" : "vni-item--row");
            container.userData = view;
            view.Container = container;

            var body = new VisualElement();
            body.AddToClassList("vni-item__body");
            view.Body = body;

            if (card)
            {
                var head = new VisualElement();
                head.AddToClassList("vni-item__head");
                view.Head = head;
                if (design.ShowHandle)
                {
                    head.Add(MakeHandle(view));
                }

                view.Title = MakeTitle(view, element, index);
                head.Add(view.Title);
                CollectCountList(view, element);
                if (design.ShowCount && view.CountList != null)
                {
                    view.Count = VeneerStyle.Pill("0", "info");
                    head.Add(view.Count);
                }

                if (design.ShowFold)
                {
                    view.Fold = VeneerStyle.IconButton(FoldOpenIcon, "Fold or unfold this item.", null, builder.CurrentBrand);
                    view.Fold.userData = view;
                    view.Fold.RegisterCallback<ClickEvent>(OnFoldClick);
                    head.Add(view.Fold);
                }

                if (design.ShowRemove)
                {
                    head.Add(MakeRemove(view));
                }

                container.Add(head);
                container.Add(body);
            }
            else
            {
                if (design.ShowHandle)
                {
                    container.Add(MakeHandle(view));
                }

                container.Add(body);
                if (design.ShowRemove)
                {
                    container.Add(MakeRemove(view));
                }
            }

            BuildLines(view, element, body);
            itemsHost.Add(container);
            views.Add(view);
        }

        private VisualElement MakeHandle(ItemView view)
        {
            var handle = new VisualElement();
            handle.AddToClassList("vni-handle");
            handle.tooltip = "Drag to reorder.";
            handle.userData = view;
            handle.Add(VeneerStyle.IconElement("drag-handle", "vn-icon--sm", builder.CurrentBrand));
            handle.RegisterCallback<PointerDownEvent>(OnHandleDown);
            handle.RegisterCallback<PointerMoveEvent>(OnHandleMove);
            handle.RegisterCallback<PointerUpEvent>(OnHandleUp);
            handle.RegisterCallback<PointerCaptureOutEvent>(OnHandleCaptureOut);
            view.Handle = handle;
            return handle;
        }

        private Button MakeRemove(ItemView view)
        {
            Button remove = VeneerStyle.IconButton("trash", "Remove this item.", null, builder.CurrentBrand);
            remove.userData = view;
            remove.RegisterCallback<ClickEvent>(OnRemoveClick);
            view.Remove = remove;
            return remove;
        }

        // An editable text field bound to the title path when the design names a text field; otherwise the label "Item n".
        private VisualElement MakeTitle(ItemView view, SerializedProperty element, int index)
        {
            SerializedProperty text = string.IsNullOrEmpty(design.TitlePath) ? null : element.FindPropertyRelative(design.TitlePath);
            string fallback = "Item " + (index + 1);
            if (text != null && text.propertyType == SerializedPropertyType.String)
            {
                var field = new TextField();
                field.AddToClassList("vni-item__title-field");
                field.textEdition.placeholder = fallback;
                field.tooltip = text.tooltip;
                field.BindProperty(text.Copy());
                return field;
            }

            var label = new Label(fallback);
            label.AddToClassList("vni-item__title");
            return label;
        }

        // The first list among the design's entries: the count pill shows its size.
        private void CollectCountList(ItemView view, SerializedProperty element)
        {
            List<VeneerLayoutEntry> entries = design.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                VeneerLayoutEntry entry = entries[i];
                if (entry == null || entry.IsNote || entry.IsCustom || string.IsNullOrEmpty(entry.Path))
                {
                    continue;
                }

                SerializedProperty property = element.FindPropertyRelative(entry.Path);
                if (property != null && property.isArray && property.propertyType != SerializedPropertyType.String)
                {
                    view.CountList = property.Copy();
                    return;
                }
            }
        }

        // Entries become cells; a cell with SameRow joins the line of the cell before it, any other starts a new line (a wrapping row).
        private void BuildLines(ItemView view, SerializedProperty element, VisualElement body)
        {
            List<VeneerLayoutEntry> entries = design.Entries;
            view.Cells = new VisualElement[entries.Count];
            VisualElement line = null;
            for (int e = 0; e < entries.Count; e++)
            {
                VeneerLayoutEntry entry = entries[e];
                if (entry == null || entry.IsCustom)
                {
                    continue;
                }

                var cell = new VisualElement();
                cell.AddToClassList("vni-cell");
                SerializedProperty property = null;
                bool drew;
                using (builder.Into(cell))
                {
                    if (entry.IsNote)
                    {
                        drew = VeneerLayoutRenderer.AddItemNote(builder, entry);
                    }
                    else if (string.IsNullOrEmpty(entry.Path))
                    {
                        drew = false;
                    }
                    else
                    {
                        property = element.FindPropertyRelative(entry.Path);
                        drew = property != null && VeneerLayoutRenderer.AddItemEntry(builder, designLayout, property, entry, depth + 1, this);
                    }
                }

                if (!drew)
                {
                    continue;
                }

                VeneerLayoutRenderer.ApplyWidthHeight(cell, entry);
                if (entry.Width > 0f)
                {
                    cell.style.flexGrow = 0f;
                }

                if (entry.SameRow && line != null)
                {
                    line.Add(cell);
                }
                else
                {
                    line = VeneerStyle.Row(true);
                    line.AddToClassList("vni-line");
                    body.Add(line);
                    line.Add(cell);
                }

                view.Cells[e] = cell;
                AddRule(element, entry, cell);
            }
        }

        // The entry's show-if rule, read relative to the element. A rule that cannot be understood never hides the cell.
        private void AddRule(SerializedProperty element, VeneerLayoutEntry entry, VisualElement cell)
        {
            if (string.IsNullOrEmpty(entry.ShowIfPath))
            {
                return;
            }

            SerializedProperty watched = element.FindPropertyRelative(entry.ShowIfPath);
            VeneerLayoutRenderer.ShowIfRule rule = watched == null ? null : VeneerLayoutRenderer.ShowIfRule.Create(watched, entry.ShowIf, entry.ShowIfValue);
            if (rule != null)
            {
                rules.Add(new CellRule(cell, rule));
            }
        }

        // ------------------------------------------------------------------ refresh (no allocation)

        private void Refresh(bool isRefresh)
        {
            int size = builtSize;
            string countText = CountText(size);
            if (headerCount != null)
            {
                VeneerStyle.SetPill(headerCount, "info", list.hasMultipleDifferentValues ? "-" : countText);
            }

            VeneerStyle.SetDisplay(emptyLabel, size == 0 && !list.hasMultipleDifferentValues);

            for (int i = 0; i < rules.Count; i++)
            {
                VeneerStyle.SetDisplay(rules[i].Cell, rules[i].Rule.IsVisible());
            }

            for (int i = 0; i < views.Count; i++)
            {
                ItemView view = views[i];
                if (view.Count != null)
                {
                    VeneerStyle.SetPill(view.Count, "info", CountText(view.CountList.arraySize));
                }

                ApplyFold(view);
                if (decorator != null)
                {
                    Decorate(view, isRefresh);
                }
            }
        }

        private void ApplyFold(ItemView view)
        {
            bool closed = view.Index < folded.Count && folded[view.Index];
            VeneerStyle.SetDisplay(view.Body, !closed);
            if (view.Fold != null)
            {
                string icon = closed ? FoldClosedIcon : FoldOpenIcon;
                if (!string.Equals(view.FoldIcon, icon, StringComparison.Ordinal))
                {
                    view.FoldIcon = icon;
                    VeneerStyle.SetIcon(view.Fold[0], icon, builder.CurrentBrand);
                }
            }
        }

        private void Decorate(ItemView view, bool isRefresh)
        {
            try
            {
                context.Set(design, listPath, list, view.Element, view.Index, view.Container, view.Head, view.Title, view.Count, view.Cells, view.Cells.Length, isRefresh);
                decorator(context);
            }
            catch (Exception exception)
            {
                // A broken decorator is switched off after one report instead of failing on every change.
                Debug.LogException(exception);
                decorator = null;
            }
            finally
            {
                context.Clear();
            }
        }

        private static string CountText(int value)
        {
            return value >= 0 && value < CountTexts.Length ? CountTexts[value] : value.ToString();
        }

        private static string[] BuildCountTexts(int count)
        {
            var texts = new string[count];
            for (int i = 0; i < count; i++)
            {
                texts[i] = i.ToString();
            }

            return texts;
        }

        // ------------------------------------------------------------------ add, remove, fold

        private void OnAdd()
        {
            if (IsStale() || list.hasMultipleDifferentValues)
            {
                return;
            }

            // Like Unity's own list: the new element starts as a copy of the last one, unless the system registered an initializer.
            list.arraySize++;
            if (initializer != null)
            {
                initializer(list.GetArrayElementAtIndex(list.arraySize - 1));
            }

            list.serializedObject.ApplyModifiedProperties();
            Sync();
        }

        private void OnRemoveClick(ClickEvent evt)
        {
            var view = (ItemView)((VisualElement)evt.currentTarget).userData;
            if (IsStale() || view.Index < 0 || view.Index >= list.arraySize)
            {
                return;
            }

            list.DeleteArrayElementAtIndex(view.Index);
            list.serializedObject.ApplyModifiedProperties();
            Sync();
        }

        private void OnFoldClick(ClickEvent evt)
        {
            var view = (ItemView)((VisualElement)evt.currentTarget).userData;
            if (view.Index >= 0 && view.Index < folded.Count)
            {
                folded[view.Index] = !folded[view.Index];
                ApplyFold(view);
            }
        }

        // ------------------------------------------------------------------ drag to reorder

        private void OnHandleDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || dragFrom >= 0)
            {
                return;
            }

            var handle = (VisualElement)evt.currentTarget;
            var view = (ItemView)handle.userData;
            dragFrom = view.Index;
            dragSlot = view.Index;
            dragHandle = handle;
            handle.CapturePointer(evt.pointerId);
            view.Container.AddToClassList("vni-item--dragging");
            evt.StopPropagation();
        }

        private void OnHandleMove(PointerMoveEvent evt)
        {
            if (dragFrom < 0 || dragHandle == null || !dragHandle.HasPointerCapture(evt.pointerId))
            {
                return;
            }

            float y = itemsHost.WorldToLocal(evt.position).y;
            int slot = 0;
            for (int i = 0; i < views.Count; i++)
            {
                Rect rect = views[i].Container.layout;
                if (y > rect.y + rect.height * 0.5f)
                {
                    slot = i + 1;
                }
            }

            dragSlot = slot;
            ShowMarker(slot);
            evt.StopPropagation();
        }

        private void ShowMarker(int slot)
        {
            if (views.Count == 0)
            {
                VeneerStyle.SetDisplay(marker, false);
                return;
            }

            float top = slot < views.Count
                ? views[slot].Container.layout.y - MarkerThickness
                : views[views.Count - 1].Container.layout.yMax;
            marker.style.top = top;
            VeneerStyle.SetDisplay(marker, true);
        }

        private void OnHandleUp(PointerUpEvent evt)
        {
            if (dragFrom < 0 || dragHandle == null || !dragHandle.HasPointerCapture(evt.pointerId))
            {
                return;
            }

            int from = dragFrom;
            int to = dragSlot > from ? dragSlot - 1 : dragSlot;
            VisualElement handle = dragHandle;
            CancelDrag();
            handle.ReleasePointer(evt.pointerId);
            if (to != from)
            {
                MoveElement(from, to);
            }

            evt.StopPropagation();
        }

        private void OnHandleCaptureOut(PointerCaptureOutEvent evt)
        {
            CancelDrag();
        }

        private void CancelDrag()
        {
            if (dragFrom >= 0 && dragFrom < views.Count)
            {
                views[dragFrom].Container.RemoveFromClassList("vni-item--dragging");
            }

            dragFrom = -1;
            dragHandle = null;
            if (marker != null)
            {
                VeneerStyle.SetDisplay(marker, false);
            }
        }

        private void MoveElement(int from, int to)
        {
            if (IsStale() || from < 0 || to < 0 || from >= list.arraySize || to >= list.arraySize)
            {
                return;
            }

            list.MoveArrayElement(from, to);
            list.serializedObject.ApplyModifiedProperties();
            if (from < folded.Count && to < folded.Count)
            {
                bool wasFolded = folded[from];
                folded.RemoveAt(from);
                folded.Insert(to, wasFolded);
            }

            // The bound fields read the moved data by themselves; titles, pills, folds and decorators are refreshed here.
            Refresh(true);
        }

        // ------------------------------------------------------------------ parts

        private sealed class ItemView
        {
            public int Index;
            public SerializedProperty Element;
            public SerializedProperty CountList;
            public VisualElement Container;
            public VisualElement Head;
            public VisualElement Body;
            public VisualElement Title;
            public VisualElement Handle;
            public Button Fold;
            public Button Remove;
            public Label Count;
            public string FoldIcon;
            public VisualElement[] Cells;
        }

        private readonly struct CellRule
        {
            public readonly VisualElement Cell;
            public readonly VeneerLayoutRenderer.ShowIfRule Rule;

            public CellRule(VisualElement cell, VeneerLayoutRenderer.ShowIfRule rule)
            {
                Cell = cell;
                Rule = rule;
            }
        }
    }
}
