using UnityEngine;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// A camera riding on the instrument tip, rendering the endoscopic view into a
    /// <see cref="RenderTexture"/> for the console monitor to display.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The camera is parented to the tip rather than following it from script. The tip is already
    /// driven by the arm's IK chain, so parenting inherits that motion exactly, with no ordering
    /// question about whether the follow ran before or after the solve.
    /// </para>
    /// <para>
    /// A real endoscope sits centimetres from tissue, so the near plane has to be far closer than a
    /// scene camera's. The default 0.3 m would clip away everything the instrument is actually
    /// working on.
    /// </para>
    /// <para>
    /// The monitor must be excluded from this camera's culling mask. A screen showing a feed that
    /// includes the screen recurses, and the picture degenerates into a tunnel of itself. Keeping
    /// the monitor on a layer this camera does not render is what prevents that.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Camera))]
    public class EndoscopeCamera : MonoBehaviour
    {
        [SerializeField, Tooltip("Texture the view renders into. The console monitor displays this same asset.")]
        RenderTexture m_Feed;

        [SerializeField, Tooltip("Horizontal-ish field of view. Real endoscopes are wide, around 70 degrees.")]
        [Range(20f, 120f)]
        float m_FieldOfView = 70f;

        [SerializeField, Tooltip("Near clip, in metres. Has to be small: the lens works centimetres from tissue.")]
        float m_NearClip = 0.005f;

        [SerializeField, Tooltip("Far clip, in metres. The view is inside a body cavity, so this can stay short.")]
        float m_FarClip = 20f;

        [SerializeField, Tooltip("Layers the endoscope can see. The monitor's own layer must stay out of this, or the feed recurses into itself.")]
        LayerMask m_VisibleLayers = ~0;

        [SerializeField, Tooltip("Feed refresh rate. Zero renders every frame; a lower rate costs less, which matters because this is a second full render on top of both eyes.")]
        [Range(0f, 90f)]
        float m_RefreshRate = 30f;

        Camera m_Camera;
        float m_Timer;

        /// <summary>The texture this camera renders into.</summary>
        public RenderTexture feed => m_Feed;

        void Awake()
        {
            m_Camera = GetComponent<Camera>();
            Apply();
        }

        void OnValidate()
        {
            if (m_Camera == null)
                m_Camera = GetComponent<Camera>();

            Apply();
        }

        void Update()
        {
            if (m_Camera == null)
                return;

            // Rendering is gated by toggling the camera rather than by calling Render() on a
            // disabled one: under a scriptable pipeline only the former is a supported path. On the
            // frames it stays off the texture simply holds its last image, which is what a paused
            // feed should look like anyway.
            if (m_RefreshRate <= 0f)
            {
                m_Camera.enabled = true;
                return;
            }

            var interval = 1f / m_RefreshRate;
            m_Timer += Time.unscaledDeltaTime;

            if (m_Timer >= interval)
            {
                m_Timer -= interval;
                m_Camera.enabled = true;
            }
            else
            {
                m_Camera.enabled = false;
            }
        }

        /// <summary>Pushes the serialized settings onto the camera.</summary>
        public void Apply()
        {
            if (m_Camera == null)
                return;

            m_Camera.targetTexture = m_Feed;
            m_Camera.fieldOfView = m_FieldOfView;
            m_Camera.nearClipPlane = Mathf.Max(0.001f, m_NearClip);
            m_Camera.farClipPlane = Mathf.Max(m_NearClip + 0.01f, m_FarClip);
            m_Camera.cullingMask = m_VisibleLayers;

            // Rendering to a texture is a flat, single-eye job. Left as a stereo target it would be
            // drawn twice and arrive on the monitor as a squeezed half-frame.
            m_Camera.stereoTargetEye = StereoTargetEyeMask.None;

            // Nothing else should be listening from the tip of an instrument.
            var listener = GetComponent<AudioListener>();
            if (listener != null)
                listener.enabled = false;
        }

        /// <summary>Points the camera at a new feed texture.</summary>
        public void SetFeed(RenderTexture feed)
        {
            m_Feed = feed;
            Apply();
        }
    }
}
