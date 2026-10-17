using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>Where a <see cref="VeneerCustomItem"/> can be placed.</summary>
    public enum VeneerCustomItemSlot
    {
        /// <summary>An item of a card, placed like a field (a button, a group of computed hints, a list with its own editor...).</summary>
        Item,
        /// <summary>A small element drawn at the right end of the header (a state pill, an icon button). It is drawn only when the layout has an entry for it.</summary>
        HeaderBadge,
        /// <summary>
        /// Drawn in a footer area at the very end of the Inspector, after the tabs, the cards and the Other card, so it is always visible whatever tab
        /// is selected (a Compile button). Like a header badge it is drawn only when the layout has an entry for it; several footer items are drawn
        /// in registration order.
        /// </summary>
        Footer,
        /// <summary>
        /// Drawn between the header and the tab view (or the cards), outside the tabs, so it is always visible (a state strip, a main button). Like a
        /// header badge it is drawn only when the layout has an entry for it; several are drawn in registration order.
        /// </summary>
        AfterHeader
    }

    /// <summary>
    /// A piece of an Inspector that is not a field and that a system registers with <see cref="VeneerCustomItems.Register"/>: a Compile
    /// button, a state pill, computed hints. A layout holds it as an entry with <see cref="VeneerLayoutEntry.IsCustom"/>; the Designer
    /// can move it, retitle its card and delete it, but not edit its inside.
    /// </summary>
    public sealed class VeneerCustomItem
    {
        /// <summary>The stable id stored in layouts (<see cref="VeneerLayoutEntry.CustomId"/>). Unique per target type; never rename it once layouts exist.</summary>
        public string Id { get; set; }

        /// <summary>The name shown in the Designer's palette.</summary>
        public string Label { get; set; }

        /// <summary>A kit icon name for the Designer's palette; may be null.</summary>
        public string Icon { get; set; }

        /// <summary>One line telling the user what the item is; shown in the Designer.</summary>
        public string Description { get; set; }

        /// <summary>Where the item can be placed.</summary>
        public VeneerCustomItemSlot Slot { get; set; }

        /// <summary>
        /// Draws the item. It may add content through <see cref="VeneerCustomItemContext.Builder"/> (the current container is already the
        /// card, the header's right end for a badge, or the footer area for a footer item) and return null, or return one element, which the renderer adds.
        /// </summary>
        public Func<VeneerCustomItemContext, VisualElement> Build { get; set; }

        /// <summary>
        /// The property paths this item is responsible for (for example the list it edits). They count as placed for the automatic Other card,
        /// so they are not drawn there a second time, and the Designer marks them as handled. Null or empty for none.
        /// </summary>
        public IReadOnlyList<string> Claims { get; set; }
    }

    /// <summary>What a <see cref="VeneerCustomItem"/> or a custom widget is given when it is built.</summary>
    public sealed class VeneerCustomItemContext
    {
        /// <summary>Makes a context for <paramref name="builder"/>; <paramref name="layout"/> is the layout being drawn (may be null) and <paramref name="entry"/> the entry that places the item or widget (may be null).</summary>
        public VeneerCustomItemContext(VeneerInspectorBuilder builder, VeneerLayout layout, VeneerLayoutEntry entry = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            Builder = builder;
            Layout = layout;
            Entry = entry;
        }

        /// <summary>
        /// The layout entry that places this item or widget, so it can honour the entry's label, tooltip, unit and range overrides. Null when a
        /// widget is drawn through the automatic Other card (no entry) or the item is built outside a layout.
        /// </summary>
        public VeneerLayoutEntry Entry { get; }

        /// <summary>The data the Inspector edits.</summary>
        public SerializedObject SerializedObject => Builder.SerializedObject;

        /// <summary>Every selected object (the same as <c>SerializedObject.targetObjects</c>).</summary>
        public UnityEngine.Object[] Targets => Builder.SerializedObject.targetObjects;

        /// <summary>The builder; its current container is where the item belongs.</summary>
        public VeneerInspectorBuilder Builder { get; }

        /// <summary>The brand in use (never null).</summary>
        public VeneerBrand Brand => Builder.CurrentBrand;

        /// <summary>The layout being drawn; may be null when the item is built outside a layout.</summary>
        public VeneerLayout Layout { get; }

        /// <summary>
        /// Runs <paramref name="action"/> on the builder's shared 200 ms scheduler (paused while the Inspector is not on a panel). It first
        /// runs on the first tick, not at once: do the first update yourself when you build the item.
        /// </summary>
        public void Poll(Action action)
        {
            Builder.Poll(action);
        }
    }

    /// <summary>
    /// The registry of custom items and custom widgets, per target type. Systems fill it from their
    /// <see cref="IVeneerDefaultLayoutProvider.RegisterItems"/>; it lives until the next domain reload (and is only used from the editor's main thread).
    /// Every lookup first makes sure the providers have been discovered, so the items of a system are known even when no default layout was asked for.
    /// </summary>
    public static class VeneerCustomItems
    {
        private static readonly Dictionary<Type, List<VeneerCustomItem>> Items = new Dictionary<Type, List<VeneerCustomItem>>(8);
        private static readonly Dictionary<Type, Dictionary<string, Func<VeneerCustomItemContext, SerializedProperty, VisualElement>>> Widgets =
            new Dictionary<Type, Dictionary<string, Func<VeneerCustomItemContext, SerializedProperty, VisualElement>>>(8);

        /// <summary>Registers <paramref name="item"/> for <paramref name="targetType"/>. An item with the same id on the same type is replaced.</summary>
        public static void Register(Type targetType, VeneerCustomItem item)
        {
            if (targetType == null)
            {
                throw new ArgumentNullException(nameof(targetType));
            }

            if (item == null || string.IsNullOrEmpty(item.Id))
            {
                throw new ArgumentException("A custom item needs an Id.", nameof(item));
            }

            if (!Items.TryGetValue(targetType, out List<VeneerCustomItem> list))
            {
                list = new List<VeneerCustomItem>(4);
                Items.Add(targetType, list);
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id == item.Id)
                {
                    list[i] = item;
                    return;
                }
            }

            list.Add(item);
        }

        /// <summary>
        /// The items of <paramref name="targetType"/> and of its base types: the type's own first, then each base type's, each in registration
        /// order. An id registered on a more derived type hides the same id of a base type. Makes a new list: not for refresh paths.
        /// </summary>
        public static List<VeneerCustomItem> For(Type targetType)
        {
            VeneerDefaultLayouts.EnsureRegistered();
            var result = new List<VeneerCustomItem>(8);
            for (Type t = targetType; t != null && t != typeof(object); t = t.BaseType)
            {
                if (!Items.TryGetValue(t, out List<VeneerCustomItem> list))
                {
                    continue;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (!ContainsId(result, list[i].Id))
                    {
                        result.Add(list[i]);
                    }
                }
            }

            return result;
        }

        /// <summary>Finds the item with <paramref name="id"/> on <paramref name="targetType"/> or its nearest base type; null when there is none.</summary>
        public static VeneerCustomItem Find(Type targetType, string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            VeneerDefaultLayouts.EnsureRegistered();
            for (Type t = targetType; t != null && t != typeof(object); t = t.BaseType)
            {
                if (!Items.TryGetValue(t, out List<VeneerCustomItem> list))
                {
                    continue;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Id == id)
                    {
                        return list[i];
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Registers a custom widget: <paramref name="build"/> draws the property at <paramref name="propertyPath"/> (a top-level or nested path)
        /// of <paramref name="targetType"/> instead of the generic widget. It returns the element to add (or null when it added its own through
        /// the context's builder). The layout's label and tooltip overrides do not apply to it. Registering the same path again replaces it.
        /// </summary>
        public static void RegisterWidget(Type targetType, string propertyPath, Func<VeneerCustomItemContext, SerializedProperty, VisualElement> build)
        {
            if (targetType == null)
            {
                throw new ArgumentNullException(nameof(targetType));
            }

            if (string.IsNullOrEmpty(propertyPath))
            {
                throw new ArgumentException("A custom widget needs a property path.", nameof(propertyPath));
            }

            if (build == null)
            {
                throw new ArgumentNullException(nameof(build));
            }

            if (!Widgets.TryGetValue(targetType, out Dictionary<string, Func<VeneerCustomItemContext, SerializedProperty, VisualElement>> map))
            {
                map = new Dictionary<string, Func<VeneerCustomItemContext, SerializedProperty, VisualElement>>(4);
                Widgets.Add(targetType, map);
            }

            map[propertyPath] = build;
        }

        /// <summary>Finds the custom widget of <paramref name="propertyPath"/> on <paramref name="targetType"/> or its nearest base type.</summary>
        public static bool TryGetWidget(
            Type targetType, string propertyPath, out Func<VeneerCustomItemContext, SerializedProperty, VisualElement> build)
        {
            build = null;
            if (string.IsNullOrEmpty(propertyPath))
            {
                return false;
            }

            VeneerDefaultLayouts.EnsureRegistered();
            for (Type t = targetType; t != null && t != typeof(object); t = t.BaseType)
            {
                if (Widgets.TryGetValue(t, out Dictionary<string, Func<VeneerCustomItemContext, SerializedProperty, VisualElement>> map)
                    && map.TryGetValue(propertyPath, out build))
                {
                    return true;
                }
            }

            build = null;
            return false;
        }

        /// <summary>
        /// Forgets every registered item and widget. They are registered again only when <see cref="VeneerDefaultLayouts.Invalidate"/> has been
        /// called too (providers register once per discovery); a domain reload resets everything by itself.
        /// </summary>
        public static void Clear()
        {
            Items.Clear();
            Widgets.Clear();
        }

        private static bool ContainsId(List<VeneerCustomItem> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id == id)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
