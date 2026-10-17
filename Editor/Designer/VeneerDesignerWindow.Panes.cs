using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    // The pane frame: Fields (fixed width) | Layout (takes the rest) | Properties (fixed width), and the tree / preview split inside the
    // centre pane. No TwoPaneSplitView: nesting it relied on its own fixed-pane bookkeeping, and the old panes also carried flex-grow 1,
    // so the window's width was shared proportionally. Here the side widths are plain inline sizes that a VeneerDesignerSplitter changes,
    // and flex does the rest (side panes have flex-grow 0 and only shrink if the window is below its minimum; the centre pane has
    // flex-grow 1 and flex-basis 0, so it alone takes extra width).
    public sealed partial class VeneerDesignerWindow
    {
        private static readonly Vector2 MinWindowSize = new Vector2(960f, 560f);

        private const float FieldsDefaultWidth = 260f;
        private const float FieldsMinWidth = 200f;
        private const float FieldsMaxWidth = 340f;
        private const float PropsDefaultWidth = 340f;
        private const float PropsMinWidth = 300f;
        private const float PropsMaxWidth = 480f;
        private const float CentreMinWidth = 360f;
        private const float TreeMinHeight = 120f;
        private const float PreviewMinHeight = 120f;

        private const string FieldsWidthKey = "Veneer.Designer.FieldsWidth";
        private const string PropsWidthKey = "Veneer.Designer.PropsWidth";
        private const string PreviewHeightKey = "Veneer.Designer.PreviewHeight";

        private VisualElement fieldsPane;
        private VisualElement centrePane;
        private VisualElement propsPane;
        private VisualElement splitArea;
        private VisualElement previewGroup;
        private float dragStartSize;

        private void BuildMainPanel(VisualElement content)
        {
            mainPanel = new VisualElement();
            mainPanel.AddToClassList("vnd-main");
            VeneerStyle.SetDisplay(mainPanel, false);

            fieldsPane = BuildFieldsPane();
            ConfigureSidePane(fieldsPane, FieldsWidthKey, FieldsDefaultWidth, FieldsMinWidth, FieldsMaxWidth);
            centrePane = BuildLayoutPane();
            centrePane.AddToClassList("vnd-pane--centre");
            centrePane.style.minWidth = CentreMinWidth;
            propsPane = BuildPropertiesPane();
            ConfigureSidePane(propsPane, PropsWidthKey, PropsDefaultWidth, PropsMinWidth, PropsMaxWidth);

            mainPanel.Add(fieldsPane);
            mainPanel.Add(new VeneerDesignerSplitter(true, BeginFieldsDrag, DragFieldsWidth, ResetFieldsWidth));
            mainPanel.Add(centrePane);
            mainPanel.Add(new VeneerDesignerSplitter(true, BeginPropsDrag, DragPropsWidth, ResetPropsWidth));
            mainPanel.Add(propsPane);
            content.Add(mainPanel);
        }

        // A side pane has an inline width (the remembered one) and hard limits; flex-shrink lets it give way only when the window is tiny.
        private static void ConfigureSidePane(VisualElement pane, string key, float defaultWidth, float minWidth, float maxWidth)
        {
            pane.AddToClassList("vnd-pane--side");
            pane.style.minWidth = minWidth;
            pane.style.maxWidth = maxWidth;
            pane.style.width = Mathf.Clamp(SessionState.GetFloat(key, defaultWidth), minWidth, maxWidth);
        }

        // ------------------------------------------------------------------ the two column handles

        private void BeginFieldsDrag()
        {
            dragStartSize = CurrentWidth(fieldsPane, FieldsDefaultWidth);
        }

        private void DragFieldsWidth(float delta)
        {
            ResizeSide(fieldsPane, propsPane, FieldsWidthKey, dragStartSize + delta, FieldsMinWidth, FieldsMaxWidth);
        }

        private void ResetFieldsWidth()
        {
            fieldsPane.style.width = FieldsDefaultWidth;
            SessionState.SetFloat(FieldsWidthKey, FieldsDefaultWidth);
        }

        private void BeginPropsDrag()
        {
            dragStartSize = CurrentWidth(propsPane, PropsDefaultWidth);
        }

        // The handle sits left of the Properties pane, so dragging left (negative) makes the pane wider.
        private void DragPropsWidth(float delta)
        {
            ResizeSide(propsPane, fieldsPane, PropsWidthKey, dragStartSize - delta, PropsMinWidth, PropsMaxWidth);
        }

        private void ResetPropsWidth()
        {
            propsPane.style.width = PropsDefaultWidth;
            SessionState.SetFloat(PropsWidthKey, PropsDefaultWidth);
        }

        private static float CurrentWidth(VisualElement pane, float fallback)
        {
            float width = pane.resolvedStyle.width;
            return float.IsNaN(width) || width <= 0f ? fallback : width;
        }

        // Sizes one side pane inside its own limits and never lets the centre pane drop under its minimum.
        private void ResizeSide(VisualElement pane, VisualElement other, string key, float wanted, float minWidth, float maxWidth)
        {
            float room = mainPanel.contentRect.width - CurrentWidth(other, 0f) - 2f * VeneerDesignerSplitter.Thickness - CentreMinWidth;
            float width = Mathf.Clamp(wanted, minWidth, Mathf.Max(minWidth, Mathf.Min(maxWidth, room)));
            pane.style.width = width;
            SessionState.SetFloat(key, width);
        }

        // ------------------------------------------------------------------ the tree / preview handle

        // The preview group starts proportional to the tree (see .vnd-tree-host and .vnd-preview-group in Designer.uss); a drag switches it
        // to a pixel height, so growing the window then grows the tree only.
        private void RestorePreviewHeight()
        {
            float stored = SessionState.GetFloat(PreviewHeightKey, 0f);
            if (stored > 0f)
            {
                ApplyPreviewHeight(stored);
            }
        }

        private void ApplyPreviewHeight(float height)
        {
            previewGroup.style.flexGrow = 0f;
            previewGroup.style.flexBasis = height;
        }

        private void BeginPreviewDrag()
        {
            float height = previewGroup.resolvedStyle.height;
            dragStartSize = float.IsNaN(height) || height <= 0f ? PreviewMinHeight + VeneerDesignerSplitter.Thickness : height;
        }

        // The handle is the top edge of the preview group, so dragging up (negative) makes the preview taller.
        private void DragPreview(float delta)
        {
            float minHeight = PreviewMinHeight + VeneerDesignerSplitter.Thickness;
            float room = splitArea.contentRect.height - TreeMinHeight;
            float height = Mathf.Clamp(dragStartSize - delta, minHeight, Mathf.Max(minHeight, room));
            ApplyPreviewHeight(height);
            SessionState.SetFloat(PreviewHeightKey, height);
        }

        private void ResetPreview()
        {
            previewGroup.style.flexGrow = StyleKeyword.Null;
            previewGroup.style.flexBasis = StyleKeyword.Null;
            SessionState.SetFloat(PreviewHeightKey, 0f);
        }
    }
}
