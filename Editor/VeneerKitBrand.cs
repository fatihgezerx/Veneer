using UnityEngine;

namespace Veneer
{
    /// <summary>
    /// The identity of Veneer itself: its logo (three stacked layers, in <c>Editor/Brand</c>) and the kit's default accent. The kit's own windows
    /// (Inspector Designer, Theme Editor) and its Project Settings page use it, so their tabs and title bars carry the Veneer icon.
    /// </summary>
    public static class VeneerKitBrand
    {
        /// <summary>The cached Veneer brand; its picture loads lazily from the asset path and survives reimports.</summary>
        public static readonly VeneerBrand Value = VeneerBrand.FromAssets(
            name: "Veneer",
            subtitle: "Inspector styling",
            accent: VeneerBrand.DefaultAccent,
            logoPath: "Assets/Scripts/Veneer/Editor/Brand/logo.png",
            bannerPath: null);
    }
}
