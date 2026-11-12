using System;
using UnityEngine;

namespace Veneer
{
    /// <summary>
    /// The tuning of a <see cref="VeneerModelPreview"/>: how fast a drag turns the model, the camera, the zoom limits and steps, how long a
    /// focus change takes. A plain object the caller may fill from its own config (a ScriptableObject, for example) and hand to the control;
    /// the defaults are the values the first user (the N-Hance look preview) was tuned with.
    /// </summary>
    [Serializable]
    public sealed class VeneerModelPreviewSettings
    {
        /// <summary>Degrees the model turns per pixel of horizontal drag.</summary>
        public float DragSpeed = 0.8f;

        /// <summary>Angle in degrees the camera looks down at the model.</summary>
        public float Pitch = 8f;

        /// <summary>Turn of the camera around the model (degrees) at the start and after a reset.</summary>
        public float InitialYaw = 160f;

        /// <summary>Vertical field of view of the preview camera, in degrees.</summary>
        public float FieldOfView = 30f;

        /// <summary>Smallest zoom (1 = the distance that fits the model; smaller is closer).</summary>
        public float MinZoom = 0.05f;

        /// <summary>Largest zoom (farthest away).</summary>
        public float MaxZoom = 2.5f;

        /// <summary>Factor the distance changes by per wheel unit (a notch is about 3 units).</summary>
        public float WheelStep = 1.06f;

        /// <summary>Factor the distance changes by per press of a zoom button or key.</summary>
        public float ButtonStep = 1.25f;

        /// <summary>The distance that fits the model is its bounds' extents magnitude times this.</summary>
        public float FitDistanceFactor = 2.6f;

        /// <summary>How far the view may be panned away from the bounds' centre, in bounds extents.</summary>
        public float PanLimit = 1.2f;

        /// <summary>Degrees an arrow key turns the model.</summary>
        public float KeyTurnDegrees = 10f;

        /// <summary>Fraction of the view an arrow key with Shift pans.</summary>
        public float KeyPanFraction = 0.08f;

        /// <summary>Seconds a focus change takes at normal motion (multiplied by <see cref="VeneerMotionKit.Scale"/>; 0 = at once).</summary>
        public float FocusSeconds = 0.35f;

        /// <summary>Near clip plane of the preview camera.</summary>
        public float NearClip = 0.02f;

        /// <summary>The far clip plane is the camera distance plus the bounds' extents magnitude times this.</summary>
        public float FarClipExtents = 4f;

        /// <summary>Background of the rendered picture in the dark editor skin.</summary>
        public Color BackgroundDark = new Color(0.17f, 0.17f, 0.18f, 1f);

        /// <summary>Background of the rendered picture in the light editor skin.</summary>
        public Color BackgroundLight = new Color(0.72f, 0.72f, 0.74f, 1f);

        /// <summary>Flips the picture vertically; a switch in case a platform shows the render texture upside down.</summary>
        public bool FlipVertical;
    }
}
