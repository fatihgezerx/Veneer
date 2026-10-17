using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// A thin drag handle between two panes of the Inspector Designer. It does not move anything itself: it captures the pointer and
    /// reports the distance dragged since the press to the owner, which sizes its own pane (so the owner decides what is fixed and what
    /// takes the rest). A double click asks the owner to restore the default size.
    /// </summary>
    internal sealed class VeneerDesignerSplitter : VisualElement
    {
        /// <summary>Thickness in pixels of the handle (its width between columns, its height between rows).</summary>
        public const float Thickness = 6f;

        private readonly bool betweenColumns;
        private readonly Action begin;
        private readonly Action<float> drag;
        private readonly Action reset;

        private Vector2 pressPosition;
        private bool dragging;

        /// <summary>Creates a handle.</summary>
        /// <param name="betweenColumns">True for a vertical bar between left and right panes (reports horizontal distance); false for a horizontal bar between upper and lower panes (reports vertical distance).</param>
        /// <param name="begin">Called when a drag starts; the owner reads the current size of the pane it resizes here.</param>
        /// <param name="drag">Called while dragging with the distance in pixels since the press (right or down is positive).</param>
        /// <param name="reset">Called on a double click; may be null.</param>
        public VeneerDesignerSplitter(bool betweenColumns, Action begin, Action<float> drag, Action reset)
        {
            this.betweenColumns = betweenColumns;
            this.begin = begin;
            this.drag = drag;
            this.reset = reset;
            AddToClassList("vnd-splitter");
            AddToClassList(betweenColumns ? "vnd-splitter--columns" : "vnd-splitter--rows");
            if (betweenColumns)
            {
                style.width = Thickness;
                style.minWidth = Thickness;
            }
            else
            {
                style.height = Thickness;
                style.minHeight = Thickness;
            }

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            evt.StopPropagation();
            if (evt.clickCount >= 2)
            {
                if (reset != null)
                {
                    reset();
                }

                return;
            }

            if (begin != null)
            {
                begin();
            }

            pressPosition = evt.position;
            dragging = true;
            EnableInClassList("vnd-splitter--active", true);
            this.CapturePointer(evt.pointerId);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!dragging || !this.HasPointerCapture(evt.pointerId))
            {
                return;
            }

            Vector2 position = evt.position;
            drag(betweenColumns ? position.x - pressPosition.x : position.y - pressPosition.y);
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!dragging)
            {
                return;
            }

            if (this.HasPointerCapture(evt.pointerId))
            {
                this.ReleasePointer(evt.pointerId);
            }

            Finish();
            evt.StopPropagation();
        }

        private void OnCaptureOut(PointerCaptureOutEvent evt)
        {
            Finish();
        }

        private void Finish()
        {
            dragging = false;
            EnableInClassList("vnd-splitter--active", false);
        }
    }
}
