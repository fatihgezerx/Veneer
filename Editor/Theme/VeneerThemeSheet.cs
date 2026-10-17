using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Veneer
{
    /// <summary>
    /// The style-sheet side of a theme. USS variables cannot be set from C#, so a theme that overrides colours, corner roundness or
    /// spacing gets a small generated companion sheet (<c>&lt;theme&gt;.uss</c>, next to the theme asset) whose rules name the theme by a
    /// class made from its GUID (<see cref="RootClass"/>); <see cref="VeneerStyle.ApplyBrand"/> adds the class and the sheet to every
    /// root. The sheet holds ONLY the variables the theme overrides. The tab style, the motion setting and the logo and banner pictures need no sheet (the first two are classes on the root).
    /// <para>
    /// Numbers (pixels). The Default column equals the kit's own values in <c>Veneer.uss</c>; the sheet writes <c>round(Default * scale)</c>,
    /// where the scale is the theme's <c>RadiusScale</c> (0..2) for the radius variables and <c>DensityScale</c> (0.6..1.5) for the density
    /// variables, so scale 1 gives exactly the Default column:
    /// </para>
    /// <code>
    /// Radius variable          Default  formula                    used for
    /// --vn-radius-sm             4      round(4 * RadiusScale)      list rows
    /// --vn-radius-md             5      round(5 * RadiusScale)      buttons, tabs, hints, fields, thumbnails, graphs
    /// --vn-radius-lg             6      round(6 * RadiusScale)      cards, preset cards, KPI cards, primary buttons
    /// --vn-radius-xl             8      round(8 * RadiusScale)      banners
    /// --vn-radius-seg            7      round(7 * RadiusScale)      segmented control box (it wraps md buttons)
    /// --vn-radius-pill-sm        8      min(round(8 * RadiusScale), 8)    state pills
    /// --vn-radius-pill-md       11      min(round(11 * RadiusScale), 11)  search fields
    /// --vn-radius-pill-lg       12      min(round(12 * RadiusScale), 12)  toggle chips, pill tabs
    ///
    /// Density variable     Default  formula                          minimum  used for
    /// --vn-gap                 6    round(6 * DensityScale)              2      block margin, row spacing, list margins
    /// --vn-gap-neg            -6    -(--vn-gap)                                 cancels the last child's gap of a row
    /// --vn-gap-sm              4    round(4 * DensityScale)              2      field row / slider row / list header bottom margin
    /// --vn-pad-x              10    round(10 * DensityScale)             4      card, preset, KPI, button and tab side padding
    /// --vn-pad-y               8    round(8 * DensityScale)              3      card, preset, KPI top and bottom padding
    /// --vn-pad-b               4    round(4 * DensityScale)              2      card bottom padding
    /// --vn-row-h              22    round(22 * DensityScale)            16      field row, slider row, search field, live strip height
    /// --vn-ctl-h              24    round(24 * DensityScale)            18      buttons, chips, segmented buttons, icon buttons, pill tabs
    /// --vn-ctl-h-lg           30    round(30 * DensityScale)            22      primary button
    /// --vn-tab-h              28    round(28 * DensityScale)            20      tabs
    /// </code>
    /// Rounding is half away from zero. The pill radii never grow above their Default (they are already fully round, so a bigger radius
    /// would change nothing); below scale 1 they shrink with the other radii, and at 0 everything is square. The minimums keep rows and
    /// controls at least as tall as a stock Unity field at the tightest density.
    /// </summary>
    public static class VeneerThemeSheet
    {
        private const string ClassPrefix = "vnt-";
        private const int HexLength = 8;

        // The first characters of every generated sheet: only a file that starts with them is ever overwritten or deleted.
        private const string Marker = "/* Written by the Veneer Theme Editor";

        // The kit's own radii, in the order of RadiusNames (the Default column of the table above).
        private static readonly string[] RadiusNames =
        {
            "--vn-radius-sm", "--vn-radius-md", "--vn-radius-lg", "--vn-radius-xl", "--vn-radius-seg",
            "--vn-radius-pill-sm", "--vn-radius-pill-md", "--vn-radius-pill-lg",
        };

        private static readonly int[] RadiusDefaults = { 4, 5, 6, 8, 7, 8, 11, 12 };

        // Radii from this index on are the pill radii: already fully round, so they never grow above their default.
        private const int FirstPillRadius = 5;

        // The kit's own spacing, in the order of DensityNames. --vn-gap-neg is not scaled itself: it is always the negative of --vn-gap.
        private static readonly string[] DensityNames =
        {
            "--vn-gap", "--vn-gap-neg", "--vn-gap-sm", "--vn-pad-x", "--vn-pad-y", "--vn-pad-b",
            "--vn-row-h", "--vn-ctl-h", "--vn-ctl-h-lg", "--vn-tab-h",
        };

        private static readonly int[] DensityDefaults = { 6, -6, 4, 10, 8, 4, 22, 24, 30, 28 };
        private static readonly int[] DensityMinimums = { 2, -2, 2, 4, 3, 2, 16, 18, 22, 20 };

        private const int GapIndex = 0;
        private const int GapNegIndex = 1;

        // What was last written per sheet path, so a repeated call compares two strings and touches no file.
        private static readonly Dictionary<string, string> Written = new Dictionary<string, string>(8);

        /// <summary>The asset path of the theme's companion style sheet (the theme's path with the extension <c>.uss</c>); null when the theme is not an asset.</summary>
        public static string SheetPath(VeneerTheme theme)
        {
            string assetPath = theme == null ? null : AssetDatabase.GetAssetPath(theme);
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            return assetPath.Substring(0, assetPath.Length - Path.GetExtension(assetPath).Length) + ".uss";
        }

        /// <summary>
        /// The USS class that selects this theme's variables, <c>vnt-</c> plus the first 8 hex characters of the theme's asset GUID; null
        /// when the theme is not saved as an asset yet.
        /// </summary>
        public static string RootClass(VeneerTheme theme)
        {
            if (theme == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(theme, out string guid, out long _) || guid.Length < HexLength)
            {
                return null;
            }

            return ClassPrefix + guid.Substring(0, HexLength);
        }

        /// <summary>True when the theme overrides something that lives in the style sheet (colours, roundness, spacing); false means it needs no sheet.</summary>
        public static bool HasContent(VeneerTheme theme)
        {
            return theme != null
                && (theme.OverrideAccent || theme.OverrideCardColor || theme.OverrideCardBorder || theme.OverrideText
                    || theme.OverrideTextMuted || theme.OverrideRadius || theme.OverrideDensity);
        }

        /// <summary>
        /// Writes (or updates) the companion style sheet of <paramref name="theme"/> next to its asset and imports it. The file is only
        /// touched when its text would change, so calling this often is harmless. A theme that overrides nothing gets no sheet: a sheet this
        /// tool wrote earlier is deleted. Does nothing for a theme that is not an asset yet, and never overwrites a file this tool did not write.
        /// </summary>
        public static void Write(VeneerTheme theme)
        {
            string sheetPath = SheetPath(theme);
            string className = RootClass(theme);
            if (sheetPath == null || className == null)
            {
                return;
            }

            string fullPath = FullPath(sheetPath);
            string text = HasContent(theme) ? BuildText(theme, className) : null;
            try
            {
                bool exists = File.Exists(fullPath);
                string current = exists ? File.ReadAllText(fullPath) : null;
                if (exists && !current.StartsWith(Marker, StringComparison.Ordinal))
                {
                    Debug.LogWarning("Veneer: " + sheetPath + " exists and was not written by the Theme Editor, so the theme's style sheet was not written. Rename or delete that file.");
                    return;
                }

                if (text == null)
                {
                    Written.Remove(sheetPath);
                    if (exists)
                    {
                        AssetDatabase.DeleteAsset(sheetPath);
                    }

                    return;
                }

                if (exists && string.Equals(current, text, StringComparison.Ordinal))
                {
                    Written[sheetPath] = text;
                    return;
                }

                // Write beside the target and swap, so a failure never leaves a half-written sheet. Unity ignores .tmp files.
                string temp = fullPath + ".tmp";
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                if (exists)
                {
                    File.Replace(temp, fullPath, null);
                }
                else
                {
                    File.Move(temp, fullPath);
                }

                Written[sheetPath] = text;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Veneer: could not write the style sheet " + sheetPath + ": " + exception.Message);
                return;
            }

            AssetDatabase.ImportAsset(sheetPath);
        }

        private static string BuildText(VeneerTheme theme, string className)
        {
            var sb = new StringBuilder(768);
            sb.Append(Marker).Append(" from the theme asset. Edit the theme, not this file. */\n");

            // Dark skin and shared rule: colours, then shape. Shape variables do not differ per skin, so they are only here.
            sb.Append(".vn-root.").Append(className).Append(" {\n");
            AppendColors(sb, theme);
            if (theme.OverrideRadius)
            {
                AppendRadii(sb, VeneerThemeEffective.ClampRadiusScale(theme.RadiusScale));
            }

            if (theme.OverrideDensity)
            {
                AppendDensity(sb, VeneerThemeEffective.ClampDensityScale(theme.DensityScale));
            }

            sb.Append("}\n");

            // The light skin redefines the colours with a rule of the same weight, so it needs a heavier selector to lose to this one.
            if (theme.OverrideAccent || theme.OverrideCardColor || theme.OverrideCardBorder || theme.OverrideText || theme.OverrideTextMuted)
            {
                sb.Append(".vn-root.vn-root--light.").Append(className).Append(" {\n");
                AppendColors(sb, theme, light: true);
                sb.Append("}\n");
            }

            return sb.ToString();
        }

        private static void AppendColors(StringBuilder sb, VeneerTheme theme, bool light = false)
        {
            if (theme.OverrideAccent)
            {
                Color32 c = theme.Accent;
                AppendVariable(sb, "--vn-accent", Rgba(c, theme.Accent.a));
                // The alphas are the kit's own for each skin (see Veneer.uss).
                AppendVariable(sb, "--vn-accent-soft", Rgba(c, light ? 0.14f : 0.16f));
                AppendVariable(sb, "--vn-accent-line", Rgba(c, light ? 0.60f : 0.55f));
                AppendVariable(sb, "--vn-pressed", Rgba(c, light ? 0.22f : 0.24f));
            }

            if (theme.OverrideCardColor)
            {
                AppendVariable(sb, "--vn-card", Rgba(theme.CardColor));
            }

            if (theme.OverrideCardBorder)
            {
                AppendVariable(sb, "--vn-card-border", Rgba(theme.CardBorder));
            }

            if (theme.OverrideText)
            {
                AppendVariable(sb, "--vn-text", Rgba(theme.Text));
            }

            if (theme.OverrideTextMuted)
            {
                AppendVariable(sb, "--vn-text-muted", Rgba(theme.TextMuted));
            }
        }

        private static void AppendRadii(StringBuilder sb, float scale)
        {
            for (int i = 0; i < RadiusNames.Length; i++)
            {
                int value = Scaled(RadiusDefaults[i], scale);
                if (i >= FirstPillRadius && value > RadiusDefaults[i])
                {
                    value = RadiusDefaults[i];
                }

                AppendPixels(sb, RadiusNames[i], value);
            }
        }

        private static void AppendDensity(StringBuilder sb, float scale)
        {
            int gap = 0;
            for (int i = 0; i < DensityNames.Length; i++)
            {
                int value;
                if (i == GapNegIndex)
                {
                    value = -gap;
                }
                else
                {
                    value = Math.Max(Scaled(DensityDefaults[i], scale), DensityMinimums[i]);
                    if (i == GapIndex)
                    {
                        gap = value;
                    }
                }

                AppendPixels(sb, DensityNames[i], value);
            }
        }

        // round(defaultValue * scale), half away from zero; the small epsilon keeps a float scale such as 0.7 from landing just under .5.
        private static int Scaled(int defaultValue, float scale)
        {
            return (int)Math.Floor(defaultValue * (double)scale + 0.5 + 1e-4);
        }

        private static void AppendPixels(StringBuilder sb, string name, int value)
        {
            AppendVariable(sb, name, value.ToString(CultureInfo.InvariantCulture) + "px");
        }

        private static void AppendVariable(StringBuilder sb, string name, string value)
        {
            sb.Append("    ").Append(name).Append(": ").Append(value).Append(";\n");
        }

        private static string Rgba(Color color)
        {
            return Rgba((Color32)color, color.a);
        }

        private static string Rgba(Color32 c, float alpha)
        {
            return "rgba(" + c.r + ", " + c.g + ", " + c.b + ", " + alpha.ToString("0.###", CultureInfo.InvariantCulture) + ")";
        }

        private static string FullPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath);
        }
    }
}
