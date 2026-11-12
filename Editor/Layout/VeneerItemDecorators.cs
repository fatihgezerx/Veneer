using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// What an item decorator is given when one element of a designed list (see <see cref="VeneerItemDecorators"/>) has been built or
    /// refreshed. ONE instance is reused for every call of a list, so a decorator must not keep it; reading its members and changing classes
    /// or tooltips of the elements allocates nothing.
    /// </summary>
    public sealed class VeneerItemContext
    {
        private VisualElement[] cells;
        private VeneerItemLayout design;
        private int cellCount;

        /// <summary>The element's container (the card or the flat row). Add or remove USS classes here.</summary>
        public VisualElement Container { get; private set; }

        /// <summary>The head row of a card; null for <see cref="VeneerItemLook.Row"/>.</summary>
        public VisualElement Head { get; private set; }

        /// <summary>The title of a card (a text field when the design has a title path, else a label); null for a row.</summary>
        public VisualElement Title { get; private set; }

        /// <summary>The count pill of a card head (set its kind and text with <c>VeneerStyle.SetPill</c>); null when the design shows none or the look is a row.</summary>
        public Label Count { get; private set; }

        /// <summary>The element's serialized property (<c>groups.Array.data[2]</c>). Valid only during the call.</summary>
        public SerializedProperty Element { get; private set; }

        /// <summary>The list this element belongs to.</summary>
        public SerializedProperty List { get; private set; }

        /// <summary>The element's index in the list.</summary>
        public int Index { get; private set; }

        /// <summary>The list's path without indices (<c>groups.entries</c>).</summary>
        public string ListPath { get; private set; }

        /// <summary>False when the element was just built, true when it is refreshed after a change of the data.</summary>
        public bool IsRefresh { get; private set; }

        /// <summary>The cell that holds the field at <paramref name="relativePath"/> of the design (for a tooltip or a class), or null when the design does not show it.</summary>
        public VisualElement CellOf(string relativePath)
        {
            if (design == null || cells == null)
            {
                return null;
            }

            int count = Math.Min(cellCount, design.Entries.Count);
            for (int i = 0; i < count; i++)
            {
                VeneerLayoutEntry entry = design.Entries[i];
                if (entry != null && string.Equals(entry.Path, relativePath, StringComparison.Ordinal))
                {
                    return cells[i];
                }
            }

            return null;
        }

        internal void Set(
            VeneerItemLayout design, string listPath, SerializedProperty list, SerializedProperty element, int index,
            VisualElement container, VisualElement head, VisualElement title, Label count, VisualElement[] cells, int cellCount, bool refresh)
        {
            Count = count;
            this.design = design;
            ListPath = listPath;
            List = list;
            Element = element;
            Index = index;
            Container = container;
            Head = head;
            Title = title;
            this.cells = cells;
            this.cellCount = cellCount;
            IsRefresh = refresh;
        }

        internal void Clear()
        {
            design = null;
            cells = null;
            Container = null;
            Head = null;
            Title = null;
            Count = null;
            Element = null;
            List = null;
        }
    }

    /// <summary>
    /// The optional hook a system registers to touch the elements of a designed list (see <see cref="VeneerItemList"/>): a row class for an
    /// error or a warning colour, a tooltip, a state pill. The decorator runs when an element container has been built and again whenever
    /// the data changed, so it must be cheap and allocation-free (compare, then add or remove a class). One decorator per list path and
    /// target type; registering again replaces it. Registered from <see cref="IVeneerDefaultLayoutProvider.RegisterItems"/> like custom items.
    /// </summary>
    public static class VeneerItemDecorators
    {
        private static readonly Dictionary<Type, Dictionary<string, Action<VeneerItemContext>>> Decorators =
            new Dictionary<Type, Dictionary<string, Action<VeneerItemContext>>>(4);

        /// <summary>Registers <paramref name="decorate"/> for the list at <paramref name="listPath"/> (indices removed, for example <c>groups.entries</c>) of <paramref name="targetType"/>.</summary>
        public static void Register(Type targetType, string listPath, Action<VeneerItemContext> decorate)
        {
            if (targetType == null)
            {
                throw new ArgumentNullException(nameof(targetType));
            }

            if (string.IsNullOrEmpty(listPath))
            {
                throw new ArgumentException("A decorator needs a list path.", nameof(listPath));
            }

            if (decorate == null)
            {
                throw new ArgumentNullException(nameof(decorate));
            }

            if (!Decorators.TryGetValue(targetType, out Dictionary<string, Action<VeneerItemContext>> map))
            {
                map = new Dictionary<string, Action<VeneerItemContext>>(2);
                Decorators.Add(targetType, map);
            }

            map[listPath] = decorate;
        }

        /// <summary>Finds the decorator of <paramref name="listPath"/> on <paramref name="targetType"/> or its nearest base type.</summary>
        public static bool TryGet(Type targetType, string listPath, out Action<VeneerItemContext> decorate)
        {
            decorate = null;
            if (string.IsNullOrEmpty(listPath))
            {
                return false;
            }

            VeneerDefaultLayouts.EnsureRegistered();
            for (Type t = targetType; t != null && t != typeof(object); t = t.BaseType)
            {
                if (Decorators.TryGetValue(t, out Dictionary<string, Action<VeneerItemContext>> map) && map.TryGetValue(listPath, out decorate))
                {
                    return true;
                }
            }

            decorate = null;
            return false;
        }

        private static readonly Dictionary<Type, Dictionary<string, Action<SerializedProperty>>> Initializers =
            new Dictionary<Type, Dictionary<string, Action<SerializedProperty>>>(4);

        /// <summary>
        /// Registers what happens to a NEW element of the list at <paramref name="listPath"/> after Add: without it the new element is a copy of
        /// the last one (like Unity's own list); with it <paramref name="initialize"/> sets the new element's fields (it must not apply the
        /// serialized object itself).
        /// </summary>
        public static void RegisterNew(Type targetType, string listPath, Action<SerializedProperty> initialize)
        {
            if (targetType == null)
            {
                throw new ArgumentNullException(nameof(targetType));
            }

            if (string.IsNullOrEmpty(listPath))
            {
                throw new ArgumentException("An initializer needs a list path.", nameof(listPath));
            }

            if (initialize == null)
            {
                throw new ArgumentNullException(nameof(initialize));
            }

            if (!Initializers.TryGetValue(targetType, out Dictionary<string, Action<SerializedProperty>> map))
            {
                map = new Dictionary<string, Action<SerializedProperty>>(2);
                Initializers.Add(targetType, map);
            }

            map[listPath] = initialize;
        }

        /// <summary>Finds the new-element initializer of <paramref name="listPath"/> on <paramref name="targetType"/> or its nearest base type.</summary>
        public static bool TryGetNew(Type targetType, string listPath, out Action<SerializedProperty> initialize)
        {
            initialize = null;
            if (string.IsNullOrEmpty(listPath))
            {
                return false;
            }

            VeneerDefaultLayouts.EnsureRegistered();
            for (Type t = targetType; t != null && t != typeof(object); t = t.BaseType)
            {
                if (Initializers.TryGetValue(t, out Dictionary<string, Action<SerializedProperty>> map) && map.TryGetValue(listPath, out initialize))
                {
                    return true;
                }
            }

            initialize = null;
            return false;
        }

        /// <summary>Forgets every registered decorator and initializer (a domain reload does it by itself).</summary>
        public static void Clear()
        {
            Decorators.Clear();
            Initializers.Clear();
        }
    }
}
