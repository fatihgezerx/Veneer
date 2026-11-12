using UnityEngine;

namespace Veneer
{
    /// <summary>
    /// The camera maths of a <see cref="VeneerModelPreview"/>, with no UI in it: a turn (yaw), a pan and a zoom around the model's bounds,
    /// the wheel zoom that keeps the point under the pointer still, jumps to a <see cref="VeneerModelFocus"/> and the eased move towards it.
    /// Time is passed in, so the move is deterministic.
    /// </summary>
    public sealed class VeneerModelPreviewOrbit
    {
        private readonly VeneerModelPreviewSettings settings;

        private Bounds bounds = new Bounds(Vector3.up, Vector3.one);
        private float baseDistance = 3f;
        private float yaw;
        private float zoom = 1f;
        private Vector3 pan;

        private bool moving;
        private double moveStart;
        private float moveSeconds;
        private Vector3 fromPan;
        private Vector3 toPan;
        private float fromZoom;
        private float toZoom;

        /// <summary>Makes an orbit with the given tuning.</summary>
        public VeneerModelPreviewOrbit(VeneerModelPreviewSettings settings)
        {
            this.settings = settings;
            yaw = settings.InitialYaw;
        }

        /// <summary>True while an eased move towards a focus is running (call <see cref="Step"/> until it returns false).</summary>
        public bool IsMoving => moving;

        /// <summary>Turn around the model in degrees.</summary>
        public float Yaw => yaw;

        /// <summary>Current zoom (1 fits the model).</summary>
        public float Zoom => zoom;

        /// <summary>Offset of the view from the bounds' centre.</summary>
        public Vector3 Pan => pan;

        private Quaternion Rotation => Quaternion.Euler(settings.Pitch, yaw, 0f);

        private float Distance => baseDistance * zoom;

        private float TanHalfFov => Mathf.Tan(settings.FieldOfView * 0.5f * Mathf.Deg2Rad);

        /// <summary>Sets the model's bounds; the view keeps its turn, zoom and pan (the pan is kept inside the new bounds).</summary>
        public void SetBounds(Bounds value)
        {
            bounds = value;
            baseDistance = Mathf.Max(value.extents.magnitude, 0.5f) * settings.FitDistanceFactor;
            ClampPan();
        }

        /// <summary>Turns the view back to the start and shows <paramref name="focus"/> at once (or the whole bounds when there is none).</summary>
        public void Reset(bool hasFocus, in VeneerModelFocus focus)
        {
            moving = false;
            yaw = settings.InitialYaw;
            if (hasFocus)
            {
                Target(focus, out pan, out zoom);
            }
            else
            {
                pan = Vector3.zero;
                zoom = 1f;
            }
        }

        /// <summary>Turns the model by a horizontal drag of <paramref name="pixels"/>.</summary>
        public void Rotate(float pixels)
        {
            yaw -= pixels * settings.DragSpeed;
        }

        /// <summary>Turns the model by an amount of degrees (keyboard).</summary>
        public void RotateDegrees(float degrees)
        {
            yaw += degrees;
        }

        /// <summary>Moves the view with the pointer: what is under it follows it. <paramref name="heightPixels"/> is the height of the picture.</summary>
        public void Pan2D(Vector2 deltaPixels, float heightPixels)
        {
            moving = false;
            float worldPerPixel = 2f * Distance * TanHalfFov / Mathf.Max(heightPixels, 1f);
            Quaternion rotation = Rotation;
            pan += (-(rotation * Vector3.right) * deltaPixels.x + rotation * Vector3.up * deltaPixels.y) * worldPerPixel;
            ClampPan();
        }

        /// <summary>
        /// Multiplies the distance by <paramref name="factor"/>; the point at <paramref name="ndc"/> (-1..1 over the picture, y up) stays
        /// where it is on the screen. <paramref name="aspect"/> is width over height.
        /// </summary>
        public void ZoomBy(float factor, Vector2 ndc, float aspect)
        {
            moving = false;
            float next = Mathf.Clamp(zoom * factor, settings.MinZoom, settings.MaxZoom);
            float applied = next / zoom;
            float halfHeight = Distance * TanHalfFov;
            Quaternion rotation = Rotation;
            pan += (rotation * Vector3.right * (ndc.x * aspect) + rotation * Vector3.up * ndc.y) * (halfHeight * (1f - applied));
            zoom = next;
            ClampPan();
        }

        /// <summary>Starts the move to a focus; with <paramref name="seconds"/> of 0 or less it is there at once. Returns true while it still runs.</summary>
        public bool MoveTo(in VeneerModelFocus focus, double now, float seconds)
        {
            Target(focus, out Vector3 targetPan, out float targetZoom);
            if (seconds <= 0f)
            {
                moving = false;
                pan = targetPan;
                zoom = targetZoom;
                return false;
            }

            moving = true;
            moveStart = now;
            moveSeconds = seconds;
            fromPan = pan;
            toPan = targetPan;
            fromZoom = zoom;
            toZoom = targetZoom;
            return true;
        }

        /// <summary>Advances the eased move to time <paramref name="now"/>. Returns true while it has more to do.</summary>
        public bool Step(double now)
        {
            if (!moving)
            {
                return false;
            }

            float t = Mathf.Clamp01((float)((now - moveStart) / moveSeconds));
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);
            pan = Vector3.LerpUnclamped(fromPan, toPan, eased);
            // The zoom is a distance: it is blended in log space so it feels even whether it goes in or out.
            zoom = Mathf.Exp(Mathf.Lerp(Mathf.Log(fromZoom), Mathf.Log(toZoom), eased));
            if (t >= 1f)
            {
                moving = false;
                pan = toPan;
                zoom = toZoom;
            }

            return moving;
        }

        /// <summary>Fills <paramref name="camera"/> from the current turn, pan and zoom.</summary>
        public void GetCamera(ref VeneerModelCamera camera)
        {
            Quaternion rotation = Rotation;
            Vector3 center = bounds.center + pan;
            camera.Position = center + rotation * new Vector3(0f, 0f, -Distance);
            camera.Rotation = rotation;
            camera.Yaw = yaw;
            camera.FieldOfView = settings.FieldOfView;
            camera.NearClip = settings.NearClip;
            camera.FarClip = Distance + bounds.extents.magnitude * settings.FarClipExtents;
        }

        // Where a focus puts the view: the point of the bounds it looks at, and the zoom that makes its height fraction fill the picture.
        private void Target(in VeneerModelFocus focus, out Vector3 targetPan, out float targetZoom)
        {
            Vector3 size = bounds.size;
            Vector3 point = bounds.min + Vector3.Scale(size, focus.Center);
            targetPan = point - bounds.center;
            float height = Mathf.Max(size.y, 0.1f);
            float visible = height * focus.HeightFraction;
            targetZoom = Mathf.Clamp(visible / (2f * TanHalfFov * baseDistance), settings.MinZoom, settings.MaxZoom);
            ClampPan(ref targetPan);
        }

        private void ClampPan()
        {
            ClampPan(ref pan);
        }

        private void ClampPan(ref Vector3 value)
        {
            Vector3 limit = bounds.extents * settings.PanLimit;
            value = new Vector3(
                Mathf.Clamp(value.x, -limit.x, limit.x),
                Mathf.Clamp(value.y, -limit.y, limit.y),
                Mathf.Clamp(value.z, -limit.z, limit.z));
        }
    }
}
