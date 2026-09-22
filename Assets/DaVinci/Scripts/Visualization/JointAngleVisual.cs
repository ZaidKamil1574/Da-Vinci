using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// Draws the angle a joint has turned through as a wedge in its own plane of rotation, with a
    /// label giving the figure in degrees.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Gizmos would have been a fraction of the code, but they only exist in the editor's scene
    /// view. The surgeon is inside a headset, so anything they need to see has to be real geometry:
    /// a <see cref="LineRenderer"/> for the wedge and a <see cref="TextMeshPro"/> for the figure.
    /// </para>
    /// <para>
    /// <b>It runs in edit mode as well as play mode.</b> An overlay you cannot see until you press
    /// Play is an overlay you cannot aim, colour or size without a guess-and-check cycle each time.
    /// The drawn objects are therefore marked <see cref="HideFlags.DontSave"/>: they appear in the
    /// scene and the hierarchy, but never enter the scene file, so editing with them on cannot
    /// leave a pile of generated geometry committed behind you.
    /// </para>
    /// <para>
    /// <b>The drawn objects are not children of the joint.</b> The Da Vinci bones carry a
    /// compounded <see cref="Transform.lossyScale"/> of about -24 — large, and negative, because
    /// the model is mirrored on import. A line or a label parented to a bone inherits that: a 2 mm
    /// line renders 5 cm thick and the text comes out inside out. Everything is parented to one
    /// unscaled root and placed in world space each frame instead, which it already was, so nothing
    /// is lost by detaching it.
    /// </para>
    /// <para>
    /// The wedge is one continuous closed polyline — pivot, out along the rest direction, around
    /// the arc, and back — so a single renderer draws the arc and both bounding spokes. Its point
    /// count is fixed for the life of the component: <see cref="LineRenderer.SetPositions(Vector3[])"/>
    /// requires the array to match <see cref="LineRenderer.positionCount"/>, and re-sizing a buffer
    /// as the angle changes would allocate on almost every frame of a move.
    /// </para>
    /// </remarks>
    // Runs after JointMotionTracker so the wedge shows the angle measured this frame, not last
    // frame's. Both sample in LateUpdate, because the rig solves between Update and LateUpdate.
    [ExecuteAlways]
    [DefaultExecutionOrder(220)]
    [DisallowMultipleComponent]
    public class JointAngleVisual : MonoBehaviour
    {
        /// <summary>Tried in order. The first that resolves draws the wedge and the hinge line.</summary>
        static readonly string[] k_ShaderFallbacks =
        {
            "Universal Render Pipeline/Unlit",
            "Unlit/Color",
            "Sprites/Default",
        };

        /// <summary>Arc tessellation. Fixed, so the position buffer never has to be re-sized.</summary>
        const int k_ArcSegments = 24;

        /// <summary>Below this the wedge is thinner than its own line width and reads as a smudge.</summary>
        const float k_MinDrawnAngle = 0.35f;

        /// <summary>Name of the unscaled object every overlay's geometry is parented to.</summary>
        const string k_OverlayRootName = "Da Vinci Angle Overlays";

        /// <summary>
        /// Shared parent for the drawn objects, kept at identity so nothing inherits a bone's scale.
        /// </summary>
        static Transform s_OverlayRoot;

        [SerializeField, Tooltip("The joint whose angle is drawn. One is added to this object automatically when left empty.")]
        JointMotionTracker m_Tracker;

        [SerializeField, Tooltip("Name shown on the label. Defaults to the joint's own name.")]
        string m_DisplayName;

        [Header("Wedge")]
        [SerializeField, Tooltip("Wedge radius as a fraction of the joint's own length. 1 draws it out as far as the next joint.")]
        float m_RadiusScale = 0.30f;

        [SerializeField, Tooltip("Smallest wedge radius in metres, so short joints stay readable.")]
        float m_MinRadius = 0.015f;

        [SerializeField, Tooltip("Largest wedge radius in metres, so long joints do not sweep across the whole arm.")]
        float m_MaxRadius = 0.045f;

        [SerializeField, Range(0.005f, 0.15f), Tooltip("Line thickness as a fraction of the wedge radius. Proportional rather than absolute, so a wedge on a small joint is not drawn in the same heavy line as one on a large joint.")]
        float m_LineWidthScale = 0.07f;

        [SerializeField, Tooltip("Colour of the wedge while the joint turns about its own hinge.")]
        Color m_Colour = new Color(0.25f, 0.85f, 1f, 1f);

        [SerializeField, Tooltip("Colour used once the solver twists this joint off its hinge by more than the warning angle.")]
        Color m_OffAxisColour = new Color(1f, 0.55f, 0.15f, 1f);

        [SerializeField, Tooltip("Off-axis rotation in degrees above which the wedge switches to the warning colour. The chain solver treats every bone as a ball joint, so a few degrees is normal.")]
        float m_OffAxisWarningAngle = 8f;

        [SerializeField, Tooltip("Draw the hinge the joint turns about. Off by default: it doubles the line count on a machine that already carries a dozen readouts, for information the wedge's own plane already conveys.")]
        bool m_ShowAxis;

        [Header("Label")]
        [SerializeField, Tooltip("Show the angle in degrees beside the wedge.")]
        bool m_ShowLabel = true;

        [SerializeField, Tooltip("Also print the joint's angular and linear speed on the label.")]
        bool m_ShowSpeed = true;

        [SerializeField, Tooltip("Label height as a fraction of the wedge radius.")]
        float m_LabelScale = 0.42f;

        LineRenderer m_Wedge;
        LineRenderer m_Axis;
        TextMeshPro m_Label;
        Transform m_LabelTransform;
        Material m_Material;
        Vector3[] m_Points;
        string m_LabelFormat;
        Camera m_Camera;
        bool m_Visible = true;

        /// <summary>The measurement this overlay draws.</summary>
        public JointMotionTracker tracker => m_Tracker;

        /// <summary>Name shown on the label.</summary>
        public string displayName => string.IsNullOrEmpty(m_DisplayName) ? name : m_DisplayName;

        /// <summary>
        /// The colour this overlay draws in. Set per joint so a dozen readouts on one machine can
        /// be told apart at a glance, rather than all reading as the same blue thicket.
        /// </summary>
        public Color colour
        {
            get => m_Colour;
            set => m_Colour = value;
        }

        /// <summary>Whether the overlay is currently drawn.</summary>
        public bool visible
        {
            get => m_Visible;
            set
            {
                m_Visible = value;
                ApplyVisibility();
            }
        }

        void OnEnable()
        {
            if (m_Tracker == null && !TryGetComponent(out m_Tracker))
                m_Tracker = gameObject.AddComponent<JointMotionTracker>();

            if (string.IsNullOrEmpty(m_DisplayName))
                m_DisplayName = m_Tracker.joint != null ? m_Tracker.joint.name : name;

            Build();
        }

        void OnDisable()
        {
            // Built in OnEnable rather than Awake so the overlay survives the domain reloads that
            // an editor session is full of; torn down here to match, so disabling the component
            // actually removes what it drew instead of leaving it frozen on screen.
            Teardown();
        }

        void LateUpdate()
        {
            if (!m_Visible || m_Tracker == null || !m_Tracker.isReady)
                return;

            // A domain reload, or a play-mode transition, can destroy the drawn objects while this
            // component survives. Rebuild whatever has gone missing before drawing into it.
            if (m_Wedge == null || (m_ShowLabel && m_LabelTransform == null) || (m_ShowAxis && m_Axis == null))
                Build();

            Redraw();
        }

        /// <summary>
        /// Sets the label text and whether it carries a speed, after the overlay has been built.
        /// </summary>
        /// <remarks>
        /// Needed because a component added with <c>AddComponent</c> runs its enable callback inside
        /// that call, before the caller has had any chance to set its fields. Anything configured
        /// from code therefore has to be applied afterwards, and the baked label format rebuilt.
        /// </remarks>
        public void Configure(string label, bool showSpeed)
        {
            if (!string.IsNullOrEmpty(label))
                m_DisplayName = label;

            m_ShowSpeed = showSpeed;
            m_LabelFormat = BuildLabelFormat();
        }

        /// <summary>
        /// Creates whichever drawn objects are currently missing.
        /// </summary>
        /// <remarks>
        /// Each piece is checked on its own rather than gating the whole method on one of them. A
        /// single early return would mean that if one object were destroyed while the others
        /// survived, the survivor would keep the method from ever rebuilding the casualty, and the
        /// overlay would run on permanently with a hole in it.
        /// </remarks>
        void Build()
        {
            if (m_Material == null)
                m_Material = CreateMaterial();

            m_Points ??= new Vector3[k_ArcSegments + 2];
            m_LabelFormat ??= BuildLabelFormat();

            if (m_Wedge == null)
            {
                m_Wedge = CreateLine("Angle Wedge");
                m_Wedge.loop = true;
                m_Wedge.positionCount = m_Points.Length;
            }

            if (m_ShowAxis && m_Axis == null)
            {
                m_Axis = CreateLine("Hinge Axis");
                m_Axis.positionCount = 2;
            }

            if (m_ShowLabel && (m_Label == null || m_LabelTransform == null))
                CreateLabel();

            ApplyVisibility();
        }

        void Teardown()
        {
            DestroyGenerated(m_Wedge != null ? m_Wedge.gameObject : null);
            DestroyGenerated(m_Axis != null ? m_Axis.gameObject : null);
            DestroyGenerated(m_LabelTransform != null ? m_LabelTransform.gameObject : null);
            DestroyGenerated(m_Material);

            m_Wedge = null;
            m_Axis = null;
            m_Label = null;
            m_LabelTransform = null;
            m_Material = null;
        }

        /// <summary>
        /// Destroys a generated object under either play mode or edit mode rules.
        /// </summary>
        /// <remarks>
        /// <see cref="Object.Destroy(Object)"/> defers to the end of the frame, which never arrives
        /// in edit mode, so the object would survive and a rebuild would stack a second copy on top
        /// of it. <see cref="Object.DestroyImmediate(Object)"/> is illegal during play.
        /// </remarks>
        static void DestroyGenerated(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        /// <summary>
        /// Bakes the joint's name into the format string.
        /// </summary>
        /// <remarks>
        /// TMP's <c>SetText</c> overloads take floats only — there is no string argument — so the
        /// name cannot be a placeholder. Baking it in keeps the per-frame update allocation free,
        /// which <c>string.Format</c> into <c>.text</c> would not be.
        /// </remarks>
        string BuildLabelFormat()
        {
            // Note the format spec is TMP's own, not .NET's: digits after the '.' count decimal
            // places. "{0:0.0}" is one decimal; "{1:0}" is a whole number.
            return m_ShowSpeed
                ? displayName + "\n<b>{0:0.0}</b>°\n<size=70%>{1:0}°/s   {2:0.00} m/s"
                : displayName + "\n<b>{0:0.0}</b>°";
        }

        /// <summary>Finds or creates the unscaled root the drawn objects live under.</summary>
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
            var host = new GameObject($"{displayName} — {label}") { hideFlags = HideFlags.DontSave };
            host.transform.SetParent(OverlayRoot(), false);

            var line = host.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = m_Material;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;

            // A ribbon that always faces the viewer, so a wedge seen edge-on stays a readable
            // outline instead of collapsing to nothing.
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;

            return line;
        }

        /// <summary>
        /// Creates the floating degrees label.
        /// </summary>
        /// <remarks>
        /// The GameObject is built with a <see cref="RectTransform"/> from the outset, and the
        /// transform is read back off the component afterwards. Both matter.
        /// <see cref="TextMeshPro"/> descends from <c>Graphic</c>, which is marked
        /// <c>[RequireComponent(typeof(RectTransform))]</c>, so adding it to an object holding a
        /// plain <see cref="Transform"/> makes Unity <b>destroy that Transform</b> and install a
        /// RectTransform in its place. A reference taken before the call is left pointing at the
        /// destroyed original, and every later write to it throws.
        /// </remarks>
        void CreateLabel()
        {
            var host = new GameObject($"{displayName} — Angle Label", typeof(RectTransform))
            {
                hideFlags = HideFlags.DontSave,
            };

            m_Label = host.AddComponent<TextMeshPro>();

            // Read back from the component, never from a reference cached beforehand.
            m_LabelTransform = m_Label.transform;
            m_LabelTransform.SetParent(OverlayRoot(), false);

            m_Label.fontSize = 1f;
            m_Label.alignment = TextAlignmentOptions.Center;
            m_Label.color = m_Colour;
            m_Label.rectTransform.sizeDelta = new Vector2(18f, 6f);

            // TMP has no font of its own until one is given; without this the label silently
            // renders nothing at all.
            if (m_Label.font == null && TMP_Settings.defaultFontAsset != null)
                m_Label.font = TMP_Settings.defaultFontAsset;
        }

        Material CreateMaterial()
        {
            for (var i = 0; i < k_ShaderFallbacks.Length; i++)
            {
                var shader = Shader.Find(k_ShaderFallbacks[i]);
                if (shader == null || !shader.isSupported)
                    continue;

                return new Material(shader)
                {
                    name = $"{name} Angle Overlay",
                    hideFlags = HideFlags.DontSave,
                };
            }

            Debug.LogError(
                $"[Da Vinci] No usable unlit shader for the angle overlay on '{name}'. Tried: " +
                string.Join(", ", k_ShaderFallbacks) + ". The wedge cannot be drawn.", this);

            return null;
        }

        void Redraw()
        {
            var pivot = m_Tracker.joint.position;
            var axis = m_Tracker.worldAxis;
            var rest = m_Tracker.restDirection;

            // Before the joint has turned at all the hinge is still a guess and the rest direction
            // can be degenerate. Drawing from it would put the wedge somewhere arbitrary.
            if (axis.sqrMagnitude < 1e-8f || rest.sqrMagnitude < 1e-8f)
                return;

            axis = axis.normalized;
            rest = rest.normalized;

            var radius = Mathf.Clamp(m_Tracker.referenceLength * m_RadiusScale, m_MinRadius, m_MaxRadius);
            var sweep = m_Tracker.angle;
            var colour = m_Tracker.offAxisAngle > m_OffAxisWarningAngle ? m_OffAxisColour : m_Colour;

            DrawWedge(pivot, axis, rest, radius, sweep);
            m_Wedge.widthMultiplier = radius * m_LineWidthScale;
            SetColour(m_Wedge, colour);

            if (m_Axis != null)
            {
                m_Axis.SetPosition(0, pivot - axis * (radius * 0.6f));
                m_Axis.SetPosition(1, pivot + axis * (radius * 0.6f));
                m_Axis.widthMultiplier = radius * m_LineWidthScale * 0.6f;
                m_Axis.startColor = colour;
                m_Axis.endColor = colour;
            }

            // Both are checked: the component and its transform are separate references, and a
            // rebuild that half-failed could leave one of them behind.
            if (m_Label != null && m_LabelTransform != null)
                UpdateLabel(pivot, axis, rest, radius, sweep, colour);
        }

        void DrawWedge(Vector3 pivot, Vector3 axis, Vector3 rest, float radius, float sweep)
        {
            var spoke = pivot + rest * radius;

            // A sweep too small to draw still deserves its spoke: it marks where zero is, which is
            // what makes "this joint has not moved" legible rather than merely absent. Collapsing
            // the arc onto that spoke keeps the point count fixed.
            var collapsed = Mathf.Abs(sweep) < k_MinDrawnAngle;

            m_Points[0] = pivot;
            for (var i = 0; i <= k_ArcSegments; i++)
            {
                m_Points[i + 1] = collapsed
                    ? spoke
                    : pivot + Quaternion.AngleAxis(sweep * i / k_ArcSegments, axis) * rest * radius;
            }

            m_Wedge.SetPositions(m_Points);
        }

        void UpdateLabel(Vector3 pivot, Vector3 axis, Vector3 rest, float radius, float sweep, Color colour)
        {
            var midpoint = pivot + Quaternion.AngleAxis(sweep * 0.5f, axis) * rest * (radius * 1.4f);
            m_LabelTransform.position = midpoint;
            m_LabelTransform.localScale = Vector3.one * (radius * m_LabelScale);

            var viewer = Viewer();
            if (viewer != null)
                m_LabelTransform.rotation = Quaternion.LookRotation(midpoint - viewer.position, Vector3.up);

            m_Label.color = colour;
            m_Label.SetText(m_LabelFormat, sweep, m_Tracker.angularSpeed, m_Tracker.linearSpeed);
        }

        /// <summary>
        /// The transform the label should turn to face.
        /// </summary>
        /// <remarks>
        /// In play mode that is the player's camera. In edit mode <see cref="Camera.main"/> is
        /// usually null, so the scene view's own camera is used and the label stays readable while
        /// the overlay is being set up.
        /// </remarks>
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

        /// <summary>
        /// Tints the overlay.
        /// </summary>
        /// <remarks>
        /// URP/Unlit ignores vertex colour, so the tint has to be written to the material as well.
        /// The wedge and the hinge line share one material and therefore one colour; the hinge is
        /// told apart by being thinner and shorter rather than by a different shade. A second
        /// material to fade it would double the material count on an overlay that already exists on
        /// every joint of every arm.
        /// </remarks>
        void SetColour(LineRenderer line, Color colour)
        {
            line.startColor = colour;
            line.endColor = colour;

            if (m_Material != null)
                m_Material.color = colour;
        }

        void ApplyVisibility()
        {
            if (m_Wedge != null)
                m_Wedge.enabled = m_Visible;

            if (m_Axis != null)
                m_Axis.enabled = m_Visible;

            if (m_Label != null)
                m_Label.enabled = m_Visible;
        }
    }
}
