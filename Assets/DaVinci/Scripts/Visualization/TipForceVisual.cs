using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// Draws an arrow at an instrument tip showing which way it is being driven and how hard.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is an estimate, and the estimate is stated rather than hidden.</b> The arms are
    /// posed kinematically by IK constraints — no rigidbody, no collisions, no contact forces — so
    /// there is no measured force anywhere in the system to read. What can be measured is how the
    /// tip moves, and Newton's second law turns that into a force: the tip's acceleration is
    /// differentiated from its world position and multiplied by <see cref="m_EffectiveMass"/>, the
    /// mass the instrument and its linkage are taken to carry. Change that number and every reading
    /// scales with it. It is the inertial force needed to move the tip as it is moving, which is
    /// the honest thing this scene can show; it is not a measured tissue-contact force, and it
    /// should not be read as one.
    /// </para>
    /// <para>
    /// Acceleration from finite differences is violently noisy — it is a second derivative of a
    /// position that the solver re-computes every frame — so both stages are smoothed. Without that
    /// the arrow flickers through its whole range several times a second and reads as broken.
    /// </para>
    /// <para>
    /// The arrow is two line renderers: a constant-width shaft and a head whose width tapers from
    /// wide to nothing, which draws as a triangle. Rebuilding a cone mesh each frame would cost far
    /// more for a shape the viewer reads as an arrow either way.
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [DefaultExecutionOrder(230)]
    [DisallowMultipleComponent]
    public class TipForceVisual : MonoBehaviour
    {
        static readonly string[] k_ShaderFallbacks =
        {
            "Universal Render Pipeline/Unlit",
            "Unlit/Color",
            "Sprites/Default",
        };

        const string k_OverlayRootName = "Da Vinci Force Arrows";

        static Transform s_OverlayRoot;

        [SerializeField, Tooltip("The instrument tip. Leave empty to use this object.")]
        Transform m_Tip;

        [SerializeField, Tooltip("Mass the instrument and its linkage are taken to carry, in kilograms. Every force reading scales directly with this.")]
        float m_EffectiveMass = 0.75f;

        [Header("Arrow")]
        [SerializeField, Tooltip("Arrow length per newton, in metres. Purely how the arrow is drawn; it does not change the reading.")]
        float m_MetresPerNewton = 0.05f;

        [SerializeField, Tooltip("Longest the arrow may be drawn, in metres, so a spike does not throw a line across the room.")]
        float m_MaxLength = 0.25f;

        [SerializeField, Tooltip("Below this force the arrow is hidden, in newtons. A tip at rest has no meaningful direction to point.")]
        float m_MinForce = 0.05f;

        [SerializeField, Tooltip("Shaft thickness in metres.")]
        float m_ShaftWidth = 0.004f;

        [SerializeField, Tooltip("Arrowhead length as a fraction of the whole arrow.")]
        float m_HeadFraction = 0.28f;

        [SerializeField, Tooltip("Arrow colour.")]
        Color m_Colour = new Color(1f, 0.35f, 0.3f);

        [Header("Smoothing")]
        [SerializeField, Range(0.02f, 0.6f), Tooltip("Smoothing on the tip's velocity, in seconds.")]
        float m_VelocitySmoothing = 0.08f;

        [SerializeField, Range(0.02f, 0.6f), Tooltip("Smoothing on the acceleration, in seconds. Acceleration is a second derivative and needs more smoothing than velocity.")]
        float m_ForceSmoothing = 0.18f;

        [Header("Label")]
        [SerializeField, Tooltip("Print the force in newtons beside the arrow.")]
        bool m_ShowLabel = true;

        [SerializeField, Tooltip("Label height in metres.")]
        float m_LabelSize = 0.016f;

        LineRenderer m_Shaft;
        LineRenderer m_Head;
        TextMeshPro m_Label;
        Transform m_LabelTransform;
        Material m_Material;
        Camera m_Camera;

        Vector3 m_PreviousPosition;
        Vector3 m_Velocity;
        Vector3 m_Force;
        bool m_Primed;

        /// <summary>The estimated force vector at the tip, in newtons.</summary>
        public Vector3 force => m_Force;

        /// <summary>The magnitude of <see cref="force"/>, in newtons.</summary>
        public float magnitude => m_Force.magnitude;

        /// <summary>The tip's smoothed world velocity, in metres per second.</summary>
        public Vector3 velocity => m_Velocity;

        /// <summary>The tip being measured.</summary>
        public Transform tip => m_Tip;

        void OnEnable()
        {
            if (m_Tip == null)
                m_Tip = transform;

            m_PreviousPosition = m_Tip.position;
            m_Primed = false;
            Build();
        }

        void OnDisable() => Teardown();

        void LateUpdate()
        {
            if (m_Tip == null)
                return;

            if (m_Shaft == null || m_Head == null || (m_ShowLabel && m_LabelTransform == null))
                Build();

            Measure();
            Draw();
        }

        void Measure()
        {
            var dt = Time.deltaTime;

            // Edit mode ticks irregularly; a delta of zero or of several seconds turns a finite
            // difference into a meaningless number rather than a large one.
            if (dt <= 0f || dt > 0.5f)
            {
                m_PreviousPosition = m_Tip.position;
                return;
            }

            var position = m_Tip.position;
            var rawVelocity = (position - m_PreviousPosition) / dt;
            m_PreviousPosition = position;

            // The first frame's "velocity" is measured against a position captured at an unrelated
            // time, so it is discarded rather than smoothed in.
            if (!m_Primed)
            {
                m_Velocity = Vector3.zero;
                m_Primed = true;
                return;
            }

            var previousVelocity = m_Velocity;
            m_Velocity = Vector3.Lerp(m_Velocity, rawVelocity, Smoothing(dt, m_VelocitySmoothing));

            var acceleration = (m_Velocity - previousVelocity) / dt;
            m_Force = Vector3.Lerp(m_Force, acceleration * m_EffectiveMass, Smoothing(dt, m_ForceSmoothing));
        }

        static float Smoothing(float dt, float seconds) => seconds > 0f ? 1f - Mathf.Exp(-dt / seconds) : 1f;

        void Draw()
        {
            var newtons = m_Force.magnitude;
            var visible = newtons >= m_MinForce;

            m_Shaft.enabled = visible;
            m_Head.enabled = visible;

            if (m_Label != null)
                m_Label.enabled = visible;

            if (!visible)
                return;

            var origin = m_Tip.position;
            var direction = m_Force / newtons;
            var length = Mathf.Min(newtons * m_MetresPerNewton, m_MaxLength);

            var headLength = length * m_HeadFraction;
            var neck = origin + direction * (length - headLength);
            var point = origin + direction * length;

            m_Shaft.SetPosition(0, origin);
            m_Shaft.SetPosition(1, neck);
            m_Shaft.widthMultiplier = m_ShaftWidth;
            m_Shaft.startColor = m_Colour;
            m_Shaft.endColor = m_Colour;

            // Width from wide at the neck to nothing at the point: a triangle, read as an arrowhead.
            m_Head.SetPosition(0, neck);
            m_Head.SetPosition(1, point);
            m_Head.widthMultiplier = m_ShaftWidth * 3.2f;
            m_Head.startColor = m_Colour;
            m_Head.endColor = m_Colour;

            if (m_Material != null)
                m_Material.color = m_Colour;

            if (m_Label == null || m_LabelTransform == null)
                return;

            m_LabelTransform.position = point + direction * (m_LabelSize * 1.5f);
            m_LabelTransform.localScale = Vector3.one * m_LabelSize;

            var viewer = Viewer();
            if (viewer != null)
            {
                m_LabelTransform.rotation =
                    Quaternion.LookRotation(m_LabelTransform.position - viewer.position, Vector3.up);
            }

            m_Label.color = m_Colour;
            m_Label.SetText("<b>{0:0.00}</b> N", newtons);
        }

        void Build()
        {
            if (m_Material == null)
                m_Material = CreateMaterial();

            if (m_Shaft == null)
            {
                m_Shaft = CreateLine("Force Shaft");
                m_Shaft.positionCount = 2;
            }

            if (m_Head == null)
            {
                m_Head = CreateLine("Force Head");
                m_Head.positionCount = 2;

                // Taper to a point. A curve rather than start/end width so it survives the
                // widthMultiplier being rewritten every frame.
                m_Head.widthCurve = new AnimationCurve(
                    new Keyframe(0f, 1f),
                    new Keyframe(1f, 0f));
            }

            if (m_ShowLabel && (m_Label == null || m_LabelTransform == null))
                CreateLabel();
        }

        void Teardown()
        {
            DestroyGenerated(m_Shaft != null ? m_Shaft.gameObject : null);
            DestroyGenerated(m_Head != null ? m_Head.gameObject : null);
            DestroyGenerated(m_LabelTransform != null ? m_LabelTransform.gameObject : null);
            DestroyGenerated(m_Material);

            m_Shaft = null;
            m_Head = null;
            m_Label = null;
            m_LabelTransform = null;
            m_Material = null;
        }

        static void DestroyGenerated(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        static Transform OverlayRoot()
        {
            if (s_OverlayRoot != null)
                return s_OverlayRoot;

            var host = new GameObject(k_OverlayRootName) { hideFlags = HideFlags.DontSave };
            host.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            host.transform.localScale = Vector3.one;

            s_OverlayRoot = host.transform;
            return s_OverlayRoot;
        }

        LineRenderer CreateLine(string label)
        {
            var host = new GameObject($"{name} — {label}") { hideFlags = HideFlags.DontSave };
            host.transform.SetParent(OverlayRoot(), false);

            var line = host.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = m_Material;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;

            return line;
        }

        void CreateLabel()
        {
            // Built with a RectTransform up front: TextMeshPro descends from Graphic, which requires
            // one, so adding it to an object with a plain Transform destroys that Transform and
            // leaves any reference taken beforehand pointing at a dead object.
            var host = new GameObject($"{name} — Force Label", typeof(RectTransform))
            {
                hideFlags = HideFlags.DontSave,
            };

            m_Label = host.AddComponent<TextMeshPro>();
            m_LabelTransform = m_Label.transform;
            m_LabelTransform.SetParent(OverlayRoot(), false);

            m_Label.fontSize = 1f;
            m_Label.alignment = TextAlignmentOptions.Center;
            m_Label.color = m_Colour;
            m_Label.rectTransform.sizeDelta = new Vector2(10f, 3f);

            if (m_Label.font == null && TMP_Settings.defaultFontAsset != null)
                m_Label.font = TMP_Settings.defaultFontAsset;
        }

        Material CreateMaterial()
        {
            for (var i = 0; i < k_ShaderFallbacks.Length; i++)
            {
                var shader = Shader.Find(k_ShaderFallbacks[i]);
                if (shader != null && shader.isSupported)
                {
                    return new Material(shader)
                    {
                        name = $"{name} Force Arrow",
                        color = m_Colour,
                        hideFlags = HideFlags.DontSave,
                    };
                }
            }

            Debug.LogError($"[Da Vinci] No usable unlit shader for the force arrow on '{name}'.", this);
            return null;
        }

        Transform Viewer()
        {
            if (m_Camera == null)
                m_Camera = Camera.main;

#if UNITY_EDITOR
            if (m_Camera == null && UnityEditor.SceneView.lastActiveSceneView != null)
                m_Camera = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif

            return m_Camera != null ? m_Camera.transform : null;
        }
    }
}
