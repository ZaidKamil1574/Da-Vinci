using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// A scrolling line chart of how the Da Vinci joints are moving: speed against time, or
    /// distance travelled against time, one coloured trace per joint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The chart is rasterised into a <see cref="Texture2D"/> and shown on a <see cref="RawImage"/>.
    /// The alternative — a mesh-building <c>Graphic</c> per trace — means a custom vertex helper and
    /// a rebuild of the UI batch on every sample. Writing pixels into one texture costs the same
    /// whether there are two traces or ten, and the sample rate is a tenth of the frame rate, so
    /// the raster is redrawn far less often than the UI would be re-laid-out.
    /// </para>
    /// <para>
    /// Samples live in a ring buffer sized to <see cref="m_WindowSeconds"/>. A list that grew would
    /// be an unbounded allocation over a long session, and the chart can only ever show the window
    /// anyway.
    /// </para>
    /// <para>
    /// Colours are not chosen here. They come from <see cref="DaVinciJointPalette"/>, the same table
    /// the angle wedges and force arrows use, so a trace and the joint it describes are the same
    /// colour in the graph and out in the world.
    /// </para>
    /// <para>
    /// While <see cref="DaVinciPauseControl"/> holds the machine frozen the graph stops sampling
    /// rather than drawing a flat line. A flat line says "this joint stopped moving", which is a
    /// different statement from "nothing was recorded here", and the two should not look alike.
    /// </para>
    /// </remarks>
    public class DaVinciTelemetryGraph : MonoBehaviour
    {
        /// <summary>What a trace plots.</summary>
        public enum Channel
        {
            /// <summary>World speed of the joint, in metres per second.</summary>
            Speed,

            /// <summary>Total path length walked since the graph started, in metres.</summary>
            Distance,
        }

        /// <summary>One joint's trace.</summary>
        [Serializable]
        public class Series
        {
            [Tooltip("Name shown in the legend. Defaults to the joint's own name.")]
            public string label;

            [Tooltip("The joint to plot.")]
            public Transform joint;

            [Tooltip("Trace colour. Left at clear, the shared joint palette decides.")]
            public Color colour = Color.clear;

            internal float[] samples;
            internal Vector3 previousPosition;
            internal float travelled;
            internal bool primed;
        }

        [Header("What to plot")]
        [SerializeField, Tooltip("Speed against time, or distance travelled against time.")]
        Channel m_Channel = Channel.Speed;

        [SerializeField, Tooltip("One entry per joint.")]
        List<Series> m_Series = new List<Series>();

        [Header("Axes")]
        [SerializeField, Tooltip("How much history the chart shows, in seconds.")]
        float m_WindowSeconds = 10f;

        [SerializeField, Tooltip("Samples per second. The chart's horizontal resolution is this times the window.")]
        float m_SampleRate = 20f;

        [SerializeField, Tooltip("Starting top of the vertical axis. The axis grows past this when a trace exceeds it.")]
        float m_BaseRange = 0.25f;

        [SerializeField, Tooltip("Let the vertical axis grow to fit the tallest trace on screen.")]
        bool m_AutoRange = true;

        [Header("Appearance")]
        [SerializeField, Tooltip("Chart height in pixels. Width follows from the window and the sample rate.")]
        int m_Height = 150;

        [SerializeField, Tooltip("Chart background.")]
        Color m_Background = new Color(0.05f, 0.07f, 0.10f, 0.95f);

        [SerializeField, Tooltip("Grid line colour.")]
        Color m_Grid = new Color(1f, 1f, 1f, 0.13f);

        [SerializeField, Tooltip("Horizontal grid divisions.")]
        int m_GridRows = 4;

        [Header("Wiring")]
        [SerializeField, Tooltip("Where the chart is drawn.")]
        RawImage m_Image;

        [SerializeField, Tooltip("Label showing the current top of the vertical axis.")]
        TMP_Text m_ScaleLabel;

        Texture2D m_Texture;
        Color32[] m_Pixels;
        int m_Width;
        int m_Head;
        int m_Filled;
        float m_NextSample;
        float m_Range;
        string m_ScaleFormat;

        /// <summary>The traces this graph plots.</summary>
        public IReadOnlyList<Series> series => m_Series;

        /// <summary>What the graph plots.</summary>
        public Channel channel => m_Channel;

        void OnEnable()
        {
            m_Range = m_BaseRange;
            m_ScaleFormat = m_Channel == Channel.Speed ? "{0:0.00} m/s full scale" : "{0:0.00} m full scale";
            Allocate();
        }

        void OnDisable()
        {
            if (m_Texture != null)
                Destroy(m_Texture);

            m_Texture = null;
            m_Pixels = null;
        }

        void Update()
        {
            if (m_Texture == null)
                return;

            // Frozen means "no data", not "zero". Sampling through a pause would draw a flat run
            // that reads as the joint having stopped of its own accord.
            if (DaVinciPauseControl.IsPaused || Time.time < m_NextSample)
                return;

            m_NextSample = Time.time + 1f / Mathf.Max(1f, m_SampleRate);

            Sample();
            Rasterise();
        }

        /// <summary>Empties the chart and restarts the distance totals from zero.</summary>
        public void Clear()
        {
            m_Head = 0;
            m_Filled = 0;
            m_Range = m_BaseRange;

            for (var i = 0; i < m_Series.Count; i++)
            {
                m_Series[i].travelled = 0f;
                m_Series[i].primed = false;

                if (m_Series[i].samples != null)
                    Array.Clear(m_Series[i].samples, 0, m_Series[i].samples.Length);
            }

            Rasterise();
        }

        void Allocate()
        {
            m_Width = Mathf.Clamp(Mathf.RoundToInt(m_WindowSeconds * m_SampleRate), 32, 1024);
            m_Height = Mathf.Clamp(m_Height, 32, 512);

            m_Texture = new Texture2D(m_Width, m_Height, TextureFormat.RGBA32, false)
            {
                name = $"{name} Chart",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };

            m_Pixels = new Color32[m_Width * m_Height];

            for (var i = 0; i < m_Series.Count; i++)
            {
                m_Series[i].samples = new float[m_Width];
                m_Series[i].primed = false;
            }

            if (m_Image != null)
                m_Image.texture = m_Texture;

            m_Head = 0;
            m_Filled = 0;
            Rasterise();
        }

        void Sample()
        {
            var dt = 1f / Mathf.Max(1f, m_SampleRate);

            for (var i = 0; i < m_Series.Count; i++)
            {
                var s = m_Series[i];
                if (s.samples == null)
                    continue;

                var value = 0f;

                if (s.joint != null)
                {
                    var position = s.joint.position;

                    if (s.primed)
                    {
                        var step = Vector3.Distance(position, s.previousPosition);
                        s.travelled += step;
                        value = m_Channel == Channel.Speed ? step / dt : s.travelled;
                    }
                    else
                    {
                        s.primed = true;
                    }

                    s.previousPosition = position;
                }

                s.samples[m_Head] = value;
            }

            m_Head = (m_Head + 1) % m_Width;
            m_Filled = Mathf.Min(m_Filled + 1, m_Width);
        }

        void Rasterise()
        {
            if (m_Pixels == null)
                return;

            Fill(m_Background);
            DrawGrid();

            var range = m_AutoRange ? Mathf.Max(m_BaseRange, PeakOnScreen() * 1.15f) : m_BaseRange;
            m_Range = Mathf.Lerp(m_Range, range, 0.25f);

            for (var i = 0; i < m_Series.Count; i++)
                DrawSeries(m_Series[i]);

            m_Texture.SetPixels32(m_Pixels);
            m_Texture.Apply(false);

            if (m_ScaleLabel != null)
                m_ScaleLabel.SetText(m_ScaleFormat, m_Range);
        }

        float PeakOnScreen()
        {
            var peak = 0f;
            for (var i = 0; i < m_Series.Count; i++)
            {
                var s = m_Series[i];
                if (s.samples == null)
                    continue;

                for (var x = 0; x < m_Filled; x++)
                    peak = Mathf.Max(peak, s.samples[x]);
            }

            return peak;
        }

        void DrawSeries(Series s)
        {
            if (s.samples == null || m_Filled < 2)
                return;

            var colour = s.colour.a > 0f
                ? s.colour
                : DaVinciJointPalette.For(s.label ?? (s.joint != null ? s.joint.name : null));

            var previousY = -1;

            for (var x = 0; x < m_Filled; x++)
            {
                // Oldest sample on the left. The ring's head is the next slot to write, so the
                // oldest live sample sits exactly there once the buffer has wrapped.
                var index = m_Filled < m_Width ? x : (m_Head + x) % m_Width;
                var y = Mathf.Clamp(
                    Mathf.RoundToInt(s.samples[index] / Mathf.Max(1e-4f, m_Range) * (m_Height - 3)),
                    0,
                    m_Height - 2);

                if (previousY >= 0)
                    DrawColumn(x, previousY, y, colour);
                else
                    Plot(x, y, colour);

                previousY = y;
            }
        }

        /// <summary>
        /// Joins two adjacent samples with a vertical run.
        /// </summary>
        /// <remarks>
        /// One sample per pixel column, so the only gap a trace can have is vertical. Filling that
        /// column is all a line-drawing routine would do here, without the general case.
        /// </remarks>
        void DrawColumn(int x, int fromY, int toY, Color colour)
        {
            var low = Mathf.Min(fromY, toY);
            var high = Mathf.Max(fromY, toY);

            for (var y = low; y <= high; y++)
            {
                Plot(x, y, colour);
                Plot(x, y + 1, colour);
            }
        }

        void DrawGrid()
        {
            for (var row = 1; row < m_GridRows; row++)
            {
                var y = Mathf.RoundToInt(row / (float)m_GridRows * (m_Height - 1));
                for (var x = 0; x < m_Width; x++)
                    Plot(x, y, m_Grid);
            }
        }

        void Fill(Color colour)
        {
            var packed = (Color32)colour;
            for (var i = 0; i < m_Pixels.Length; i++)
                m_Pixels[i] = packed;
        }

        void Plot(int x, int y, Color colour)
        {
            if (x < 0 || x >= m_Width || y < 0 || y >= m_Height)
                return;

            m_Pixels[y * m_Width + x] = colour;
        }
    }
}
