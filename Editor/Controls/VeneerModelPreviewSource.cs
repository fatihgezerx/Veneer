using System;
using System.Collections.Generic;
using UnityEngine;

namespace Veneer
{
    /// <summary>
    /// A named view of the model the toolbar of a <see cref="VeneerModelPreview"/> jumps to (for a character: Full, Head, Body, Legs).
    /// The place is given relative to the model's bounds so it keeps fitting when the model changes.
    /// </summary>
    public readonly struct VeneerModelFocus
    {
        /// <summary>The text of the button (used when <see cref="Icon"/> is empty; otherwise the tooltip carries it).</summary>
        public readonly string Name;

        /// <summary>A kit icon name for the button; empty shows <see cref="Name"/> as text.</summary>
        public readonly string Icon;

        /// <summary>The tooltip of the button; empty falls back to <see cref="Name"/>.</summary>
        public readonly string Tooltip;

        /// <summary>The point looked at, as fractions of the bounds (0 = min, 1 = max, 0.5 = the middle) on each axis.</summary>
        public readonly Vector3 Center;

        /// <summary>The part of the bounds' height that fills the picture (1.15 = the whole model with some air).</summary>
        public readonly float HeightFraction;

        /// <summary>Makes a focus looking at the middle of the bounds horizontally and at a height given as a fraction of it.</summary>
        public VeneerModelFocus(string name, string icon, string tooltip, float centerHeight, float heightFraction)
            : this(name, icon, tooltip, new Vector3(0.5f, centerHeight, 0.5f), heightFraction)
        {
        }

        /// <summary>Makes a focus looking at any point of the bounds.</summary>
        public VeneerModelFocus(string name, string icon, string tooltip, Vector3 center, float heightFraction)
        {
            Name = name ?? string.Empty;
            Icon = icon ?? string.Empty;
            Tooltip = tooltip ?? string.Empty;
            Center = center;
            HeightFraction = heightFraction;
        }
    }

    /// <summary>The camera the control asks a source to render with (the control owns turning, panning and zooming; the source owns the scene).</summary>
    public struct VeneerModelCamera
    {
        /// <summary>World position of the camera.</summary>
        public Vector3 Position;

        /// <summary>World rotation of the camera.</summary>
        public Quaternion Rotation;

        /// <summary>Turn around the model in degrees; a source may turn its lights with it.</summary>
        public float Yaw;

        /// <summary>Vertical field of view in degrees.</summary>
        public float FieldOfView;

        /// <summary>Near clip plane.</summary>
        public float NearClip;

        /// <summary>Far clip plane.</summary>
        public float FarClip;
    }

    /// <summary>
    /// What a <see cref="VeneerModelPreview"/> shows: it builds and disposes a private preview scene, tells the control the model's bounds
    /// and renders the scene with the camera the control asks for. The control never touches the scene itself, so nothing here is specific to
    /// the model type. <see cref="VeneerModelPreviewScene"/> implements the plumbing around <c>PreviewRenderUtility</c>.
    /// The caller owns the source and disposes it; the control only reads it.
    /// </summary>
    public interface IVeneerModelPreviewSource : IDisposable
    {
        /// <summary>True while there is a model to show (otherwise the control shows <see cref="EmptyHint"/>).</summary>
        bool HasModel { get; }

        /// <summary>Text shown over the empty picture while there is no model (what is missing, or "Loading..."); may be null.</summary>
        string EmptyHint { get; }

        /// <summary>World bounds of the model; read after <see cref="Sync"/> reported a change.</summary>
        Bounds Bounds { get; }

        /// <summary>The views of the toolbar; the first one is the starting view and the one Reset returns to. May be empty.</summary>
        IReadOnlyList<VeneerModelFocus> Focuses { get; }

        /// <summary>
        /// Brings the scene up to date with what it should show (rebuild only when something changed). Called when the control is asked to
        /// <see cref="VeneerModelPreview.Refresh"/>, and once when it first shows. Returns true when the model or its bounds changed.
        /// </summary>
        bool Sync();

        /// <summary>Renders the scene. The size is in points; the picture is made at the panel's pixel density. Returns the texture to show.</summary>
        Texture Render(Vector2 size, in VeneerModelCamera camera);
    }
}
