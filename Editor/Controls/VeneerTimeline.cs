using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// A horizontal time bar made of lanes. A lane holds coloured spans (start, end, label) and grips that can be dragged along the
    /// time axis; a ruler on top and a playhead across all lanes make it a scrubbable timeline. It knows nothing about what the spans
    /// mean: the host adds the lanes, spans and handles once, then pushes the current times with <see cref="SetSpan"/> and
    /// <see cref="SetHandle"/> and writes the data in the <see cref="HandleMoved"/> callback.
    /// </summary>
    /// <remarks>
    /// Mouse: drag a grip (pointer capture). Without a modifier the time snaps to <see cref="SnapStep"/> and sticks to other grips and the
    /// playhead that are within a few pixels; Shift snaps to whole animation frames (<see cref="FrameDuration"/>, else ten steps); Ctrl
    /// (Cmd on a Mac) turns snapping off (millisecond precision); Escape while dragging puts the grip back. A click or drag on the ruler or on
    /// an empty part of a lane moves the playhead when <see cref="PlayheadEnabled"/> is on.
    /// Keyboard (the bar is focusable): Left / Right move the selected grip by <see cref="SnapStep"/> (Shift: one frame, else ten steps; Ctrl:
    /// a thousandth of a second), Up / Down or PageUp / PageDown select the previous / next grip (the playhead is the last one), Home / End the
    /// first / last. Navigation events (a gamepad's d-pad) do the same as the arrow keys.
    /// A small bubble with the grip's name, its time in seconds and, when <see cref="FrameDuration"/> is set, the frame number fades in above the
    /// bar while the pointer is on a grip, while it is dragged and for a moment after a key press; its fade follows the kit's Motion setting.
    /// </remarks>
    public sealed class VeneerTimeline : VisualElement
    {
        /// <summary>The default for <see cref="SnapStep"/>: a hundredth of a second.</summary>
        public const float DefaultSnapStep = 0.01f;

        /// <summary>The step of a nudge or a drag while Ctrl is held: a thousandth of a second.</summary>
        public const float FineStep = 0.001f;

        private const string SheetRelativePath = "/Styles/VeneerTimeline.uss";
        private const float SidePadding = 8f;
        private const float MagnetPixels = 6f;
        private const float RulerHeight = 16f;
        private const long KeyBubbleMilliseconds = 900;

        private readonly List<LaneItem> lanes = new List<LaneItem>();
        private readonly List<SpanItem> spans = new List<SpanItem>();
        private readonly List<HandleItem> handles = new List<HandleItem>();

        private readonly VisualElement inner;
        private readonly VisualElement ruler;
        private readonly VisualElement body;
        private readonly VisualElement playheadLine;
        private readonly VisualElement playheadHead;
        private readonly VisualElement bubble;
        private readonly Label bubbleName;
        private readonly Label bubbleTime;
        private readonly Label bubbleFrame;
        private readonly VeneerNumberText timeText = new VeneerNumberText(string.Empty, " s", "0.00#", 1000f);
        private readonly VeneerNumberText frameText = new VeneerNumberText("frame ", string.Empty, "0", 1f);
        private readonly Action hideBubbleLater;

        private float span = 1f;
        private float snapStep = DefaultSnapStep;
        private float frameDuration;
        private float displayScale = 1f;
        private bool showRuler = true;
        private bool playheadEnabled;
        private float playhead = float.NaN;

        // selected is an index into the handles; handles.Count means the playhead.
        private int selected;
        private int hovered = -1;
        private int dragging = -1;
        private int bubbleFor = -1;
        private bool scrubbing;
        private int pointerId = -1;
        private float grabOffset;
        private float dragStartTime;
        private VisualElement captureElement;
        private IVisualElementScheduledItem bubbleTimer;

        /// <summary>Creates an empty timeline: add lanes, spans and handles, then set <see cref="Span"/>.</summary>
        public VeneerTimeline()
        {
            AddToClassList("vn-tl");
            focusable = true;
            tabIndex = 0;

            inner = new VisualElement();
            inner.AddToClassList("vn-tl__inner");
            inner.style.paddingLeft = SidePadding;
            inner.style.paddingRight = SidePadding;
            Add(inner);

            ruler = new VisualElement();
            ruler.AddToClassList("vn-tl__ruler");
            ruler.style.height = RulerHeight;
            ruler.generateVisualContent += DrawRuler;
            ruler.RegisterCallback<GeometryChangedEvent>(OnRulerGeometry);
            playheadHead = new VisualElement { pickingMode = PickingMode.Ignore };
            playheadHead.AddToClassList("vn-tl__playhead-head");
            playheadHead.style.display = DisplayStyle.None;
            ruler.Add(playheadHead);
            inner.Add(ruler);

            body = new VisualElement();
            body.AddToClassList("vn-tl__body");
            playheadLine = new VisualElement { pickingMode = PickingMode.Ignore };
            playheadLine.AddToClassList("vn-tl__playhead");
            playheadLine.style.display = DisplayStyle.None;
            body.Add(playheadLine);
            inner.Add(body);

            bubble = new VisualElement { pickingMode = PickingMode.Ignore };
            bubble.AddToClassList("vn-tl__bubble");
            bubbleName = new Label { pickingMode = PickingMode.Ignore };
            bubbleName.AddToClassList("vn-tl__bubble-name");
            bubbleTime = new Label { pickingMode = PickingMode.Ignore };
            bubbleTime.AddToClassList("vn-tl__bubble-time");
            bubbleFrame = new Label { pickingMode = PickingMode.Ignore };
            bubbleFrame.AddToClassList("vn-tl__bubble-frame");
            bubble.Add(bubbleName);
            bubble.Add(bubbleTime);
            bubble.Add(bubbleFrame);
            Add(bubble);

            hideBubbleLater = HideBubbleIfIdle;
            RegisterCallback<AttachToPanelEvent>(OnAttach);
            RegisterCallback<DetachFromPanelEvent>(OnDetach);
            RegisterCallback<PointerDownEvent>(OnBackgroundDown);
            RegisterCallback<PointerMoveEvent>(OnBackgroundMove);
            RegisterCallback<PointerUpEvent>(OnBackgroundUp);
            RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<NavigationMoveEvent>(OnNavigationMove);
            RegisterCallback<FocusOutEvent>(OnFocusOut);
        }

        /// <summary>Raised while a grip is dragged or nudged: its index (from <see cref="AddHandle"/>) and the new time, already snapped and clamped to its range. The host writes the data and calls <see cref="SetHandle"/> again.</summary>
        public event Action<int, float> HandleMoved;

        /// <summary>Raised when a drag begins (the host can open an Undo group).</summary>
        public event Action<int> DragStarted;

        /// <summary>Raised when a drag ends or is cancelled (the host can close its Undo group).</summary>
        public event Action<int> DragEnded;

        /// <summary>Raised when the user moves the playhead (a click or drag on the ruler or a lane, or a key press while it is selected): the new time in seconds.</summary>
        public event Action<float> PlayheadMoved;

        /// <summary>The style sheet of the timeline (<c>Styles/VeneerTimeline.uss</c>); null while the kit folder or the file cannot be found yet.</summary>
        public static StyleSheet Sheet
        {
            get
            {
                string kit = VeneerStyle.KitFolder;
                return kit == null ? null : VeneerStyle.LoadSheet(kit + SheetRelativePath);
            }
        }

        /// <summary>The seconds shown across the width of the bar (0 to Span). Set it before the times; changing it moves everything.</summary>
        public float Span
        {
            get => span;
            set
            {
                float next = Mathf.Max(value, 0.001f);
                if (Mathf.Approximately(next, span))
                {
                    return;
                }

                span = next;
                LayoutAll();
            }
        }

        /// <summary>The snap grid of a drag in seconds (default 0.01).</summary>
        public float SnapStep
        {
            get => snapStep;
            set => snapStep = Mathf.Max(value, FineStep);
        }

        /// <summary>Seconds of the timeline that one frame of the animation lasts; 0 = unknown. With it the bubble shows the frame number and Shift snaps to frames.</summary>
        public float FrameDuration
        {
            get => frameDuration;
            set => frameDuration = Mathf.Max(value, 0f);
        }

        /// <summary>
        /// How many timeline seconds make one second in the bubble (default 1). A host whose axis is not the time it shows - the timeline
        /// is the clip's time, the bubble tells the attack's seconds - sets this; the frame number still counts timeline seconds.
        /// </summary>
        public float DisplayScale
        {
            get => displayScale;
            set => displayScale = Mathf.Max(value, 0.001f);
        }

        /// <summary>Whether the ruler (tick marks) is drawn. The strip stays so the bubble has room.</summary>
        public bool ShowRuler
        {
            get => showRuler;
            set
            {
                showRuler = value;
                ruler.MarkDirtyRepaint();
            }
        }

        /// <summary>Whether the playhead is shown and the ruler and the lanes move it.</summary>
        public bool PlayheadEnabled
        {
            get => playheadEnabled;
            set
            {
                if (playheadEnabled == value)
                {
                    return;
                }

                playheadEnabled = value;
                playheadLine.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                playheadHead.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                if (!value && selected == handles.Count)
                {
                    selected = 0;
                }

                LayoutPlayhead();
            }
        }

        /// <summary>The playhead in seconds (it is moved by <see cref="SetPlayhead"/>).</summary>
        public float Playhead => playhead;

        /// <summary>True while a grip or the playhead is being dragged.</summary>
        public bool IsDragging => dragging >= 0 || scrubbing;

        /// <summary>Adds a lane (a horizontal strip) below the ones already there and returns its index.</summary>
        /// <param name="height">Height in pixels.</param>
        /// <param name="tooltip">Tooltip of the empty part of the lane; may be null.</param>
        /// <param name="title">A small muted word at the left end of the lane (behind its spans); may be null.</param>
        public int AddLane(float height, string tooltip = null, string title = null)
        {
            var element = new VisualElement();
            element.AddToClassList("vn-tl__lane");
            element.style.height = height;
            element.tooltip = tooltip;

            if (!string.IsNullOrEmpty(title))
            {
                var text = new Label(title) { pickingMode = PickingMode.Ignore };
                text.AddToClassList("vn-tl__lane-title");
                element.Add(text);
            }

            var spanHost = new VisualElement { pickingMode = PickingMode.Ignore };
            spanHost.AddToClassList("vn-tl__layer");
            // The thin border is its own element, so it does not change the width the times are measured over.
            var frame = new VisualElement { pickingMode = PickingMode.Ignore };
            frame.AddToClassList("vn-tl__layer");
            frame.AddToClassList("vn-tl__frame");
            var handleHost = new VisualElement { pickingMode = PickingMode.Ignore };
            handleHost.AddToClassList("vn-tl__layer");
            element.Add(spanHost);
            element.Add(frame);
            element.Add(handleHost);
            body.Insert(lanes.Count, element);
            lanes.Add(new LaneItem { Element = element, SpanHost = spanHost, HandleHost = handleHost });
            return lanes.Count - 1;
        }

        /// <summary>Adds a span with a fixed fill colour and returns its index; give it times with <see cref="SetSpan"/>.</summary>
        /// <param name="lane">Lane index from <see cref="AddLane"/>.</param>
        /// <param name="label">Text inside the span (cut with an ellipsis when it does not fit); may be null.</param>
        /// <param name="tooltip">Tooltip of the span; may be null.</param>
        /// <param name="color">Fill colour.</param>
        public int AddSpan(int lane, string label, string tooltip, Color color)
        {
            int index = AddSpan(lane, label, tooltip, (string)null);
            spans[index].Fill.style.backgroundColor = color;
            return index;
        }

        /// <summary>
        /// Adds a span whose look comes from a style sheet: <paramref name="className"/> is put on the span and its rules set the fill with
        /// <c>.className &gt; .vn-tl__span-fill { background-color: ... }</c> (and the label colour with <c>.className .vn-tl__span-label</c>),
        /// so a theme or the light skin can recolour it. The fill is rounded and keeps a gap of a pixel to the span next to it.
        /// </summary>
        /// <param name="lane">Lane index from <see cref="AddLane"/>.</param>
        /// <param name="label">Text inside the span (cut with an ellipsis when it does not fit); may be null.</param>
        /// <param name="tooltip">Tooltip of the span; may be null.</param>
        /// <param name="className">The USS class that colours the span; may be null.</param>
        public int AddSpan(int lane, string label, string tooltip, string className)
        {
            var element = new VisualElement();
            element.AddToClassList("vn-tl__span");
            if (!string.IsNullOrEmpty(className))
            {
                element.AddToClassList(className);
            }

            element.tooltip = tooltip;
            element.style.display = DisplayStyle.None;
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("vn-tl__span-fill");
            element.Add(fill);
            if (!string.IsNullOrEmpty(label))
            {
                var text = new Label(label) { pickingMode = PickingMode.Ignore };
                text.AddToClassList("vn-tl__span-label");
                element.Add(text);
            }

            lanes[lane].SpanHost.Add(element);
            spans.Add(new SpanItem { Element = element, Fill = fill, Start = -1f, End = -1f, Opacity = 1f });
            return spans.Count - 1;
        }

        /// <summary>Puts a span between two times (seconds); an empty or reversed range hides it.</summary>
        /// <param name="index">Span index from <see cref="AddSpan"/>.</param>
        /// <param name="start">Start in seconds.</param>
        /// <param name="end">End in seconds.</param>
        /// <param name="opacity">0 to 1.</param>
        public void SetSpan(int index, float start, float end, float opacity = 1f)
        {
            SpanItem item = spans[index];
            if (Mathf.Approximately(item.Start, start) && Mathf.Approximately(item.End, end) && Mathf.Approximately(item.Opacity, opacity))
            {
                return;
            }

            item.Start = start;
            item.End = end;
            item.Opacity = opacity;
            LayoutSpan(item);
        }

        /// <summary>Adds a grip that can be dragged and returns its index; give it a time and a range with <see cref="SetHandle"/>.</summary>
        /// <param name="lane">Lane index from <see cref="AddLane"/>.</param>
        /// <param name="name">Name shown in the bubble.</param>
        /// <param name="tooltip">Tooltip of the grip; may be null.</param>
        /// <param name="color">Colour of the grip's line.</param>
        public int AddHandle(int lane, string name, string tooltip, Color color)
        {
            int index = AddHandle(lane, name, tooltip);
            handles[index].Element[0].style.backgroundColor = color;
            return index;
        }

        /// <summary>
        /// Adds a marker: a grip drawn as a thin vertical line across the whole lane (a little taller than it) with a small rounded tab at its
        /// top, like the marker of an animation event. It glows when the pointer is on it and is drawn above the spans and the other grips.
        /// Everything else works as for <see cref="AddHandle(int, string, string)"/>.
        /// </summary>
        /// <param name="lane">Lane index from <see cref="AddLane"/>.</param>
        /// <param name="name">Name shown in the bubble.</param>
        /// <param name="tooltip">Tooltip of the marker; may be null.</param>
        public int AddMarker(int lane, string name, string tooltip)
        {
            int index = AddHandle(lane, name, tooltip);
            VisualElement element = handles[index].Element;
            element.AddToClassList("vn-tl__handle--line");
            var halo = new VisualElement { pickingMode = PickingMode.Ignore };
            halo.AddToClassList("vn-tl__halo");
            var tab = new VisualElement { pickingMode = PickingMode.Ignore };
            tab.AddToClassList("vn-tl__tab");
            element.Insert(0, halo);
            element.Insert(1, tab);
            return index;
        }

        /// <summary>Adds a grip that can be dragged, drawn as a slim rounded light pill in the theme's colours; see <see cref="AddHandle(int, string, string, Color)"/>.</summary>
        /// <param name="lane">Lane index from <see cref="AddLane"/>.</param>
        /// <param name="name">Name shown in the bubble.</param>
        /// <param name="tooltip">Tooltip of the grip; may be null.</param>
        public int AddHandle(int lane, string name, string tooltip)
        {
            var element = new VisualElement();
            element.AddToClassList("vn-tl__handle");
            element.tooltip = tooltip;
            var grip = new VisualElement { pickingMode = PickingMode.Ignore };
            grip.AddToClassList("vn-tl__grip");
            element.Add(grip);

            var item = new HandleItem { Element = element, Name = name, Time = float.NaN, Min = 0f, Max = 0f, Visible = true };
            element.userData = item;
            element.RegisterCallback<PointerDownEvent>(OnHandleDown);
            element.RegisterCallback<PointerMoveEvent>(OnHandleMove);
            element.RegisterCallback<PointerUpEvent>(OnHandleUp);
            element.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            element.RegisterCallback<PointerEnterEvent>(OnHandleEnter);
            element.RegisterCallback<PointerLeaveEvent>(OnHandleLeave);
            lanes[lane].HandleHost.Add(element);
            handles.Add(item);
            item.Index = handles.Count - 1;
            return item.Index;
        }

        /// <summary>Gives a grip its time and the range it may be dragged in (seconds). A grip whose range is empty cannot move.</summary>
        public void SetHandle(int index, float time, float min, float max)
        {
            HandleItem item = handles[index];
            item.Min = min;
            item.Max = Mathf.Max(max, min);
            if (!Mathf.Approximately(item.Time, time) || float.IsNaN(item.Time))
            {
                item.Time = time;
                LayoutHandle(item);
                if (bubbleFor == index)
                {
                    FillBubble(item.Name, time);
                    PlaceBubble(time);
                }
            }
        }

        /// <summary>Shows or hides a grip (a hidden grip cannot be picked or selected).</summary>
        public void SetHandleVisible(int index, bool visible)
        {
            HandleItem item = handles[index];
            item.Visible = visible;
            item.Element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>The current time of a grip in seconds.</summary>
        public float GetHandleTime(int index) => handles[index].Time;

        /// <summary>Moves the playhead (seconds); NaN hides it. It does not raise <see cref="PlayheadMoved"/>.</summary>
        public void SetPlayhead(float seconds)
        {
            if (Mathf.Approximately(playhead, seconds) || (float.IsNaN(playhead) && float.IsNaN(seconds)))
            {
                return;
            }

            playhead = seconds;
            LayoutPlayhead();
            if (scrubbing || bubbleFor == handles.Count)
            {
                FillBubble("Playhead", seconds);
                PlaceBubble(seconds);
            }
        }

        // ------------------------------------------------------------------ layout

        private void LayoutAll()
        {
            for (int i = 0; i < spans.Count; i++)
            {
                LayoutSpan(spans[i]);
            }

            for (int i = 0; i < handles.Count; i++)
            {
                LayoutHandle(handles[i]);
            }

            LayoutPlayhead();
            ruler.MarkDirtyRepaint();
        }

        private void LayoutSpan(SpanItem item)
        {
            // A span that runs past the end of the bar is drawn cut at the end (its times are not touched).
            float start = Mathf.Max(item.Start, 0f);
            float end = Mathf.Min(item.End, span);
            bool visible = end > start;
            item.Element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
            {
                return;
            }

            item.Element.style.left = Length.Percent(start / span * 100f);
            item.Element.style.width = Length.Percent((end - start) / span * 100f);
            // The fill fades, not the label on it.
            item.Fill.style.opacity = item.Opacity;
        }

        private void LayoutHandle(HandleItem item)
        {
            // A grip whose time is past the end of the bar sits at the end (its time is not touched until it is dragged).
            if (!float.IsNaN(item.Time))
            {
                item.Element.style.left = Length.Percent(Mathf.Clamp(item.Time, 0f, span) / span * 100f);
            }
        }

        private void LayoutPlayhead()
        {
            if (!playheadEnabled || float.IsNaN(playhead))
            {
                playheadLine.style.display = DisplayStyle.None;
                playheadHead.style.display = DisplayStyle.None;
                return;
            }

            playheadLine.style.display = DisplayStyle.Flex;
            playheadHead.style.display = DisplayStyle.Flex;
            float percent = playhead / span * 100f;
            playheadLine.style.left = Length.Percent(percent);
            playheadHead.style.left = Length.Percent(percent);
        }

        // ------------------------------------------------------------------ ruler

        private void OnRulerGeometry(GeometryChangedEvent evt)
        {
            ruler.MarkDirtyRepaint();
        }

        private void DrawRuler(MeshGenerationContext context)
        {
            if (!showRuler)
            {
                return;
            }

            Rect rect = ruler.contentRect;
            if (rect.width < 8f || span <= 0f)
            {
                return;
            }

            // The finest step that still leaves at least 6 pixels between two ticks; every fifth one is taller.
            float[] steps = RulerSteps;
            float step = steps[steps.Length - 1];
            for (int i = 0; i < steps.Length; i++)
            {
                if (steps[i] / span * rect.width >= 6f)
                {
                    step = steps[i];
                    break;
                }
            }

            Painter2D painter = context.painter2D;
            Color color = ruler.resolvedStyle.color;
            color.a *= 0.4f;
            painter.strokeColor = color;
            painter.lineWidth = 1f;
            painter.BeginPath();
            int count = Mathf.FloorToInt(span / step + 0.0001f);
            for (int i = 0; i <= count; i++)
            {
                float x = Mathf.Round(i * step / span * rect.width) + 0.5f;
                float tick = i % 5 == 0 ? 7f : 4f;
                painter.MoveTo(new Vector2(x, rect.height));
                painter.LineTo(new Vector2(x, rect.height - tick));
            }

            painter.Stroke();
        }

        private static readonly float[] RulerSteps = { 0.01f, 0.02f, 0.05f, 0.1f, 0.2f, 0.5f, 1f, 2f, 5f, 10f };

        // ------------------------------------------------------------------ style sheet

        private void OnAttach(AttachToPanelEvent evt)
        {
            // The sheet goes on the Veneer root the bar sits in (once per root); outside a Veneer tree it goes on the bar itself.
            StyleSheet sheet = Sheet;
            if (sheet == null)
            {
                return;
            }

            VisualElement host = this;
            for (VisualElement current = parent; current != null; current = current.parent)
            {
                if (current.ClassListContains("vn-root"))
                {
                    host = current;
                    break;
                }
            }

            if (!host.styleSheets.Contains(sheet))
            {
                host.styleSheets.Add(sheet);
            }
        }

        private void OnDetach(DetachFromPanelEvent evt)
        {
            EndInteraction(true);
            HideBubble();
        }

        // ------------------------------------------------------------------ pointer: grips

        private void OnHandleDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || dragging >= 0 || scrubbing)
            {
                return;
            }

            var element = (VisualElement)evt.currentTarget;
            var item = (HandleItem)element.userData;
            if (item.Max <= item.Min)
            {
                // Nothing to drag, but the grip is still selected for the keyboard.
                selected = item.Index;
                RefreshSelection();
                Focus();
                evt.StopPropagation();
                return;
            }

            selected = item.Index;
            RefreshSelection();
            Focus();
            dragging = item.Index;
            pointerId = evt.pointerId;
            captureElement = element;
            dragStartTime = item.Time;
            grabOffset = item.Time - PointerTime((Vector2)evt.position);
            element.CapturePointer(evt.pointerId);
            element.AddToClassList("vn-tl__handle--drag");
            ShowBubbleFor(item.Index);
            DragStarted?.Invoke(item.Index);
            evt.StopPropagation();
        }

        private void OnHandleMove(PointerMoveEvent evt)
        {
            if (dragging < 0 || evt.pointerId != pointerId || !captureElement.HasPointerCapture(pointerId))
            {
                return;
            }

            HandleItem item = handles[dragging];
            float raw = PointerTime((Vector2)evt.position) + grabOffset;
            float time = Snap(raw, item.Index, evt.shiftKey, evt.actionKey || evt.ctrlKey);
            time = Mathf.Clamp(Mathf.Clamp(time, 0f, span), item.Min, item.Max);
            if (!Mathf.Approximately(time, item.Time))
            {
                HandleMoved?.Invoke(item.Index, time);
            }

            FillBubble(item.Name, item.Time);
            PlaceBubble(item.Time);
            evt.StopPropagation();
        }

        private void OnHandleUp(PointerUpEvent evt)
        {
            if (dragging >= 0 && evt.pointerId == pointerId)
            {
                EndInteraction(false);
                evt.StopPropagation();
            }
        }

        private void OnHandleEnter(PointerEnterEvent evt)
        {
            var item = (HandleItem)((VisualElement)evt.currentTarget).userData;
            hovered = item.Index;
            if (!IsDragging)
            {
                ShowBubbleFor(item.Index);
            }
        }

        private void OnHandleLeave(PointerLeaveEvent evt)
        {
            var item = (HandleItem)((VisualElement)evt.currentTarget).userData;
            if (hovered == item.Index)
            {
                hovered = -1;
            }

            if (!IsDragging && bubbleFor == item.Index)
            {
                HideBubble();
            }
        }

        // ------------------------------------------------------------------ pointer: playhead

        private void OnBackgroundDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || !playheadEnabled || IsDragging)
            {
                return;
            }

            // The grips stop their own events, so this one is the ruler, a lane or a span.
            Focus();
            selected = handles.Count;
            RefreshSelection();
            scrubbing = true;
            pointerId = evt.pointerId;
            captureElement = this;
            this.CapturePointer(evt.pointerId);
            MovePlayhead(PointerTime((Vector2)evt.position), evt.shiftKey, evt.actionKey || evt.ctrlKey);
            ShowBubbleFor(handles.Count);
            evt.StopPropagation();
        }

        private void OnBackgroundMove(PointerMoveEvent evt)
        {
            if (!scrubbing || evt.pointerId != pointerId)
            {
                return;
            }

            MovePlayhead(PointerTime((Vector2)evt.position), evt.shiftKey, evt.actionKey || evt.ctrlKey);
            evt.StopPropagation();
        }

        private void OnBackgroundUp(PointerUpEvent evt)
        {
            if (scrubbing && evt.pointerId == pointerId)
            {
                EndInteraction(false);
                evt.StopPropagation();
            }
        }

        private void OnCaptureOut(PointerCaptureOutEvent evt)
        {
            EndInteraction(false);
        }

        private void MovePlayhead(float raw, bool shift, bool free)
        {
            float time = Mathf.Clamp(SnapPlayhead(raw, shift, free), 0f, span);
            if (!Mathf.Approximately(time, playhead))
            {
                playhead = time;
                LayoutPlayhead();
                PlayheadMoved?.Invoke(time);
            }

            FillBubble("Playhead", playhead);
            PlaceBubble(playhead);
        }

        // Ends a drag or a scrub (and puts the grip back when cancelled).
        private void EndInteraction(bool cancel)
        {
            int was = dragging;
            if (dragging >= 0 && cancel)
            {
                HandleMoved?.Invoke(was, dragStartTime);
            }

            VisualElement element = captureElement;
            int id = pointerId;
            dragging = -1;
            scrubbing = false;
            pointerId = -1;
            captureElement = null;
            if (element != null)
            {
                element.RemoveFromClassList("vn-tl__handle--drag");
                if (id >= 0 && element.HasPointerCapture(id))
                {
                    element.ReleasePointer(id);
                }
            }

            if (was >= 0)
            {
                DragEnded?.Invoke(was);
            }

            if (hovered < 0)
            {
                HideBubble();
            }
        }

        // ------------------------------------------------------------------ snapping

        private float PointerTime(Vector2 worldPosition)
        {
            float width = body.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 1f)
            {
                return 0f;
            }

            return body.WorldToLocal(worldPosition).x / width * span;
        }

        private float Snap(float seconds, int handle, bool shift, bool free)
        {
            if (free)
            {
                return Mathf.Round(seconds / FineStep) * FineStep;
            }

            if (shift)
            {
                float grid = frameDuration > 0f ? frameDuration : snapStep * 10f;
                return Mathf.Round(seconds / grid) * grid;
            }

            float snapped = Mathf.Round(seconds / snapStep) * snapStep;
            float width = body.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 1f)
            {
                return snapped;
            }

            float reach = MagnetPixels / width * span;
            float best = reach;
            float result = snapped;
            for (int i = 0; i < handles.Count; i++)
            {
                HandleItem other = handles[i];
                if (i == handle || !other.Visible || float.IsNaN(other.Time))
                {
                    continue;
                }

                float distance = Mathf.Abs(seconds - other.Time);
                if (distance <= best)
                {
                    best = distance;
                    result = other.Time;
                }
            }

            if (playheadEnabled && !float.IsNaN(playhead) && Mathf.Abs(seconds - playhead) <= best)
            {
                result = playhead;
            }

            return result;
        }

        private float SnapPlayhead(float seconds, bool shift, bool free)
        {
            if (free)
            {
                return Mathf.Round(seconds / FineStep) * FineStep;
            }

            // Scrubbing lands on whole frames when the frame length is known; Shift gives the plain grid instead.
            float grid = frameDuration > 0f && !shift ? frameDuration : (shift ? snapStep * 10f : snapStep);
            return Mathf.Round(seconds / grid) * grid;
        }

        // ------------------------------------------------------------------ keyboard

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape && IsDragging)
            {
                EndInteraction(true);
                evt.StopPropagation();
                return;
            }

            int dir = 0;
            int select = 0;
            switch (evt.keyCode)
            {
                case KeyCode.LeftArrow: dir = -1; break;
                case KeyCode.RightArrow: dir = 1; break;
                case KeyCode.UpArrow:
                case KeyCode.PageUp: select = -1; break;
                case KeyCode.DownArrow:
                case KeyCode.PageDown: select = 1; break;
                case KeyCode.Home: SelectEdge(true); evt.StopPropagation(); return;
                case KeyCode.End: SelectEdge(false); evt.StopPropagation(); return;
                default: return;
            }

            if (dir != 0)
            {
                Nudge(dir, evt.shiftKey, evt.actionKey || evt.ctrlKey);
            }
            else
            {
                SelectStep(select);
            }

            evt.StopPropagation();
        }

        private void OnNavigationMove(NavigationMoveEvent evt)
        {
            switch (evt.direction)
            {
                case NavigationMoveEvent.Direction.Left: Nudge(-1, false, false); break;
                case NavigationMoveEvent.Direction.Right: Nudge(1, false, false); break;
                case NavigationMoveEvent.Direction.Up: SelectStep(-1); break;
                case NavigationMoveEvent.Direction.Down: SelectStep(1); break;
                default: return;
            }

            evt.StopPropagation();
        }

        private void OnFocusOut(FocusOutEvent evt)
        {
            if (!IsDragging && hovered < 0)
            {
                HideBubble();
            }
        }

        private void Nudge(int direction, bool shift, bool fine)
        {
            float step = fine ? FineStep : (shift ? (frameDuration > 0f ? frameDuration : snapStep * 10f) : snapStep);
            if (selected == handles.Count)
            {
                if (playheadEnabled)
                {
                    float start = float.IsNaN(playhead) ? 0f : playhead;
                    MovePlayhead(start + direction * step, false, true);
                    ShowBubbleBriefly(handles.Count);
                }

                return;
            }

            HandleItem item = handles[selected];
            if (!item.Visible || item.Max <= item.Min)
            {
                return;
            }

            float time = Mathf.Clamp(Mathf.Clamp(Mathf.Round((item.Time + direction * step) / FineStep) * FineStep, 0f, span), item.Min, item.Max);
            if (!Mathf.Approximately(time, item.Time))
            {
                DragStarted?.Invoke(item.Index);
                HandleMoved?.Invoke(item.Index, time);
                DragEnded?.Invoke(item.Index);
            }

            ShowBubbleBriefly(item.Index);
        }

        private void SelectStep(int direction)
        {
            int count = handles.Count + (playheadEnabled ? 1 : 0);
            if (count == 0)
            {
                return;
            }

            int index = selected;
            for (int tries = 0; tries < count; tries++)
            {
                index = (index + direction + count) % count;
                if (index == handles.Count || handles[index].Visible)
                {
                    break;
                }
            }

            selected = index;
            RefreshSelection();
            ShowBubbleBriefly(index);
        }

        private void SelectEdge(bool first)
        {
            int count = handles.Count + (playheadEnabled ? 1 : 0);
            if (count == 0)
            {
                return;
            }

            selected = first ? 0 : count - 1;
            SelectStep(0);
        }

        private void RefreshSelection()
        {
            for (int i = 0; i < handles.Count; i++)
            {
                handles[i].Element.EnableInClassList("vn-tl__handle--selected", i == selected);
            }

            playheadHead.EnableInClassList("vn-tl__playhead-head--selected", selected == handles.Count);
        }

        // ------------------------------------------------------------------ bubble

        private void ShowBubbleFor(int index)
        {
            bubbleFor = index;
            if (index == handles.Count)
            {
                FillBubble("Playhead", playhead);
                PlaceBubble(playhead);
            }
            else
            {
                FillBubble(handles[index].Name, handles[index].Time);
                PlaceBubble(handles[index].Time);
            }

            bubble.AddToClassList("vn-tl__bubble--show");
        }

        private void ShowBubbleBriefly(int index)
        {
            ShowBubbleFor(index);
            if (bubbleTimer == null)
            {
                bubbleTimer = schedule.Execute(hideBubbleLater);
            }

            bubbleTimer.ExecuteLater(KeyBubbleMilliseconds);
        }

        private void HideBubbleIfIdle()
        {
            if (!IsDragging && hovered < 0)
            {
                HideBubble();
            }
        }

        private void HideBubble()
        {
            bubbleFor = -1;
            bubble.RemoveFromClassList("vn-tl__bubble--show");
        }

        private void FillBubble(string name, float seconds)
        {
            if (!string.Equals(bubbleName.text, name))
            {
                bubbleName.text = name;
            }

            string time = timeText.Get(seconds / displayScale);
            if (!string.Equals(bubbleTime.text, time))
            {
                bubbleTime.text = time;
            }

            bool frames = frameDuration > 0f;
            bubbleFrame.style.display = frames ? DisplayStyle.Flex : DisplayStyle.None;
            if (frames)
            {
                string frame = frameText.Get(Mathf.Round(seconds / frameDuration));
                if (!string.Equals(bubbleFrame.text, frame))
                {
                    bubbleFrame.text = frame;
                }
            }
        }

        private void PlaceBubble(float seconds)
        {
            float width = body.resolvedStyle.width;
            if (float.IsNaN(width) || float.IsNaN(seconds))
            {
                return;
            }

            float own = bubble.resolvedStyle.width;
            float half = float.IsNaN(own) || own < 10f ? 50f : own * 0.5f;
            float total = width + 2f * SidePadding;
            float center = SidePadding + seconds / span * width;
            bubble.style.left = Mathf.Clamp(center, half, Mathf.Max(half, total - half));
        }

        // ------------------------------------------------------------------ items

        private sealed class LaneItem
        {
            public VisualElement Element;
            public VisualElement SpanHost;
            public VisualElement HandleHost;
        }

        private sealed class SpanItem
        {
            public VisualElement Element;
            public VisualElement Fill;
            public float Start;
            public float End;
            public float Opacity;
        }

        private sealed class HandleItem
        {
            public VisualElement Element;
            public string Name;
            public int Index;
            public float Time;
            public float Min;
            public float Max;
            public bool Visible;
        }
    }
}
