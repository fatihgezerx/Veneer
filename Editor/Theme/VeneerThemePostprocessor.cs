using System;
using UnityEditor;

namespace Veneer
{
    /// <summary>
    /// Tells <see cref="VeneerThemeRegistry"/> to forget its caches when a <see cref="VeneerTheme"/> asset is imported, deleted or moved.
    /// It only compares paths and asset types, so it costs nothing on imports that have no theme in them. Nothing needs
    /// <c>[InitializeOnLoad]</c>: the registry builds lazily and a domain reload starts it empty.
    /// </summary>
    internal sealed class VeneerThemePostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            // A deleted path cannot be asked for its type any more, so any deleted .asset counts.
            if (AnyTheme(imported) || AnyTheme(moved) || AnyAsset(deleted) || AnyAsset(movedFrom))
            {
                VeneerThemeRegistry.Invalidate();
            }
        }

        private static bool AnyTheme(string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i].EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                    && AssetDatabase.GetMainAssetTypeAtPath(paths[i]) == typeof(VeneerTheme))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AnyAsset(string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i].EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
