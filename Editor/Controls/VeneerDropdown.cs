using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// The kit's animated dropdown. Unity's own popup menu cannot be animated, so the list is built from elements: it is added to the
    /// top of the panel, looks themed (it copies the style classes and sheets of the <c>vn-root</c> the button sits in), opens downward
    /// like a rubber band that is pulled and released, and closes with a short fade. <see cref="Create"/> makes the button; the
    /// <see cref="VeneerControls.EnumDropdown"/> row and <see cref="VeneerInspectorBuilder.Dropdown"/> use it for enum fields.
    /// All durations are multiplied by <see cref="VeneerMotionKit.Scale"/>; with motion off the list opens and closes at once.
    /// </summary>
    public static class VeneerDropdown
    {
        private const string SheetRelativePath = "/Styles/VeneerDropdown.uss";

        private static HashSet<Type> drawerTypes;
        private static bool drawersUnknown;

        /// <summary>The style sheet of the dropdown (<c>Styles/VeneerDropdown.uss</c>); null while the kit folder or the file cannot be found yet.</summary>
        public static StyleSheet Sheet
        {
            get
            {
                string kit = VeneerStyle.KitFolder;
                return kit == null ? null : VeneerStyle.LoadSheet(kit + SheetRelativePath);
            }
        }

        /// <summary>
        /// Makes a dropdown button: a field-like element showing the current choice and a chevron. A click (or Space, Enter or Down while it
        /// has focus) opens the list. The element is a <see cref="VeneerDropdownField"/>, which has <see cref="VeneerDropdownField.SetValue"/>,
        /// <see cref="VeneerDropdownField.Value"/> and <see cref="VeneerDropdownField.SetChoices"/>.
        /// </summary>
        /// <param name="choices">The texts of the choices.</param>
        /// <param name="selected">The index shown at the start; -1 shows an em dash (nothing selected).</param>
        /// <param name="changed">Called with the index after the user picked a choice (not when the value is set from code); may be null.</param>
        /// <param name="brand">Brand for icon lookup and for the popup's look when the button is outside a Veneer tree; may be null.</param>
        /// <param name="icons">Icon name per choice (may be null, shorter, or hold empty entries).</param>
        /// <param name="tooltips">Tooltip per choice (may be null or shorter).</param>
        public static VisualElement Create(
            IReadOnlyList<string> choices,
            int selected,
            Action<int> changed,
            VeneerBrand brand = null,
            IReadOnlyList<string> icons = null,
            IReadOnlyList<string> tooltips = null)
        {
            var field = new VeneerDropdownField(brand);
            field.SetChoices(choices, selected, icons, tooltips);
            if (changed != null)
            {
                field.Changed += changed;
            }

            return field;
        }

        /// <summary>
        /// True when an enum property can be drawn by the dropdown without losing anything Unity would draw: a plain (non-flags) enum whose
        /// C# field has no attribute that needs a drawer and whose type has no custom property drawer. When it cannot be known the answer is false.
        /// </summary>
        /// <param name="property">The property to test.</param>
        /// <param name="keepDecorators">True when <c>[Header]</c> and <c>[Space]</c> on the field must still be drawn (then they make the answer false).</param>
        internal static bool IsPlainEnum(SerializedProperty property, bool keepDecorators)
        {
            if (property.propertyType != SerializedPropertyType.Enum || property.isArray)
            {
                return false;
            }

            if (!VeneerFieldInfo.TryResolve(property, out FieldInfo field, out Type type) || field == null || type == null || !type.IsEnum)
            {
                return false;
            }

            if (Attribute.IsDefined(type, typeof(FlagsAttribute), false))
            {
                return false;
            }

            object[] attributes = field.GetCustomAttributes(typeof(PropertyAttribute), true);
            for (int i = 0; i < attributes.Length; i++)
            {
                object attribute = attributes[i];
                if (attribute is TooltipAttribute)
                {
                    continue;
                }

                if (!keepDecorators && (attribute is HeaderAttribute || attribute is SpaceAttribute))
                {
                    continue;
                }

                return false;
            }

            return !HasTypeDrawer(type);
        }

        // A [CustomPropertyDrawer(typeof(MyEnum))] replaces the field's look. Unity's lookup is internal, so the drawers' attributes are read
        // once (m_Type is private). When that field cannot be read the answer is "maybe": the native field stays.
        private static bool HasTypeDrawer(Type enumType)
        {
            if (drawerTypes == null)
            {
                BuildDrawerTypes();
            }

            return drawersUnknown || drawerTypes.Contains(enumType);
        }

        private static void BuildDrawerTypes()
        {
            drawerTypes = new HashSet<Type>();
            FieldInfo typeField = typeof(CustomPropertyDrawer).GetField("m_Type", BindingFlags.Instance | BindingFlags.NonPublic);
            if (typeField == null)
            {
                drawersUnknown = true;
                return;
            }

            foreach (Type drawer in TypeCache.GetTypesDerivedFrom<PropertyDrawer>())
            {
                object[] attributes = drawer.GetCustomAttributes(typeof(CustomPropertyDrawer), true);
                for (int i = 0; i < attributes.Length; i++)
                {
                    if (typeField.GetValue(attributes[i]) is Type target)
                    {
                        drawerTypes.Add(target);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The button of a <see cref="VeneerDropdown"/>: the current choice, an optional icon and a chevron. Click, or Space / Enter / Down while
    /// it has focus, opens the animated list; Up, Down, Home, End, PageUp, PageDown, a letter, Enter and Escape work while it is open.
    /// Only one list is open at a time. The list is removed when the button leaves the panel and before a domain reload.
    /// </summary>
    public sealed class VeneerDropdownField : VisualElement
    {
        private const string FieldClass = "vn-dd";
        private const string OpenClass = "vn-dd--open";
        private const string EmptyClass = "vn-dd--empty";
        private const string PopupClass = "vn-dd-popup";
        private const string PopupOpenClass = "vn-dd-popup--open";
        private const string ItemClass = "vn-dd-item";
        private const string ItemSelectedClass = "vn-dd-item--selected";
        private const string ItemActiveClass = "vn-dd-item--active";
        private const string RootClass = "vn-root";
        private const string LightClass = "vn-root--light";

        private const float MinPopupWidth = 140f;
        private const float NarrowestPopupWidth = 60f;
        private const float MaxPopupHeight = 260f;
        private const float MinPopupHeight = 48f;
        private const float EdgeGap = 4f;
        private const float OpenMilliseconds = 460f;
        private const float OpenFadeMilliseconds = 150f;
        private const float CloseMilliseconds = 120f;
        private const float StartScaleY = 0.55f;
        private const float CloseScaleY = 0.8f;
        private const int PageSize = 8;

        // One class per item index (capped): each one carries a transition delay so the items fade in one after the other.
        private static readonly string[] StaggerClasses =
        {
            "vn-dd-item--d0", "vn-dd-item--d1", "vn-dd-item--d2", "vn-dd-item--d3", "vn-dd-item--d4",
            "vn-dd-item--d5", "vn-dd-item--d6", "vn-dd-item--d7", "vn-dd-item--d8", "vn-dd-item--d9",
        };

        private static VeneerDropdownField openOwner;

        private readonly VeneerBrand brand;
        private readonly Label valueLabel;
        private readonly VisualElement chevron;
        private readonly EventCallback<KeyDownEvent> keyDown;
        private readonly EventCallback<FocusOutEvent> popupFocusOut;
        private readonly EventCallback<PointerDownEvent> panelPointerDown;
        private readonly EventCallback<WheelEvent> panelWheel;
        private readonly EventCallback<GeometryChangedEvent> panelGeometry;
        private readonly EventCallback<GeometryChangedEvent> popupGeometry;
        private readonly EventCallback<PointerEnterEvent> itemEnter;
        private readonly EventCallback<ClickEvent> itemClick;
        private readonly Action beginOpen;
        private readonly Action finishClosing;

        private string[] choices = Array.Empty<string>();
        private string[] icons;
        private string[] tooltips;
        private string placeholder = "—";
        private int value = -1;
        private VisualElement valueIcon;
        private string valueIconName;

        private bool isOpen;
        private bool placed;
        private bool openUp;
        private int active = -1;
        private VisualElement panelRoot;
        private VisualElement popup;
        private ScrollView list;
        private VisualElement[] items;
        private VisualElement closingPopup;
        private IVisualElementScheduledItem closeTimer;

        static VeneerDropdownField()
        {
            // A list left in a panel would survive nothing useful: remove it before the assemblies reload.
            AssemblyReloadEvents.beforeAssemblyReload += CloseOpenForReload;
        }

        /// <summary>Creates an empty dropdown; fill it with <see cref="SetChoices"/>. Most code uses <see cref="VeneerDropdown.Create"/>.</summary>
        /// <param name="brand">Brand for icon lookup and for the popup's look outside a Veneer tree; may be null.</param>
        public VeneerDropdownField(VeneerBrand brand = null)
        {
            this.brand = brand;
            AddToClassList(FieldClass);
            focusable = true;
            tabIndex = 0;

            valueLabel = new Label();
            valueLabel.AddToClassList("vn-dd__label");
            valueLabel.pickingMode = PickingMode.Ignore;
            Add(valueLabel);
            chevron = VeneerStyle.IconElement("chevron-down", "vn-dd__chevron", brand);
            Add(chevron);

            keyDown = OnKeyDown;
            popupFocusOut = OnPopupFocusOut;
            panelPointerDown = OnPanelPointerDown;
            panelWheel = OnPanelWheel;
            panelGeometry = OnPanelGeometry;
            popupGeometry = OnPopupGeometry;
            itemEnter = OnItemEnter;
            itemClick = OnItemClick;
            beginOpen = BeginOpen;
            finishClosing = FinishClosing;

            this.AddManipulator(new Clickable(OnClicked));
            RegisterCallback(keyDown);
            RegisterCallback<AttachToPanelEvent>(OnAttach);
            RegisterCallback<DetachFromPanelEvent>(OnDetach);
        }

        /// <summary>Raised with the chosen index after the user picked a choice. Not raised by <see cref="SetValue"/>.</summary>
        public event Action<int> Changed;

        /// <summary>The selected index, or -1 when nothing is selected (the button then shows <see cref="Placeholder"/>).</summary>
        public int Value => value;

        /// <summary>How many choices there are.</summary>
        public int ChoiceCount => choices.Length;

        /// <summary>True while the list is open.</summary>
        public bool IsOpen => isOpen;

        /// <summary>The text shown when nothing is selected (an em dash by default; a mixed multi-edit value uses it too).</summary>
        public string Placeholder
        {
            get => placeholder;
            set
            {
                placeholder = value ?? string.Empty;
                UpdateDisplay();
            }
        }

        /// <summary>
        /// Selects a choice without raising <see cref="Changed"/>; -1 (or any index out of range) selects nothing. Safe to call while the
        /// list is open: the highlight follows.
        /// </summary>
        /// <param name="index">The index to show.</param>
        public void SetValue(int index)
        {
            int next = index >= 0 && index < choices.Length ? index : -1;
            if (next == value)
            {
                return;
            }

            value = next;
            UpdateDisplay();
            if (items != null)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    items[i].EnableInClassList(ItemSelectedClass, i == value);
                }
            }
        }

        /// <summary>Replaces the choices. An open list is closed first (at once); the selection is set to <paramref name="selected"/>.</summary>
        /// <param name="choices">The texts of the choices.</param>
        /// <param name="selected">The index to select; -1 for none.</param>
        /// <param name="icons">Icon name per choice (may be null, shorter, or hold empty entries).</param>
        /// <param name="tooltips">Tooltip per choice (may be null or shorter).</param>
        public void SetChoices(
            IReadOnlyList<string> choices, int selected = -1, IReadOnlyList<string> icons = null, IReadOnlyList<string> tooltips = null)
        {
            CloseImmediate();
            this.choices = Copy(choices, false);
            this.icons = icons == null ? null : Copy(icons, true);
            this.tooltips = tooltips == null ? null : Copy(tooltips, true);
            value = selected >= 0 && selected < this.choices.Length ? selected : -1;
            UpdateDisplay();
        }

        // ------------------------------------------------------------------ the button

        private void OnClicked()
        {
            if (!enabledInHierarchy)
            {
                return;
            }

            if (isOpen)
            {
                Close(true);
            }
            else
            {
                Open();
            }
        }

        private void OnAttach(AttachToPanelEvent evt)
        {
            // The sheet goes on the Veneer root the button sits in (once per root); outside a Veneer tree it goes on the button itself.
            StyleSheet sheet = VeneerDropdown.Sheet;
            if (sheet == null)
            {
                return;
            }

            VisualElement host = FindKitRoot() ?? this;
            if (!host.styleSheets.Contains(sheet))
            {
                host.styleSheets.Add(sheet);
            }
        }

        private void OnDetach(DetachFromPanelEvent evt)
        {
            CloseImmediate();
        }

        private void UpdateDisplay()
        {
            bool valid = value >= 0 && value < choices.Length;
            valueLabel.text = valid ? choices[value] : placeholder;
            EnableInClassList(EmptyClass, !valid);

            string iconName = valid && icons != null && value < icons.Length ? icons[value] : null;
            if (string.IsNullOrEmpty(iconName))
            {
                if (valueIcon != null)
                {
                    VeneerStyle.SetDisplay(valueIcon, false);
                }

                return;
            }

            if (valueIcon == null)
            {
                valueIcon = VeneerStyle.IconElement(iconName, "vn-dd__icon", brand);
                Insert(0, valueIcon);
                valueIconName = iconName;
            }
            else if (!string.Equals(valueIconName, iconName, StringComparison.Ordinal))
            {
                VeneerStyle.SetIcon(valueIcon, iconName, brand);
                valueIconName = iconName;
            }

            VeneerStyle.SetDisplay(valueIcon, true);
        }

        private VisualElement FindKitRoot()
        {
            for (VisualElement current = parent; current != null; current = current.parent)
            {
                if (current.ClassListContains(RootClass))
                {
                    return current;
                }
            }

            return null;
        }

        // ------------------------------------------------------------------ opening

        private void Open()
        {
            if (isOpen || choices.Length == 0 || panel == null || !enabledInHierarchy)
            {
                return;
            }

            if (openOwner != null)
            {
                openOwner.CloseImmediate();
            }

            FinishClosing();
            panelRoot = panel.visualTree;
            BuildPopup();
            isOpen = true;
            placed = false;
            openOwner = this;
            EnableInClassList(OpenClass, true);

            panelRoot.Add(popup);
            panelRoot.RegisterCallback(panelPointerDown, TrickleDown.TrickleDown);
            panelRoot.RegisterCallback(panelWheel, TrickleDown.TrickleDown);
            panelRoot.RegisterCallback(panelGeometry);
            EditorApplication.focusChanged += OnApplicationFocusChanged;
        }

        private void BuildPopup()
        {
            VisualElement view = new VisualElement { name = "VeneerDropdownPopup" };
            VisualElement root = FindKitRoot();
            if (root != null)
            {
                // The same look as the tree the button is in: palette, accent, theme, density and motion are classes and sheets on the root.
                foreach (string className in root.GetClasses())
                {
                    view.AddToClassList(className);
                }

                for (int i = 0; i < root.styleSheets.count; i++)
                {
                    StyleSheet sheet = root.styleSheets[i];
                    if (sheet != null && !view.styleSheets.Contains(sheet))
                    {
                        view.styleSheets.Add(sheet);
                    }
                }
            }
            else
            {
                view.styleSheets.Add(VeneerStyle.Sheet);
                view.AddToClassList(RootClass);
                if (!EditorGUIUtility.isProSkin)
                {
                    view.AddToClassList(LightClass);
                }

                VeneerStyle.ApplyBrand(view, brand);
            }

            StyleSheet own = VeneerDropdown.Sheet;
            if (own != null && !view.styleSheets.Contains(own))
            {
                view.styleSheets.Add(own);
            }

            view.AddToClassList(PopupClass);
            view.focusable = true;
            view.tabIndex = -1;
            view.style.width = Mathf.Max(MinPopupWidth, worldBound.width);

            list = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            list.AddToClassList("vn-dd-popup__list");
            list.focusable = false;
            view.Add(list);

            items = new VisualElement[choices.Length];
            for (int i = 0; i < choices.Length; i++)
            {
                VisualElement item = new VisualElement();
                item.AddToClassList(ItemClass);
                item.AddToClassList(StaggerClasses[Math.Min(i, StaggerClasses.Length - 1)]);
                item.EnableInClassList(ItemSelectedClass, i == value);
                item.userData = i;
                if (tooltips != null && i < tooltips.Length && !string.IsNullOrEmpty(tooltips[i]))
                {
                    item.tooltip = tooltips[i];
                }

                if (icons != null && i < icons.Length && !string.IsNullOrEmpty(icons[i]))
                {
                    item.Add(VeneerStyle.IconElement(icons[i], null, brand));
                }

                var text = new Label(choices[i]);
                text.AddToClassList("vn-dd-item__label");
                text.pickingMode = PickingMode.Ignore;
                item.Add(text);
                item.Add(VeneerStyle.IconElement("check", "vn-dd-item__check", brand));
                item.RegisterCallback(itemEnter);
                item.RegisterCallback(itemClick);
                items[i] = item;
                list.Add(item);
            }

            // Start state of the animation, so the first frame of the popup is already the "pulled up" shape (and invisible).
            float motion = VeneerMotionKit.Scale(this);
            view.style.opacity = 0f;
            view.style.transformOrigin = new TransformOrigin(Length.Percent(50f), Length.Percent(0f));
            if (motion > 0f)
            {
                float startY = Mathf.Clamp(1f - (1f - StartScaleY) * motion, 0.2f, 0.95f);
                view.style.scale = new Scale(new Vector3(1f, startY, 1f));
                SetTransitions(
                    view,
                    OpenFadeMilliseconds * motion,
                    new EasingFunction(EasingMode.EaseOut),
                    OpenMilliseconds * motion,
                    new EasingFunction(EasingMode.EaseOutElastic));
            }

            view.RegisterCallback(keyDown, TrickleDown.TrickleDown);
            view.RegisterCallback(popupFocusOut);
            view.RegisterCallback(popupGeometry);
            popup = view;
            SetActive(value >= 0 ? value : 0, false);
        }

        // Runs once, when the popup has its first layout: the real height is known, so it can be put below (or above) the button.
        private void OnPopupGeometry(GeometryChangedEvent evt)
        {
            if (placed || popup == null || evt.target != popup || evt.newRect.height <= 0f)
            {
                return;
            }

            placed = true;
            popup.UnregisterCallback(popupGeometry);
            Place(evt.newRect.height);
            popup.schedule.Execute(beginOpen);
        }

        private void Place(float height)
        {
            Rect anchor = worldBound;
            Vector2 local = panelRoot.WorldToLocal(anchor.position);
            float panelWidth = panelRoot.layout.width;
            float panelHeight = panelRoot.layout.height;

            float below = panelHeight - (local.y + anchor.height) - EdgeGap - 2f;
            float above = local.y - EdgeGap - 2f;
            openUp = height > below && above > below;
            float room = Mathf.Max(openUp ? above : below, MinPopupHeight);
            if (height > room)
            {
                height = room;
                popup.style.height = height;
            }

            float width = Mathf.Min(popup.resolvedStyle.width, Mathf.Max(panelWidth - EdgeGap * 2f, NarrowestPopupWidth));
            float left = Mathf.Clamp(local.x, EdgeGap, Mathf.Max(EdgeGap, panelWidth - width - EdgeGap));
            float top = openUp ? local.y - height - 2f : local.y + anchor.height + 2f;
            top = Mathf.Clamp(top, EdgeGap, Mathf.Max(EdgeGap, panelHeight - height - EdgeGap));

            popup.style.width = width;
            popup.style.left = left;
            popup.style.top = top;
            popup.style.transformOrigin = new TransformOrigin(Length.Percent(50f), Length.Percent(openUp ? 100f : 0f));
        }

        // The next tick after Place: the start values are resolved by now, so changing them runs the transitions.
        private void BeginOpen()
        {
            if (!isOpen || popup == null)
            {
                return;
            }

            popup.style.opacity = 1f;
            popup.style.scale = new Scale(Vector3.one);
            popup.AddToClassList(PopupOpenClass);
            ScrollToActive(true);
            popup.Focus();
        }

        // ------------------------------------------------------------------ closing

        private void Close(bool refocus)
        {
            if (!isOpen)
            {
                return;
            }

            isOpen = false;
            VisualElement leaving = popup;
            ReleaseOpenState();
            if (refocus)
            {
                Focus();
            }

            if (leaving == null)
            {
                return;
            }

            float motion = VeneerMotionKit.Scale(this);
            if (motion <= 0f || leaving.parent == null)
            {
                leaving.RemoveFromHierarchy();
                return;
            }

            // A quick ease-in: the list sags a little and fades out, then it is removed.
            var easeIn = new EasingFunction(EasingMode.EaseIn);
            SetTransitions(leaving, CloseMilliseconds * motion, easeIn, CloseMilliseconds * motion, easeIn);
            leaving.style.opacity = 0f;
            leaving.style.scale = new Scale(new Vector3(1f, CloseScaleY, 1f));
            closingPopup = leaving;
            closeTimer = leaving.schedule.Execute(finishClosing);
            closeTimer.StartingIn((long)(CloseMilliseconds * motion) + 30L);
        }

        /// <summary>Closes the list now, without the animation, and forgets any list that is still fading out.</summary>
        internal void CloseImmediate()
        {
            isOpen = false;
            VisualElement leaving = popup;
            ReleaseOpenState();
            if (leaving != null)
            {
                leaving.RemoveFromHierarchy();
            }

            FinishClosing();
        }

        private void ReleaseOpenState()
        {
            if (openOwner == this)
            {
                openOwner = null;
            }

            EnableInClassList(OpenClass, false);
            EditorApplication.focusChanged -= OnApplicationFocusChanged;
            if (panelRoot != null)
            {
                panelRoot.UnregisterCallback(panelPointerDown, TrickleDown.TrickleDown);
                panelRoot.UnregisterCallback(panelWheel, TrickleDown.TrickleDown);
                panelRoot.UnregisterCallback(panelGeometry);
                panelRoot = null;
            }

            popup = null;
            list = null;
            items = null;
            active = -1;
        }

        private void FinishClosing()
        {
            if (closeTimer != null)
            {
                closeTimer.Pause();
                closeTimer = null;
            }

            if (closingPopup != null)
            {
                closingPopup.RemoveFromHierarchy();
                closingPopup = null;
            }
        }

        private static void CloseOpenForReload()
        {
            if (openOwner != null)
            {
                openOwner.CloseImmediate();
            }
        }

        private static void OnApplicationFocusChanged(bool focused)
        {
            if (!focused && openOwner != null)
            {
                openOwner.Close(false);
            }
        }

        private void OnPanelPointerDown(PointerDownEvent evt)
        {
            if (!isOpen)
            {
                return;
            }

            // A press on the button is its own click (it toggles); a press in the list is a choice. Anything else closes the list.
            if (evt.target is VisualElement target && (IsInPopup(target) || target == this || Contains(target)))
            {
                return;
            }

            Close(false);
        }

        private void OnPanelWheel(WheelEvent evt)
        {
            // Scrolling the Inspector behind the list would leave the list hanging in the wrong place.
            if (isOpen && !(evt.target is VisualElement target && IsInPopup(target)))
            {
                Close(false);
            }
        }

        private void OnPanelGeometry(GeometryChangedEvent evt)
        {
            if (isOpen && evt.oldRect.width > 0f && evt.oldRect.size != evt.newRect.size)
            {
                Close(false);
            }
        }

        private void OnPopupFocusOut(FocusOutEvent evt)
        {
            if (!isOpen)
            {
                return;
            }

            if (evt.relatedTarget is VisualElement next && (IsInPopup(next) || next == this))
            {
                return;
            }

            Close(false);
        }

        private bool IsInPopup(VisualElement target)
        {
            return popup != null && (target == popup || popup.Contains(target));
        }

        // ------------------------------------------------------------------ items and keys

        private void OnItemEnter(PointerEnterEvent evt)
        {
            if (isOpen && evt.currentTarget is VisualElement item && item.userData is int index)
            {
                SetActive(index, false);
            }
        }

        private void OnItemClick(ClickEvent evt)
        {
            if (isOpen && evt.currentTarget is VisualElement item && item.userData is int index)
            {
                Select(index);
                evt.StopPropagation();
            }
        }

        private void Select(int index)
        {
            if (index < 0 || index >= choices.Length)
            {
                Close(true);
                return;
            }

            bool different = index != value;
            Close(true);
            if (!different)
            {
                return;
            }

            value = index;
            UpdateDisplay();
            Action<int> handler = Changed;
            if (handler != null)
            {
                handler(index);
            }
        }

        private void SetActive(int index, bool scroll)
        {
            if (items == null || items.Length == 0)
            {
                return;
            }

            index = Mathf.Clamp(index, 0, items.Length - 1);
            if (active >= 0 && active < items.Length)
            {
                items[active].RemoveFromClassList(ItemActiveClass);
            }

            active = index;
            items[active].AddToClassList(ItemActiveClass);
            if (scroll)
            {
                ScrollToActive(false);
            }
        }

        // Works from layout numbers (not worldBound), because the list is scaled while it opens.
        private void ScrollToActive(bool center)
        {
            if (list == null || items == null || active < 0 || active >= items.Length)
            {
                return;
            }

            VisualElement item = items[active];
            float viewHeight = list.contentViewport.layout.height;
            float contentHeight = list.contentContainer.layout.height;
            if (viewHeight <= 0f || contentHeight <= viewHeight)
            {
                return;
            }

            float y = item.layout.y;
            float height = item.layout.height;
            float offset = list.scrollOffset.y;
            float target;
            if (center)
            {
                target = y - (viewHeight - height) * 0.5f;
            }
            else if (y < offset)
            {
                target = y;
            }
            else if (y + height > offset + viewHeight)
            {
                target = y + height - viewHeight;
            }
            else
            {
                return;
            }

            list.scrollOffset = new Vector2(0f, Mathf.Clamp(target, 0f, contentHeight - viewHeight));
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (isOpen)
            {
                HandleOpenKey(evt);
                return;
            }

            if (evt.target != this)
            {
                return;
            }

            switch (evt.keyCode)
            {
                case KeyCode.Space:
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.DownArrow:
                    Open();
                    evt.StopPropagation();
                    break;
            }
        }

        private void HandleOpenKey(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                    SetActive(active + 1, true);
                    break;
                case KeyCode.UpArrow:
                    SetActive(active - 1, true);
                    break;
                case KeyCode.PageDown:
                    SetActive(active + PageSize, true);
                    break;
                case KeyCode.PageUp:
                    SetActive(active - PageSize, true);
                    break;
                case KeyCode.Home:
                    SetActive(0, true);
                    break;
                case KeyCode.End:
                    SetActive(choices.Length - 1, true);
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.Space:
                    Select(active);
                    break;
                case KeyCode.Escape:
                case KeyCode.Tab:
                    Close(true);
                    break;
                default:
                    if (!JumpToLetter(evt.character))
                    {
                        return;
                    }

                    break;
            }

            evt.StopPropagation();
        }

        // Type-ahead: a letter or digit moves to the next choice that starts with it.
        private bool JumpToLetter(char letter)
        {
            if (!char.IsLetterOrDigit(letter))
            {
                return false;
            }

            int count = choices.Length;
            for (int step = 1; step <= count; step++)
            {
                int index = (active + step + count) % count;
                string text = choices[index];
                if (text.Length > 0 && char.ToUpperInvariant(text[0]) == char.ToUpperInvariant(letter))
                {
                    SetActive(index, true);
                    break;
                }
            }

            return true;
        }

        // ------------------------------------------------------------------ helpers

        private static void SetTransitions(
            VisualElement element, float fadeMilliseconds, EasingFunction fadeEasing, float scaleMilliseconds, EasingFunction scaleEasing)
        {
            element.style.transitionProperty = new List<StylePropertyName>(2)
            {
                new StylePropertyName("opacity"), new StylePropertyName("scale"),
            };
            element.style.transitionDuration = new List<TimeValue>(2)
            {
                new TimeValue(fadeMilliseconds, TimeUnit.Millisecond), new TimeValue(scaleMilliseconds, TimeUnit.Millisecond),
            };
            element.style.transitionTimingFunction = new List<EasingFunction>(2) { fadeEasing, scaleEasing };
            element.style.transitionDelay = new List<TimeValue>(1) { new TimeValue(0f, TimeUnit.Millisecond) };
        }

        private static string[] Copy(IReadOnlyList<string> source, bool allowNull)
        {
            if (source == null)
            {
                return allowNull ? null : Array.Empty<string>();
            }

            var copy = new string[source.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = source[i] ?? string.Empty;
            }

            return copy;
        }
    }
}
