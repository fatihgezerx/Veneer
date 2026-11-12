using System.IO;
using UnityEditor;

namespace Veneer
{
    /// <summary>
    /// Where the assets the user makes with the kit live: <c>&lt;Veneer folder&gt;/Data/Layouts</c> and <c>&lt;Veneer folder&gt;/Data/Themes</c>.
    /// The Veneer folder is found through the kit's own location (<see cref="VeneerStyle.KitFolder"/>), so the kit can sit anywhere in the
    /// project and its data travels with it.
    /// </summary>
    public static class VeneerDataFolders
    {
        private const string FallbackRoot = "Assets/Veneer";
        private const string DataName = "Data";
        private const string LayoutsName = "Layouts";
        private const string ThemesName = "Themes";

        /// <summary>The folder for the layout assets made by the Designer, for example <c>Assets/Scripts/Veneer/Data/Layouts</c>.</summary>
        public static string Layouts => Root + "/" + DataName + "/" + LayoutsName;

        /// <summary>The folder for the custom theme assets made by the Theme Editor, for example <c>Assets/Scripts/Veneer/Data/Themes</c>.</summary>
        public static string Themes => Root + "/" + DataName + "/" + ThemesName;

        private static string Root
        {
            get
            {
                string kit = VeneerStyle.KitFolder;
                if (string.IsNullOrEmpty(kit))
                {
                    return FallbackRoot;
                }

                string parent = Path.GetDirectoryName(kit);
                return string.IsNullOrEmpty(parent) ? FallbackRoot : parent.Replace('\\', '/');
            }
        }

        /// <summary>Makes <see cref="Layouts"/> (and the folders above it) when missing and returns it.</summary>
        public static string EnsureLayouts()
        {
            return Ensure(Layouts);
        }

        /// <summary>Makes <see cref="Themes"/> (and the folders above it) when missing and returns it.</summary>
        public static string EnsureThemes()
        {
            return Ensure(Themes);
        }

        private static string Ensure(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return path;
            }

            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }

            return path;
        }
    }
}
