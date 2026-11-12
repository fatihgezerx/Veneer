using System;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// Drag-to-change for number rows: hold the left button on the label of an int, float or double field and move the mouse left or right
    /// to lower or raise the value, like the stock Inspector. Shift is four times faster, Alt four times slower. The step grows with the
    /// size of the value (as in the stock Inspector) so small numbers move finely and big ones quickly. <see cref="VeneerStyle.FieldRow"/>
    /// attaches it to every row whose control is a number field, so every number of every studio gets it. One drag is one Undo step.
    /// </summary>
    public static class VeneerScrub
    {
        private const string ScrubClass = "vn-scrub";
        private const float BaseSensitivity = 0.03f;
        private const float FastFactor = 4f;
        private const float SlowFactor = 0.25f;

        private static readonly ConditionalWeakTable<VisualElement, Scrubber> Attached = new ConditionalWeakTable<VisualElement, Scrubber>();

        /// <summary>Makes <paramref name="handle"/> (normally the row's label) change <paramref name="field"/> when dragged sideways.</summary>
        public static void Attach(VisualElement handle, FloatField field)
        {
            Register(handle, new Scrubber(handle, field, () => field.value, value => field.value = (float)value, false));
        }

        /// <summary>Makes <paramref name="handle"/> (normally the row's label) change <paramref name="field"/> when dragged sideways.</summary>
        public static void Attach(VisualElement handle, IntegerField field)
        {
            Register(handle, new Scrubber(handle, field, () => field.value, value => field.value = (int)Math.Round(value), true));
        }

        /// <summary>Makes <paramref name="handle"/> (normally the row's label) change <paramref name="field"/> when dragged sideways.</summary>
        public static void Attach(VisualElement handle, DoubleField field)
        {
            Register(handle, new Scrubber(handle, field, () => field.value, value => field.value = value, false));
        }

        /// <summary>Attaches to the handle when <paramref name="control"/> is a number field; any other control is left alone.</summary>
        public static void AttachIfNumber(VisualElement handle, VisualElement control)
        {
            switch (control)
            {
                case FloatField floatField:
                    Attach(handle, floatField);
                    break;
                case IntegerField integerField:
                    Attach(handle, integerField);
                    break;
                case DoubleField doubleField:
                    Attach(handle, doubleField);
                    break;
            }
        }

        /// <summary>Keeps the dragged value inside <paramref name="min"/>..<paramref name="max"/> (a handle without a scrub is ignored).</summary>
        public static void SetLimits(VisualElement handle, float min, float max)
        {
            if (Attached.TryGetValue(handle, out Scrubber scrubber))
            {
                scrubber.Min = min;
                scrubber.Max = max;
            }
        }

        /// <summary>
        /// Makes a drag on <paramref name="handle"/> call <paramref name="live"/> on every move instead of writing the field, and
        /// <paramref name="commit"/> once when the button is released. For a number whose every write is costly (it saves an asset): the
        /// view follows the drag, the save happens once. A handle without a scrub is ignored.
        /// </summary>
        public static void Deferred(VisualElement handle, Action<float> live, Action commit)
        {
            if (Attached.TryGetValue(handle, out Scrubber scrubber))
            {
                scrubber.Live = live;
                scrubber.Commit = commit;
            }
        }

        private static void Register(VisualElement handle, Scrubber scrubber)
        {
            Attached.Remove(handle);
            Attached.Add(handle, scrubber);
        }

        private sealed class Scrubber
        {
            private readonly VisualElement handle;
            private readonly VisualElement field;
            private readonly Func<double> get;
            private readonly Action<double> set;
            private readonly bool whole;
            private bool dragging;
            private double accumulated;
            private float sensitivity;
            private int undoGroup;

            public Action<float> Live;
            public Action Commit;
            public float Min = float.NegativeInfinity;
            public float Max = float.PositiveInfinity;

            public Scrubber(VisualElement handle, VisualElement field, Func<double> get, Action<double> set, bool whole)
            {
                this.handle = handle;
                this.field = field;
                this.get = get;
                this.set = set;
                this.whole = whole;
                handle.AddToClassList(ScrubClass);
                handle.RegisterCallback<PointerDownEvent>(OnDown);
                handle.RegisterCallback<PointerMoveEvent>(OnMove);
                handle.RegisterCallback<PointerUpEvent>(OnUp);
                handle.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            }

            private void OnDown(PointerDownEvent evt)
            {
                if (evt.button != 0 || !field.enabledInHierarchy)
                {
                    return;
                }

                field.Blur();
                dragging = true;
                accumulated = get();
                sensitivity = Mathf.Max(1f, Mathf.Pow(Mathf.Abs((float)accumulated), 0.5f)) * BaseSensitivity;
                undoGroup = Undo.GetCurrentGroup();
                handle.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            }

            private void OnMove(PointerMoveEvent evt)
            {
                if (!dragging || !handle.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                float speed = evt.shiftKey ? FastFactor : evt.altKey ? SlowFactor : 1f;
                accumulated += evt.deltaPosition.x * sensitivity * speed;
                accumulated = Math.Min(Math.Max(accumulated, Min), Max);
                double value = accumulated;
                if (!whole)
                {
                    int digits = Mathf.Clamp(-Mathf.FloorToInt(Mathf.Log10(sensitivity)) + 1, 0, 12);
                    value = Math.Round(value, digits);
                }

                if (Live != null)
                {
                    Live((float)value);
                }
                else if (value != get())
                {
                    set(value);
                }

                evt.StopPropagation();
            }

            private void OnUp(PointerUpEvent evt)
            {
                if (!dragging || evt.button != 0)
                {
                    return;
                }

                handle.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            }

            private void OnCaptureOut(PointerCaptureOutEvent evt)
            {
                if (!dragging)
                {
                    return;
                }

                dragging = false;
                Commit?.Invoke();
                Undo.CollapseUndoOperations(undoGroup);
            }
        }
    }
}
