using System;
using System.IO;
using UnityEditor;

namespace Veneer
{
    /// <summary>
    /// Import settings for the kit's glyphs and for brand pictures: default texture type with alpha as transparency, no mipmaps,
    /// uncompressed, bilinear, clamped, not readable. PNG files cannot carry these settings through a .meta file not written by Unity, so
    /// they are applied on import. It touches only the kit's own <c>Editor/Icons</c> folder (the sibling <c>Styles/Veneer.uss</c> marks
    /// it) and any PNG under a folder path that contains <c>/Editor/Brand/</c> (the convention for a system's brand logo, banner and
    /// <c>Icons</c> folder). Files elsewhere are untouched. After an import the icon caches of <see cref="VeneerStyle"/> are cleared.
    /// </summary>
    internal sealed class VeneerIconPostprocessor : AssetPostprocessor
    {
        private const string SheetRelativePath = "/Styles/Veneer.uss";
        private const string BrandMarker = "/Editor/Brand/";

        /// <summary>Changing the settings below makes Unity reimport the pictures.</summary>
        public override uint GetVersion()
        {
            return 1;
        }

        /// <summary>True when the asset path is a PNG of the kit's icon folder or of a system's <c>Editor/Brand</c> folder.</summary>
        internal static bool IsStudioPicture(string assetPath)
        {
            if (!assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (assetPath.IndexOf(BrandMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string iconsFolder = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(iconsFolder) || Path.GetFileName(iconsFolder) != "Icons")
            {
                return false;
            }

            string editorFolder = Path.GetDirectoryName(iconsFolder);
            return !string.IsNullOrEmpty(editorFolder) && File.Exists(editorFolder + SheetRelativePath);
        }

        /// <summary>Whether a picture importer already has the wanted settings.</summary>
        internal static bool IsConfigured(TextureImporter importer)
        {
            return importer.textureType == TextureImporterType.Default
                && importer.alphaIsTransparency
                && !importer.mipmapEnabled
                && !importer.isReadable
                && importer.textureCompression == TextureImporterCompression.Uncompressed
                && importer.filterMode == UnityEngine.FilterMode.Bilinear
                && importer.wrapMode == UnityEngine.TextureWrapMode.Clamp
                && importer.npotScale == TextureImporterNPOTScale.None;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            for (int i = 0; i < importedAssets.Length; i++)
            {
                if (importedAssets[i].EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    || importedAssets[i].EndsWith(".uss", StringComparison.OrdinalIgnoreCase))
                {
                    VeneerStyle.InvalidateIcons();
                    return;
                }
            }

            for (int i = 0; i < deletedAssets.Length; i++)
            {
                if (deletedAssets[i].EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    VeneerStyle.InvalidateIcons();
                    return;
                }
            }
        }

        private void OnPreprocessTexture()
        {
            if (!IsStudioPicture(assetPath))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
        }
    }
}
