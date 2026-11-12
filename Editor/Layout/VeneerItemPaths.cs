using System;
using System.Text;

namespace Veneer
{
    /// <summary>
    /// Pure helpers for the property paths of list elements. A <see cref="VeneerItemLayout"/> is keyed by the list's path with every array
    /// index removed, so one design serves every element: <c>groups.Array.data[3].entries.Array.data[1].prefab</c> and
    /// <c>groups[3].entries[1].prefab</c> both become <c>groups.entries.prefab</c>. No Unity calls: usable from a scratch test.
    /// </summary>
    public static class VeneerItemPaths
    {
        private const string ArraySuffix = ".Array.data";

        /// <summary>
        /// Removes every <c>[n]</c> and the <c>.Array.data</c> part Unity writes before it. A path without an index is returned as it is (no allocation).
        /// </summary>
        /// <param name="path">A property path, or an informational path such as <c>groups[].header</c>.</param>
        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path) || path.IndexOf('[') < 0)
            {
                return path;
            }

            var builder = new StringBuilder(path.Length);
            for (int i = 0; i < path.Length; i++)
            {
                char c = path[i];
                if (c != '[')
                {
                    builder.Append(c);
                    continue;
                }

                int close = path.IndexOf(']', i);
                i = close < 0 ? path.Length : close;
                int cut = builder.Length - ArraySuffix.Length;
                if (cut >= 0 && EndsWithArraySuffix(builder))
                {
                    builder.Length = cut;
                }
            }

            return builder.ToString();
        }

        /// <summary>Joins a list path and a path relative to its element with a dot; either may be empty.</summary>
        public static string Join(string parent, string relative)
        {
            if (string.IsNullOrEmpty(parent))
            {
                return relative;
            }

            return string.IsNullOrEmpty(relative) ? parent : parent + "." + relative;
        }

        /// <summary>
        /// True when <paramref name="normalizedPath"/> lies inside the element of the list <paramref name="listPath"/> (it starts with
        /// <c>listPath.</c>).
        /// </summary>
        public static bool IsInside(string normalizedPath, string listPath)
        {
            return !string.IsNullOrEmpty(normalizedPath)
                && !string.IsNullOrEmpty(listPath)
                && normalizedPath.Length > listPath.Length + 1
                && normalizedPath[listPath.Length] == '.'
                && normalizedPath.StartsWith(listPath, StringComparison.Ordinal);
        }

        private static bool EndsWithArraySuffix(StringBuilder builder)
        {
            int start = builder.Length - ArraySuffix.Length;
            for (int i = 0; i < ArraySuffix.Length; i++)
            {
                if (builder[start + i] != ArraySuffix[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
