using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    // The live preview: the REAL Inspector, rendered by the layout renderer on the temporary instance, rebuilt (throttled) after every edit.
    public sealed partial class VeneerDesignerWindow
    {
        private const string PreviewKey = "Veneer.Designer.Preview";
        private const long PreviewDelayMs = 150;

        private ScrollView previewScroll;
        private VisualElement previewHost;
        private VisualElement previewRoot;
        private SerializedObject previewSo;
        private bool previewOn;
        private bool previewPending;
        private Action previewAction;

        // The preview group is the drag handle on top of a scrolling box; hiding the group gives the whole column back to the tree.
        private void BuildPreviewPane(VisualElement column, bool on)
        {
            previewOn = on;
            previewGroup = new VisualElement();
            previewGroup.AddToClassList("vnd-preview-group");
            previewGroup.Add(new VeneerDesignerSplitter(false, BeginPreviewDrag, DragPreview, ResetPreview));

            previewScroll = new ScrollView(ScrollViewMode.Vertical);
            previewScroll.AddToClassList("vnd-preview");
            previewHost = new VisualElement();
            previewHost.AddToClassList("vnd-preview__host");
            previewScroll.Add(previewHost);
            previewGroup.Add(previewScroll);
            column.Add(previewGroup);
            RestorePreviewHeight();
            VeneerStyle.SetDisplay(previewGroup, on);
        }

        private void SetPreviewOn(bool on)
        {
            previewOn = on;
            SessionState.SetBool(PreviewKey, on);
            VeneerStyle.SetDisplay(previewGroup, on);
            if (on)
            {
                SchedulePreview();
            }
            else
            {
                ClearPreview();
            }
        }

        // Throttled: the first request schedules one rebuild 150 ms later, requests in between ride along.
        private void SchedulePreview()
        {
            if (!previewOn || previewPending || RootElement == null || previewHost == null)
            {
                return;
            }

            previewPending = true;
            if (previewAction == null)
            {
                previewAction = RebuildPreview;
            }

            RootElement.schedule.Execute(previewAction).StartingIn(PreviewDelayMs);
        }

        private void ClearPreview()
        {
            if (previewRoot != null)
            {
                previewRoot.Unbind();
                previewRoot.RemoveFromHierarchy();
                previewRoot = null;
            }

            if (previewHost != null)
            {
                previewHost.Clear();
            }

            if (previewSo != null)
            {
                previewSo.Dispose();
                previewSo = null;
            }
        }

        private void RebuildPreview()
        {
            previewPending = false;
            if (RootElement == null || previewHost == null)
            {
                return;
            }

            ClearPreview();
            if (!previewOn || layout == null)
            {
                return;
            }

            if (tempObject == null)
            {
                previewHost.Add(VeneerStyle.Hint("No preview: the script has no instance to show (see the message above).", "info", Brand));
                return;
            }

            if (!layout.HasContent)
            {
                previewHost.Add(VeneerStyle.Hint("An empty layout shows Unity's default Inspector. Add a tab to see the styled one here.", "info", Brand));
                return;
            }

            try
            {
                previewSo = new SerializedObject(tempObject);
                VeneerBrand brand = VeneerLayoutTheme.BrandFor(layout);
                VisualElement root = VeneerStyle.Root(brand);
                // The same look as the real Inspector: the system's look, a custom theme, then the layout's own accent.
                VeneerLayoutTheme.ApplyTo(root, layout);

                // Fills the preview's width (never wider, so text wraps and rows shrink instead of being cut off).
                root.AddToClassList("vnd-preview__root");

                var builder = new VeneerInspectorBuilder(previewSo, root, brand);
                VeneerLayoutRenderer.Build(builder, layout, previewSo);
                builder.Finish();
                previewRoot = root;
                previewHost.Add(root);
            }
            catch (Exception exception)
            {
                ClearPreview();
                previewHost.Add(VeneerStyle.Hint("The preview failed: " + exception.Message, "error", Brand));
            }
        }
    }
}
