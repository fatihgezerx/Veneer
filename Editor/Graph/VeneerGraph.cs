using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// The last <see cref="Capacity"/> samples of one number in a ring allocated once. Index 0 is the oldest sample. <see cref="Latest"/>
    /// and <see cref="Peak"/> are kept up to date by <see cref="Add"/> without scanning in the common case. Feed it from a window's
    /// scheduler and hand it to a <see cref="VeneerGraph"/> or <see cref="VeneerSparkline"/>; nothing is copied or allocated per sample.
    /// </summary>
    public sealed class VeneerSeries
    {
        private readonly float[] values;
        private int next;
        private int count;

        /// <summary>Creates an empty ring of <paramref name="capacity"/> samples (at least 2).</summary>
        public VeneerSeries(int capacity)
        {
            values = new float[capacity < 2 ? 2 : capacity];
        }

        /// <summary>Most samples the ring holds.</summary>
        public int Capacity => values.Length;

        /// <summary>Samples stored right now.</summary>
        public int Count => count;

        /// <summary>The newest sample, or zero while empty.</summary>
        public float Latest { get; private set; }

        /// <summary>The largest sample still in the ring (never below zero).</summary>
        public float Peak { get; private set; }

        /// <summary>The sample at <paramref name="index"/>, 0 being the oldest and <c>Count - 1</c> the newest.</summary>
        public float this[int index]
        {
            get
            {
                int first = (next - count + values.Length) % values.Length;
                return values[(first + index) % values.Length];
            }
        }

        /// <summary>Forgets every sample.</summary>
        public void Clear()
        {
            next = 0;
            count = 0;
            Latest = 0f;
            Peak = 0f;
        }

        /// <summary>Appends a sample, dropping the oldest when the ring is full.</summary>
        public void Add(float value)
        {
            float dropped = count == values.Length ? values[next] : -1f;
            values[next] = value;
            next = (next + 1) % values.Length;
            if (count < values.Length)
            {
                count++;
            }

            Latest = value;
            if (value >= Peak)
            {
                Peak = value;
            }
            else if (dropped >= Peak)
            {
                // The old peak just left the ring; find the new one.
                float peak = 0f;
                for (int i = 0; i < count; i++)
                {
                    peak = Mathf.Max(peak, values[i]);
                }

                Peak = peak;
            }
        }
    }

    /// <summary>
    /// The colours of the kit in both editor skins, for code that cannot read USS variables (a <see cref="Painter2D"/> drawing, inline
    /// label colours). They repeat the palette of <c>Veneer.uss</c>; the accent is the default one unless a brand's is passed in.
    /// </summary>
    public sealed class VeneerPalette
    {
        private static readonly VeneerPalette Dark = new VeneerPalette(true);
        private static readonly VeneerPalette Light = new VeneerPalette(false);

        /// <summary>The default accent.</summary>
        public readonly Color Accent;

        /// <summary>The accent, mostly transparent (fills and selected rows).</summary>
        public readonly Color AccentFill;

        /// <summary>Normal text.</summary>
        public readonly Color Text;

        /// <summary>Secondary text.</summary>
        public readonly Color TextMuted;

        /// <summary>Faint grid lines.</summary>
        public readonly Color Grid;

        /// <summary>The dark inset behind graphs.</summary>
        public readonly Color Track;

        /// <summary>Good / running.</summary>
        public readonly Color Ok;

        /// <summary>Idle / asleep.</summary>
        public readonly Color Idle;

        /// <summary>Warning (amber).</summary>
        public readonly Color Warn;

        /// <summary>Warning line / border.</summary>
        public readonly Color WarnLine;

        /// <summary>Error (red).</summary>
        public readonly Color Error;

        /// <summary>Special (violet).</summary>
        public readonly Color Special;

        private VeneerPalette(bool dark)
        {
            if (dark)
            {
                Accent = Rgb(0x3FB6FF, 1f);
                AccentFill = Rgb(0x3FB6FF, 0.20f);
                Text = Rgb(0xD2D2D2, 1f);
                TextMuted = Rgb(0x9B9B9B, 1f);
                Grid = new Color(1f, 1f, 1f, 0.06f);
                Track = new Color(0f, 0f, 0f, 0.28f);
                Ok = Rgb(0x4ADE80, 1f);
                Idle = Rgb(0x7C93B8, 1f);
                Warn = Rgb(0xF5B83D, 1f);
                WarnLine = Rgb(0xF5B83D, 0.60f);
                Error = Rgb(0xF26B6B, 1f);
                Special = Rgb(0xB48CFF, 1f);
            }
            else
            {
                Accent = Rgb(0x0B86D6, 1f);
                AccentFill = Rgb(0x0B86D6, 0.20f);
                Text = Rgb(0x1A1A1A, 1f);
                TextMuted = Rgb(0x555555, 1f);
                Grid = new Color(0f, 0f, 0f, 0.08f);
                Track = new Color(0f, 0f, 0f, 0.13f);
                Ok = Rgb(0x178A45, 1f);
                Idle = Rgb(0x4C6591, 1f);
                Warn = Rgb(0x9A5F00, 1f);
                WarnLine = Rgb(0x9A5F00, 0.65f);
                Error = Rgb(0xC23030, 1f);
                Special = Rgb(0x7A4FD0, 1f);
            }
        }

        /// <summary>The palette of the current editor skin.</summary>
        public static VeneerPalette Current => EditorGUIUtility.isProSkin ? Dark : Light;

        private static Color Rgb(int hex, float alpha)
        {
            return new Color(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, alpha);
        }
    }

    /// <summary>
    /// A graph of one or two series drawn with <see cref="Painter2D"/>: the primary series as a line over a soft filled area (newest
    /// sample at the right edge), an optional second series as a thin line on its own scale, faint grid lines, and an optional dashed
    /// budget line. It reads the series it was given and never copies them, so a repaint allocates nothing. Call <see cref="Tick"/> a
    /// few times a second (it eases the scales and repaints). Give it the class <c>vn-graph</c> for a height and a dark inset.
    /// </summary>
    public sealed class VeneerGraph : VisualElement
    {
        private const float Pad = 4f;
        private const float MinPrimaryRange = 1f;
        private const float MinSecondaryRange = 8f;
        private const float DashLength = 4f;
        private const float DashPeriod = 8f;

        private readonly VeneerSeries primary;
        private readonly VeneerSeries secondary;
        private readonly int gridSamples;
        private float primaryRange = MinPrimaryRange;
        private float secondaryRange = MinSecondaryRange;
        private float budget;
        private Color? accent;

        /// <summary>Creates the graph over series that the caller keeps filling.</summary>
        /// <param name="primary">The filled line.</param>
        /// <param name="secondary">The thin second line on its own scale; may be null.</param>
        /// <param name="gridSamples">Samples between two vertical grid lines (for example 30 at 30 samples a second); 0 draws none.</param>
        public VeneerGraph(VeneerSeries primary, VeneerSeries secondary = null, int gridSamples = 30)
        {
            this.primary = primary;
            this.secondary = secondary;
            this.gridSamples = gridSamples;
            AddToClassList("vn-graph");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;
        }

        /// <summary>Uses the brand's exact accent for the primary line instead of the default one.</summary>
        public void SetAccent(Color color)
        {
            accent = color;
            MarkDirtyRepaint();
        }

        /// <summary>Sets the level the dashed line marks, in the primary series' units; zero or less hides the line.</summary>
        public void SetBudget(float level)
        {
            budget = level;
        }

        /// <summary>Forgets the eased scales (after the series were cleared) so the next samples start from the minimum scale.</summary>
        public void ResetScale()
        {
            primaryRange = MinPrimaryRange;
            secondaryRange = MinSecondaryRange;
        }

        /// <summary>
        /// Eases the scales towards what the series need (growing at once, shrinking slowly so the picture stays calm) and repaints.
        /// </summary>
        public void Tick()
        {
            float target = Mathf.Max(primary.Peak * 1.2f, MinPrimaryRange);

            // The budget line only joins the scale once the values get close to it, so a healthy graph is not squashed flat.
            if (budget > 0f && primary.Peak > budget * 0.5f)
            {
                target = Mathf.Max(target, budget * 1.15f);
            }

            primaryRange = Ease(primaryRange, target);
            if (secondary != null)
            {
                secondaryRange = Ease(secondaryRange, Mathf.Max(secondary.Peak * 1.15f, MinSecondaryRange));
            }

            MarkDirtyRepaint();
        }

        internal static float Ease(float current, float target)
        {
            if (target >= current)
            {
                return target;
            }

            float next = current + (target - current) * 0.08f;
            return next - target < 0.001f ? target : next;
        }

        // One polyline through the series, newest sample at the right edge. With closeToBottom the path becomes the closed area
        // below the line (for the soft fill).
        internal static void Trace(Painter2D painter, VeneerSeries series, float width, float height, float range, float pad, bool closeToBottom)
        {
            int count = series.Count;
            float stepX = width / (series.Capacity - 1);
            float usable = height - 2f * pad;
            float inverse = 1f / range;
            for (int i = 0; i < count; i++)
            {
                float value = series[i] * inverse;
                value = value < 0f ? 0f : (value > 1f ? 1f : value);
                var point = new Vector2(width - (count - 1 - i) * stepX, height - pad - value * usable);
                if (i == 0)
                {
                    if (closeToBottom)
                    {
                        painter.MoveTo(new Vector2(point.x, height));
                        painter.LineTo(point);
                    }
                    else
                    {
                        painter.MoveTo(point);
                    }
                }
                else
                {
                    painter.LineTo(point);
                }
            }

            if (closeToBottom)
            {
                painter.LineTo(new Vector2(width, height));
                painter.ClosePath();
            }
        }

        internal static void StrokeSeries(
            Painter2D painter, VeneerSeries series, float width, float height, float range, float pad, Color color, float lineWidth)
        {
            painter.BeginPath();
            Trace(painter, series, width, height, range, pad, false);
            painter.lineWidth = lineWidth;
            painter.strokeColor = color;
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            painter.Stroke();
        }

        internal static void FillSeries(Painter2D painter, VeneerSeries series, float width, float height, float range, float pad, Color color)
        {
            painter.BeginPath();
            Trace(painter, series, width, height, range, pad, true);
            painter.fillColor = color;
            painter.Fill();
        }

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            float width = rect.width;
            float height = rect.height;
            if (width < 8f || height < 8f)
            {
                return;
            }

            VeneerPalette palette = VeneerPalette.Current;
            Color line = accent ?? palette.Accent;
            Color fill = new Color(line.r, line.g, line.b, 0.20f);
            Painter2D painter = context.painter2D;
            DrawGrid(painter, palette, width, height);

            if (primary.Count >= 2)
            {
                FillSeries(painter, primary, width, height, primaryRange, Pad, fill);
            }

            if (secondary != null && secondary.Count >= 2)
            {
                StrokeSeries(painter, secondary, width, height, secondaryRange, Pad, palette.Ok, 1f);
            }

            if (primary.Count >= 2)
            {
                StrokeSeries(painter, primary, width, height, primaryRange, Pad, line, 1.6f);
            }

            if (budget > 0f && budget <= primaryRange)
            {
                DrawBudget(painter, palette, width, height);
            }
        }

        private void DrawGrid(Painter2D painter, VeneerPalette palette, float width, float height)
        {
            painter.BeginPath();
            for (int i = 1; i < 4; i++)
            {
                float y = height * i * 0.25f;
                painter.MoveTo(new Vector2(0f, y));
                painter.LineTo(new Vector2(width, y));
            }

            if (gridSamples > 0)
            {
                int capacity = primary.Capacity;
                float stepX = width / (capacity - 1);
                for (int sample = gridSamples; sample < capacity; sample += gridSamples)
                {
                    float x = width - sample * stepX;
                    painter.MoveTo(new Vector2(x, 0f));
                    painter.LineTo(new Vector2(x, height));
                }
            }

            painter.lineWidth = 1f;
            painter.strokeColor = palette.Grid;
            painter.Stroke();
        }

        private void DrawBudget(Painter2D painter, VeneerPalette palette, float width, float height)
        {
            float fraction = Mathf.Clamp01(budget / primaryRange);
            float y = height - Pad - fraction * (height - 2f * Pad);
            painter.BeginPath();
            for (float x = 0f; x < width; x += DashPeriod)
            {
                painter.MoveTo(new Vector2(x, y));
                painter.LineTo(new Vector2(Mathf.Min(x + DashLength, width), y));
            }

            painter.lineWidth = 1f;
            painter.strokeColor = palette.WarnLine;
            painter.Stroke();
        }
    }

    /// <summary>
    /// A small trend line with a soft fill under it, meant to sit behind the number of a statistic card (<see cref="VeneerStyle.Kpi"/>).
    /// It reads a <see cref="VeneerSeries"/> and draws it with <see cref="Painter2D"/>; the scale grows at once and shrinks slowly.
    /// </summary>
    public sealed class VeneerSparkline : VisualElement
    {
        private readonly VeneerSeries series;
        private readonly float minimumRange;
        private float range;
        private Color lineColor;
        private Color fillColor;

        /// <summary>Creates the line over <paramref name="series"/>; the scale never drops below <paramref name="minimumRange"/>.</summary>
        public VeneerSparkline(VeneerSeries series, float minimumRange)
        {
            this.series = series;
            this.minimumRange = minimumRange;
            range = minimumRange;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;
        }

        /// <summary>Sets the colour of the line; the fill under it is the same colour, mostly transparent.</summary>
        public void SetColor(Color color)
        {
            lineColor = new Color(color.r, color.g, color.b, 0.85f);
            fillColor = new Color(color.r, color.g, color.b, 0.14f);
            MarkDirtyRepaint();
        }

        /// <summary>Forgets the eased scale (after the series was cleared).</summary>
        public void ResetScale()
        {
            range = minimumRange;
        }

        /// <summary>Eases the scale and asks for a repaint. Call it a few times a second.</summary>
        public void Tick()
        {
            range = VeneerGraph.Ease(range, Mathf.Max(series.Peak * 1.15f, minimumRange));
            MarkDirtyRepaint();
        }

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            if (rect.width < 8f || rect.height < 4f || series.Count < 2)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            VeneerGraph.FillSeries(painter, series, rect.width, rect.height, range, 2f, fillColor);
            VeneerGraph.StrokeSeries(painter, series, rect.width, rect.height, range, 2f, lineColor, 1.25f);
        }
    }

    /// <summary>
    /// A number shown as text with a fixed prefix, suffix and format, formatted again only when the shown value actually changes (it is
    /// compared after rounding to the format's precision), so a label refreshed from a scheduler allocates nothing while the number is
    /// steady. Numbers are written with the invariant culture.
    /// </summary>
    public sealed class VeneerNumberText
    {
        private const float Limit = 1.0e7f;

        private readonly string prefix;
        private readonly string suffix;
        private readonly string format;
        private readonly float scale;
        private int quantised = int.MinValue;
        private string text;

        /// <summary>Creates the cache.</summary>
        /// <param name="prefix">Text before the number.</param>
        /// <param name="suffix">Text after the number.</param>
        /// <param name="format">The number format, for example <c>0.00</c> or <c>N0</c>.</param>
        /// <param name="scale">Value multiplier used to compare (10 for one decimal, 100 for two, 1 for whole numbers).</param>
        public VeneerNumberText(string prefix, string suffix, string format, float scale)
        {
            this.prefix = prefix;
            this.suffix = suffix;
            this.format = format;
            this.scale = scale;
            text = prefix + 0f.ToString(format, CultureInfo.InvariantCulture) + suffix;
        }

        /// <summary>The text for <paramref name="value"/>; the same string instance is returned while the rounded value is unchanged.</summary>
        public string Get(float value)
        {
            float clamped = value < -Limit ? -Limit : (value > Limit ? Limit : value);
            int rounded = Mathf.RoundToInt(clamped * scale);
            if (rounded != quantised)
            {
                quantised = rounded;
                text = prefix + (rounded / scale).ToString(format, CultureInfo.InvariantCulture) + suffix;
            }

            return text;
        }
    }
}
