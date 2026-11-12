using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Veneer
{
    /// <summary>
    /// A ready-made <see cref="IVeneerModelPreviewSource"/> around <c>PreviewRenderUtility</c>: it creates the private preview scene on first
    /// use, sets the camera and the two lights for every render, draws, and cleans up on <see cref="Dispose"/>. A caller derives from it and
    /// only supplies the model: build it into <see cref="Utility"/> in <see cref="Sync"/> and release it in <see cref="ReleaseModel"/>.
    /// Must be rendered inside an IMGUI repaint (<see cref="VeneerModelPreview"/> does that): <c>PreviewRenderUtility</c> reads the current event.
    /// </summary>
    public abstract class VeneerModelPreviewScene : IVeneerModelPreviewSource
    {
        private static readonly IReadOnlyList<VeneerModelFocus> NoFocuses = new VeneerModelFocus[0];

        private readonly VeneerModelPreviewSettings settings;
        private PreviewRenderUtility utility;
        private Light[] lights;

        /// <summary>Makes a source with the given tuning (null = the defaults).</summary>
        protected VeneerModelPreviewScene(VeneerModelPreviewSettings settings = null)
        {
            this.settings = settings ?? new VeneerModelPreviewSettings();
        }

        /// <summary>The tuning in use.</summary>
        public VeneerModelPreviewSettings Settings => settings;

        /// <inheritdoc />
        public abstract bool HasModel { get; }

        /// <inheritdoc />
        public virtual string EmptyHint => null;

        /// <inheritdoc />
        public abstract Bounds Bounds { get; }

        /// <inheritdoc />
        public virtual IReadOnlyList<VeneerModelFocus> Focuses => NoFocuses;

        /// <summary>The private preview scene's utility, created on first use; add the model to its scene (<c>InstantiatePrefabInScene</c>, <c>AddSingleGO</c>).</summary>
        protected PreviewRenderUtility Utility
        {
            get
            {
                if (utility == null)
                {
                    utility = new PreviewRenderUtility();
                }

                return utility;
            }
        }

        /// <inheritdoc />
        public abstract bool Sync();

        /// <inheritdoc />
        public Texture Render(Vector2 size, in VeneerModelCamera camera)
        {
            PreviewRenderUtility u = Utility;
            Camera view = u.camera;
            view.transform.position = camera.Position;
            view.transform.rotation = camera.Rotation;
            view.nearClipPlane = camera.NearClip;
            view.farClipPlane = camera.FarClip;
            view.fieldOfView = camera.FieldOfView;
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = EditorGUIUtility.isProSkin ? settings.BackgroundDark : settings.BackgroundLight;

            // The lights turn with the camera, so the model is lit from the same side whatever way it is turned.
            lights ??= u.lights;
            lights[0].transform.rotation = Quaternion.Euler(35f, camera.Yaw + 25f, 0f);
            lights[0].intensity = 1.3f;
            lights[1].transform.rotation = Quaternion.Euler(-20f, camera.Yaw + 160f, 0f);
            lights[1].intensity = 0.7f;

            u.BeginPreview(new Rect(0f, 0f, size.x, size.y), GUIStyle.none);
            // The field of view stays the one set above (the utility would otherwise widen it for a picture taller than wide).
            u.Render(true, false);
            return u.EndPreview();
        }

        /// <summary>Releases the model's objects (the scene stays); called by <see cref="Dispose"/> before the utility is cleaned up.</summary>
        protected abstract void ReleaseModel();

        /// <inheritdoc />
        public void Dispose()
        {
            ReleaseModel();
            if (utility != null)
            {
                utility.Cleanup();
                utility = null;
                lights = null;
            }
        }
    }
}
