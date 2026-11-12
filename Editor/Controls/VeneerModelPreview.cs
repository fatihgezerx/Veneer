using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// A 3D preview of a model for Veneer editors, drawn as a UI Toolkit element: a picture in the kit's card look with an overlay toolbar.
    /// A drag with the left or right button turns the model, the middle button pans, the wheel zooms towards the pointer; the toolbar has
    /// zoom in, zoom out and reset buttons and a segmented group of named views (the source's <see cref="IVeneerModelPreviewSource.Focuses"/>)
    /// that the camera glides to (following the kit's Motion setting). Arrow keys turn (with Shift they pan, Up and Down zoom), plus and
    /// minus zoom, Home resets, while the element has focus. What is shown comes from an <see cref="IVeneerModelPreviewSource"/>; the control
    /// knows nothing about the model type. Nothing runs while nothing changes: a scheduled tick exists only during a drag, a glide or until
    /// the next picture is drawn, and pauses itself. The picture is drawn at the element's size times the panel's pixel density, inside
    /// an IMGUI repaint (<c>PreviewRenderUtility</c> needs one). The caller disposes the source; the control never does.
    /// </summary>
    public class VeneerModelPreview : VisualElement
    {
        private const string SheetRelativePath = "/Styles/VeneerModelPreview.uss";
        private const string RootClass = "vn-root";
        private const string LightClass = "vn-root--light";
        private const long TickMilliseconds = 16L;
        private const float MinSize = 4f;

        private readonly VeneerModelPreviewSettings settings;
        private readonly VeneerBrand brand;
        private readonly VeneerModelPreviewOrbit orbit;
        private readonly Image image;
        private readonly Label hint;
        private readonly IMGUIContainer host;
        private readonly VisualElement bar;
        private readonly Action<int> focusClicked;
        private readonly Action zoomInAction;
        private readonly Action zoomOutAction;
        private readonly Action resetAction;
        private readonly Action tickAction;

        private IVeneerModelPreviewSource source;
        private IReadOnlyList<VeneerModelFocus> builtFocuses;
        private int builtFocusCount = -1;
        private VisualElement focusGroup;
        private int focusIndex = -1;
        private IVisualElementScheduledItem loop;

        private bool contentDirty = true;
        private bool renderDirty = true;
        private bool modelSeen;
        private bool madeRoot;
        private VeneerModelCamera cameraData;
        private Texture shown;
        private string shownHint;
        private bool hintVisible;

        private int dragButton = -1;
        private Vector2 lastPointer;

        /// <summary>Makes a preview of <paramref name="source"/> (may be null and set later with <see cref="SetSource"/>).</summary>
        /// <param name="source">What to show; the caller keeps ownership.</param>
        /// <param name="settings">The tuning; null uses the defaults.</param>
        /// <param name="brand">Brand for icon lookup and for the look when the element is outside a Veneer tree; may be null.</param>
        public VeneerModelPreview(IVeneerModelPreviewSource source = null, VeneerModelPreviewSettings settings = null, VeneerBrand brand = null)
        {
            this.settings = settings ?? new VeneerModelPreviewSettings();
            this.brand = brand;
            orbit = new VeneerModelPreviewOrbit(this.settings);
            focusClicked = OnFocusClicked;
            zoomInAction = ZoomIn;
            zoomOutAction = ZoomOut;
            resetAction = OnResetClicked;
            tickAction = Tick;

            AddToClassList("vn-mp");
            focusable = true;
            tabIndex = 0;

            image = new Image { scaleMode = ScaleMode.StretchToFill };
            image.AddToClassList("vn-mp__image");
            image.uv = this.settings.FlipVertical ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
            Add(image);

            hint = new Label();
            hint.AddToClassList("vn-mp__hint");
            hint.pickingMode = PickingMode.Ignore;
            VeneerStyle.SetDisplay(hint, false);
            Add(hint);

            // The picture is drawn from an IMGUI repaint, as PreviewRenderUtility expects; the element itself is one pixel and draws nothing.
            host = new IMGUIContainer(OnHostGui);
            host.AddToClassList("vn-mp__host");
            host.pickingMode = PickingMode.Ignore;
            Add(host);

            bar = new VisualElement();
            bar.AddToClassList("vn-mp__bar");
            Add(bar);
            var tools = new VisualElement();
            tools.AddToClassList("vn-mp__tools");
            tools.Add(VeneerStyle.IconButton("zoom-in", "Zoom in (+)", zoomInAction, brand));
            tools.Add(VeneerStyle.IconButton("zoom-out", "Zoom out (-)", zoomOutAction, brand));
            tools.Add(VeneerStyle.IconButton("fit", "Reset the view: turn and zoom back to the start (Home)", resetAction, brand));
            var spacer = VeneerStyle.Spacer();
            bar.Add(spacer);
            bar.Add(tools);

            image.RegisterCallback<PointerDownEvent>(OnPointerDown);
            image.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            image.RegisterCallback<PointerUpEvent>(OnPointerUp);
            image.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            image.RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<GeometryChangedEvent>(OnGeometry);
            RegisterCallback<AttachToPanelEvent>(OnAttach);
            RegisterCallback<DetachFromPanelEvent>(OnDetach);

            SetSource(source);
        }

        /// <summary>The style sheet of the control (<c>Styles/VeneerModelPreview.uss</c>); null while the kit folder or the file cannot be found yet.</summary>
        public static StyleSheet Sheet
        {
            get
            {
                string kit = VeneerStyle.KitFolder;
                return kit == null ? null : VeneerStyle.LoadSheet(kit + SheetRelativePath);
            }
        }

        /// <summary>What the control shows.</summary>
        public IVeneerModelPreviewSource Source => source;

        /// <summary>The index of the view the toolbar shows as selected, or -1 after the user moved the view by hand.</summary>
        public int FocusIndex => focusIndex;

        /// <summary>Shows another source (or none). The old one is not disposed. The view starts over on the first model it shows.</summary>
        public void SetSource(IVeneerModelPreviewSource value)
        {
            source = value;
            contentDirty = true;
            renderDirty = true;
            modelSeen = false;
            RebuildFocusBar();
            Wake();
        }

        /// <summary>Tells the control the model changed: the source is asked to <see cref="IVeneerModelPreviewSource.Sync"/> and the picture is redrawn.</summary>
        public void Refresh()
        {
            contentDirty = true;
            renderDirty = true;
            Wake();
        }

        /// <summary>Glides (or, with <paramref name="animate"/> false, jumps) to the view <paramref name="index"/> of the source's focus list.</summary>
        public void FocusView(int index, bool animate = true)
        {
            if (source == null || index < 0 || index >= source.Focuses.Count)
            {
                return;
            }

            VeneerModelFocus focus = source.Focuses[index];
            float seconds = animate ? settings.FocusSeconds * VeneerMotionKit.Scale(this) : 0f;
            orbit.MoveTo(focus, EditorApplication.timeSinceStartup, seconds);
            SelectFocus(index);
            Changed();
        }

        /// <summary>Zooms in by one step (towards the middle of the picture).</summary>
        public void ZoomIn()
        {
            ZoomBy(1f / settings.ButtonStep, Vector2.zero);
        }

        /// <summary>Zooms out by one step.</summary>
        public void ZoomOut()
        {
            ZoomBy(settings.ButtonStep, Vector2.zero);
        }

        /// <summary>Turns the model back to the start and shows the first view (the whole model when the source has none).</summary>
        public void ResetView()
        {
            bool has = source != null && source.Focuses.Count > 0;
            VeneerModelFocus first = has ? source.Focuses[0] : default;
            orbit.Reset(has, first);
            SelectFocus(has ? 0 : -1);
            Changed();
        }

        // ------------------------------------------------------------------ look

        private void OnAttach(AttachToPanelEvent evt)
        {
            VisualElement kitRoot = FindKitRoot();
            if (kitRoot == null && !madeRoot)
            {
                // Outside a Veneer tree the element carries the palette itself.
                madeRoot = true;
                AddToClassList(RootClass);
                if (!EditorGUIUtility.isProSkin)
                {
                    AddToClassList(LightClass);
                }

                styleSheets.Add(VeneerStyle.Sheet);
                VeneerStyle.ApplyBrand(this, brand);
            }

            StyleSheet sheet = Sheet;
            VisualElement target = kitRoot ?? this;
            if (sheet != null && !target.styleSheets.Contains(sheet))
            {
                target.styleSheets.Add(sheet);
            }

            contentDirty = true;
            renderDirty = true;
            Wake();
        }

        private void OnDetach(DetachFromPanelEvent evt)
        {
            if (loop != null)
            {
                loop.Pause();
            }

            // The texture belongs to the source's utility, which the caller may dispose right now.
            image.image = null;
            shown = null;
            dragButton = -1;
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

        // ------------------------------------------------------------------ the toolbar

        private void RebuildFocusBar()
        {
            if (focusGroup != null)
            {
                focusGroup.RemoveFromHierarchy();
                focusGroup = null;
            }

            builtFocuses = null;
            builtFocusCount = -1;
            focusIndex = -1;
            if (source == null)
            {
                return;
            }

            IReadOnlyList<VeneerModelFocus> list = source.Focuses;
            builtFocuses = list;
            builtFocusCount = list.Count;
            if (list.Count == 0)
            {
                return;
            }

            var icons = new string[list.Count];
            var names = new string[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                icons[i] = list[i].Icon;
                names[i] = list[i].Name;
            }

            focusGroup = VeneerStyle.SegmentedIcons(icons, names, -1, focusClicked, brand);
            for (int i = 0; i < list.Count; i++)
            {
                focusGroup[i].tooltip = string.IsNullOrEmpty(list[i].Tooltip) ? list[i].Name : list[i].Tooltip;
            }

            bar.Insert(0, focusGroup);
        }

        private void SelectFocus(int index)
        {
            focusIndex = index;
            if (focusGroup != null)
            {
                VeneerStyle.SetSegmentedSelected(focusGroup, index);
            }
        }

        private void OnFocusClicked(int index)
        {
            FocusView(index, true);
        }

        private void OnResetClicked()
        {
            ResetView();
        }

        // ------------------------------------------------------------------ input

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button > 2 || dragButton >= 0)
            {
                return;
            }

            dragButton = evt.button;
            lastPointer = evt.localPosition;
            image.CapturePointer(evt.pointerId);
            Focus();
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (dragButton < 0 || !image.HasPointerCapture(evt.pointerId))
            {
                return;
            }

            Vector2 position = evt.localPosition;
            Vector2 delta = position - lastPointer;
            lastPointer = position;
            if (dragButton == 2)
            {
                orbit.Pan2D(delta, image.layout.height);
                SelectFocus(-1);
            }
            else
            {
                orbit.Rotate(delta.x);
            }

            evt.StopPropagation();
            Changed();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (dragButton != evt.button)
            {
                return;
            }

            dragButton = -1;
            image.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnCaptureOut(PointerCaptureOutEvent evt)
        {
            dragButton = -1;
        }

        private void OnWheel(WheelEvent evt)
        {
            Rect rect = image.layout;
            if (rect.width < 1f || rect.height < 1f)
            {
                return;
            }

            // The point under the pointer stays under it: the view zooms towards where the mouse is.
            Vector2 local = evt.localMousePosition;
            var ndc = new Vector2((local.x - rect.width * 0.5f) / (rect.width * 0.5f), -(local.y - rect.height * 0.5f) / (rect.height * 0.5f));
            ZoomBy(Mathf.Pow(settings.WheelStep, evt.delta.y), ndc);
            evt.StopPropagation();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            float height = Mathf.Max(image.layout.height, 1f);
            float step = height * settings.KeyPanFraction;
            switch (evt.keyCode)
            {
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                    float sign = evt.keyCode == KeyCode.LeftArrow ? -1f : 1f;
                    if (evt.shiftKey)
                    {
                        orbit.Pan2D(new Vector2(-sign * step, 0f), height);
                        SelectFocus(-1);
                    }
                    else
                    {
                        orbit.RotateDegrees(sign * settings.KeyTurnDegrees);
                    }

                    Changed();
                    break;
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                    if (evt.shiftKey)
                    {
                        orbit.Pan2D(new Vector2(0f, evt.keyCode == KeyCode.UpArrow ? step : -step), height);
                        SelectFocus(-1);
                        Changed();
                    }
                    else if (evt.keyCode == KeyCode.UpArrow)
                    {
                        ZoomIn();
                    }
                    else
                    {
                        ZoomOut();
                    }

                    break;
                case KeyCode.Plus:
                case KeyCode.KeypadPlus:
                case KeyCode.Equals:
                    ZoomIn();
                    break;
                case KeyCode.Minus:
                case KeyCode.KeypadMinus:
                    ZoomOut();
                    break;
                case KeyCode.Home:
                    ResetView();
                    break;
                default:
                    return;
            }

            evt.StopPropagation();
        }

        private void ZoomBy(float factor, Vector2 ndc)
        {
            Rect rect = image.layout;
            float aspect = rect.height > 0f ? rect.width / rect.height : 1f;
            orbit.ZoomBy(factor, ndc, aspect);
            SelectFocus(-1);
            Changed();
        }

        // ------------------------------------------------------------------ drawing

        private void OnGeometry(GeometryChangedEvent evt)
        {
            if (evt.newRect.size != evt.oldRect.size)
            {
                renderDirty = true;
                Wake();
            }
        }

        // Something about the view changed: a picture is due.
        private void Changed()
        {
            renderDirty = true;
            Wake();
        }

        // Starts the tick if it is not running. It runs only until the picture is drawn and no glide is left.
        private void Wake()
        {
            if (panel == null)
            {
                return;
            }

            if (loop == null)
            {
                loop = schedule.Execute(tickAction).Every(TickMilliseconds);
            }
            else
            {
                loop.Resume();
            }
        }

        private void Tick()
        {
            bool gliding = false;
            if (orbit.IsMoving)
            {
                gliding = orbit.Step(EditorApplication.timeSinceStartup);
                renderDirty = true;
            }

            // Nothing to draw into while the element is hidden or has no size: wait for the next geometry change.
            if (layout.width < MinSize || layout.height < MinSize)
            {
                loop.Pause();
                return;
            }

            if (renderDirty || contentDirty)
            {
                host.MarkDirtyRepaint();
            }
            else if (!gliding)
            {
                loop.Pause();
            }
        }

        private void OnHostGui()
        {
            if (Event.current.type == EventType.Repaint)
            {
                Draw();
            }
        }

        private void Draw()
        {
            if (source == null)
            {
                ShowHint(null);
                renderDirty = false;
                return;
            }

            if (contentDirty)
            {
                contentDirty = false;
                bool changed = source.Sync();
                if (builtFocuses != source.Focuses || builtFocusCount != source.Focuses.Count)
                {
                    RebuildFocusBar();
                }

                if (source.HasModel && (changed || !modelSeen))
                {
                    orbit.SetBounds(source.Bounds);
                    if (!modelSeen)
                    {
                        modelSeen = true;
                        ResetView();
                    }
                }
            }

            if (!source.HasModel)
            {
                if (shown != null)
                {
                    image.image = null;
                    shown = null;
                }

                ShowHint(source.EmptyHint);
                renderDirty = false;
                return;
            }

            if (layout.width < MinSize || layout.height < MinSize)
            {
                return;
            }

            ShowHint(null);
            orbit.GetCamera(ref cameraData);

            // The utility multiplies by the editor's pixels per point; this panel may sit on a screen with another density.
            float density = panel != null ? panel.scaledPixelsPerPoint / EditorGUIUtility.pixelsPerPoint : 1f;
            Texture texture = source.Render(new Vector2(layout.width * density, layout.height * density), cameraData);
            renderDirty = false;
            if (!ReferenceEquals(texture, shown))
            {
                shown = texture;
                image.image = texture;
            }

            image.MarkDirtyRepaint();
        }

        private void ShowHint(string text)
        {
            bool visible = !string.IsNullOrEmpty(text);
            if (visible && !string.Equals(shownHint, text, StringComparison.Ordinal))
            {
                shownHint = text;
                hint.text = text;
            }

            if (visible != hintVisible)
            {
                hintVisible = visible;
                VeneerStyle.SetDisplay(hint, visible);
            }
        }
    }
}
