using UnityEditor;

namespace Veneer
{
    /// <summary>
    /// Tells <see cref="VeneerLayoutRegistry"/> to forget its cache when a <see cref="VeneerLayout"/> asset is imported, deleted or moved.
    /// It only compares paths and asset types, so it costs nothing on imports that have no layout in them.
    /// </summary>
    internal sealed class VeneerLayoutPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            // A deleted path cannot be asked for its type any more, so any deleted .asset counts.
            if (AnyLayout(imported) || AnyLayout(moved) || AnyAsset(deleted) || AnyAsset(movedFrom))
            {
                VeneerLayoutRegistry.Invalidate();
            }
        }

        private static bool AnyLayout(string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i].EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase)
                    && AssetDatabase.GetMainAssetTypeAtPath(paths[i]) == typeof(VeneerLayout))
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
                if (paths[i].EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
