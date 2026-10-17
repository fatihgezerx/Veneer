using UnityEditor;
using UnityEngine;

namespace Veneer
{
    /// <summary>
    /// Puts a layout's script icon on the script (the Project window, the Add Component menu, the Inspector's component title). Unity
    /// stores a script's icon in its .meta file; the layout only remembers which picture to use. Every method accepts a null layout or
    /// a layout without a script and then does nothing.
    /// </summary>
    public static class VeneerScriptIcon
    {
        /// <summary>Sets the layout's <see cref="VeneerLayout.ScriptIcon"/> as the icon of its script (a null icon clears it). Nothing is written when the script already has that icon.</summary>
        public static void Apply(VeneerLayout layout)
        {
            if (layout == null || layout.Script == null)
            {
                return;
            }

            Texture2D icon = layout.ScriptIcon;
            if (Current(layout.Script) == icon)
            {
                return;
            }

            EditorGUIUtility.SetIconForObject(layout.Script, icon);
        }

        /// <summary>Removes the custom icon from the layout's script, giving the default script icon back.</summary>
        public static void Clear(VeneerLayout layout)
        {
            if (layout == null || layout.Script == null)
            {
                return;
            }

            if (Current(layout.Script) == null)
            {
                return;
            }

            EditorGUIUtility.SetIconForObject(layout.Script, null);
        }

        /// <summary>The custom icon stored on <paramref name="script"/>, or null when it has none (or the script is null).</summary>
        public static Texture2D Current(MonoScript script)
        {
            return script == null ? null : EditorGUIUtility.GetIconForObject(script);
        }
    }
}
