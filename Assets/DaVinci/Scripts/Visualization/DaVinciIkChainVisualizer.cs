using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// Shows what a <see cref="ChainIKConstraint"/> is doing: an angle wedge on every joint the
    /// solver moves, the chain itself drawn as a skeleton, and the line from the tip to the target
    /// it is reaching for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The constraint reports only its root, its tip and its target. Everything between root and
    /// tip is implied by the hierarchy, which is exactly the part a reader cannot see — nothing in
    /// the inspector says that the chain rooted at HAND_BEGIN runs down through the three rotary
    /// mechanisms. Walking from tip up to root recovers that list and puts a readout on each link.
    /// </para>
    /// <para>
    /// Watching the constraint rather than a hand-listed set of joints means the overlay cannot go
    /// stale. Re-root the constraint one joint higher and the overlay follows; a fixed list would
    /// keep reporting the joint that is no longer in the chain.
    /// </para>
    /// <para>
    /// This is a pure observer. It adds <see cref="JointMotionTracker"/> and
    /// <see cref="JointAngleVisual"/> components, which only read, so the solver stays the single
    /// owner of every bone it drives.
    /// </para>
    /// </remarks>
    // After the trackers and the wedges, so the skeleton is drawn from the same solved pose.
    [ExecuteAlways]
    [DefaultExecutionOrder(240)]
    public class DaVinciIkChainVisualizer : MonoBehaviour
    {
        /// <summary>Guards against a malformed hierarchy turning the upward walk into a hang.</summary>
        const int k_MaxChainLength = 64;

        static readonly string[] k_ShaderFallbacks =
        {
            "Universal Render Pipeline/Unlit",
            "Unlit/Color",
            "Sprites/Default",
        };

        [SerializeField, Tooltip("The constraint to watch. Root, tip and target are read from it, so the overlay follows any re-rooting.")]
        ChainIKConstraint m_Constraint;

        [Header("Manual chain")]
        [SerializeField, Tooltip("Chain root, used only when no constraint is assigned. The first joint the solver is allowed to turn.")]
        Transform m_Root;

        [SerializeField, Tooltip("Chain tip, used only when no constraint is assigned. The joint driven to the target.")]
        Transform m_Tip;

        [SerializeField, Tooltip("What the tip reaches for. Used only when no constraint is assigned.")]
        Transform m_Target;

        [Header("Joints")]
        [SerializeField, Tooltip("Only put a readout on joints whose name contains one of these. Leave empty to read out every joint in the chain.")]
        string[] m_JointNameFilter = new string[0];

        [SerializeField, Tooltip("Show the angular speed alongside the angle on each joint's label.")]
        bool m_ShowJointSpeed;

        [Header("Chain")]
        [SerializeField, Tooltip("Draw a line through the chain, from root to tip. Off by default: the arm's own geometry already shows where the chain runs, so the line mostly adds clutter.")]
        bool m_ShowSkeleton;

        [SerializeField, Tooltip("Draw the line from the tip to the target it is reaching for.")]
        bool m_ShowTargetLine = true;

        [SerializeField, Tooltip("Skeleton and target line thickness in metres.")]
        float m_LineWidth = 0.0016f;

        [SerializeField, Tooltip("Colour of the chain skeleton.")]
        Color m_SkeletonColour = new Color(1f, 1f, 1f, 0.65f);

        [SerializeField, Tooltip("Colour of the tip-to-target line.")]
        Color m_TargetColour = new Color(0.4f, 1f, 0.5f, 1f);

        readonly List<Transform> m_Chain = new List<Transform>();
        readonly List<JointMotionTracker> m_Trackers = new List<JointMotionTracker>();
        readonly List<JointAngleVisual> m_Visuals = new List<JointAngleVisual>();

        LineRenderer m_Skeleton;
        LineRenderer m_TargetLine;
        Material m_SkeletonMaterial;
        Material m_TargetMaterial;
        Vector3[] m_SkeletonPoints;
        bool m_Visible = true;

        /// <summary>The joints between root and tip, ordered from the root outward.</summary>
        public IReadOnlyList<Transform> chain => m_Chain;

        /// <summary>A measurement for every joint given a readout, in the same order as <see cref="chain"/>.</summary>
        public IReadOnlyList<JointMotionTracker> trackers => m_Trackers;

        /// <summary>The joint the solver drives toward the target.</summary>
        public Transform tip => m_Constraint != null ? m_Constraint.data.tip : m_Tip;

        /// <summary>What the tip is reaching for, or <see langword="null"/> when the constraint has no target.</summary>
        public Transform target => m_Constraint != null ? m_Constraint.data.target : m_Target;

        /// <summary>
        /// How far the tip still is from its target, in metres. Infinite when there is no target.
        /// </summary>
        /// <remarks>
        /// This is the single number that says whether the solver is keeping up. A chain that is
        /// posed but never converging sits at a distance that does not fall.
        /// </remarks>
        public float targetDistance => tip != null && target != null
            ? Vector3.Distance(tip.position, target.position)
            : float.PositiveInfinity;

        /// <summary>Whether the whole overlay is drawn.</summary>
        public bool visible
        {
            get => m_Visible;
            set
            {
                m_Visible = value;

                for (var i = 0; i < m_Visuals.Count; i++)
                    m_Visuals[i].visible = value;

                if (m_Skeleton != null)
                    m_Skeleton.enabled = value && m_ShowSkeleton;

                if (m_TargetLine != null)
                    m_TargetLine.enabled = value && m_ShowTargetLine;
            }
        }

        void OnEnable()
        {
            m_Trackers.Clear();
            m_Visuals.Clear();

            if (!BuildChain())
                return;

            AttachReadouts();
            BuildLines();
        }

        void OnDisable()
        {
            DestroyGenerated(m_Skeleton != null ? m_Skeleton.gameObject : null);
            DestroyGenerated(m_TargetLine != null ? m_TargetLine.gameObject : null);
            DestroyGenerated(m_SkeletonMaterial);
            DestroyGenerated(m_TargetMaterial);

            m_Skeleton = null;
            m_TargetLine = null;
            m_SkeletonMaterial = null;
            m_TargetMaterial = null;
        }

        /// <summary>Destroys a generated object under either play mode or edit mode rules.</summary>
        static void DestroyGenerated(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        void LateUpdate()
        {
            if (!m_Visible)
                return;

            if (m_Skeleton != null)
            {
                for (var i = 0; i < m_Chain.Count; i++)
                    m_SkeletonPoints[i] = m_Chain[i].position;

                m_Skeleton.SetPositions(m_SkeletonPoints);
            }

            if (m_TargetLine != null)
            {
                var hasTarget = tip != null && target != null;
                m_TargetLine.enabled = hasTarget;

                if (hasTarget)
                {
                    m_TargetLine.SetPosition(0, tip.position);
                    m_TargetLine.SetPosition(1, target.position);
                }
            }
        }

        /// <summary>
        /// Recovers the joints between root and tip by walking up the hierarchy from the tip.
        /// </summary>
        /// <remarks>
        /// Upward, not downward: a joint has exactly one parent but the Da Vinci bones carry
        /// decorative children — fasteners, knurled knobs, indicator lights — so a downward search
        /// would have to guess which child continues the chain.
        /// </remarks>
        bool BuildChain()
        {
            var root = m_Constraint != null ? m_Constraint.data.root : m_Root;
            var chainTip = tip;

            if (root == null || chainTip == null)
            {
                Debug.LogError(
                    $"[Da Vinci] {name} has no chain to visualize. Assign a Chain IK Constraint, " +
                    "or set Root and Tip directly.", this);
                return false;
            }

            m_Chain.Clear();

            var current = chainTip;
            for (var i = 0; i < k_MaxChainLength && current != null; i++)
            {
                m_Chain.Add(current);

                if (current == root)
                {
                    m_Chain.Reverse();
                    return true;
                }

                current = current.parent;
            }

            Debug.LogError(
                $"[Da Vinci] {name}: '{root.name}' is not an ancestor of '{chainTip.name}', so there " +
                "is no chain between them.", this);

            m_Chain.Clear();
            return false;
        }

        void AttachReadouts()
        {
            for (var i = 0; i < m_Chain.Count; i++)
            {
                var joint = m_Chain[i];
                if (!WantsReadout(joint))
                    continue;

                var hasTracker = joint.TryGetComponent<JointMotionTracker>(out var tracker);
                var hasVisual = joint.TryGetComponent<JointAngleVisual>(out var visual);

                // Adding components is a play-mode-only convenience. In edit mode it would be a
                // silent change to the scene every time the inspector repainted, so the setup
                // command adds them as real, saved components instead and this only collects them.
                if (!Application.isPlaying)
                {
                    if (hasTracker && hasVisual)
                    {
                        m_Trackers.Add(tracker);
                        m_Visuals.Add(visual);
                    }

                    continue;
                }

                if (!hasTracker)
                    tracker = joint.gameObject.AddComponent<JointMotionTracker>();

                if (!hasVisual)
                    visual = joint.gameObject.AddComponent<JointAngleVisual>();

                visual.Configure(joint.name, m_ShowJointSpeed);

                m_Trackers.Add(tracker);
                m_Visuals.Add(visual);
            }

            if (m_Trackers.Count == 0 && Application.isPlaying)
            {
                Debug.LogWarning(
                    $"[Da Vinci] {name}: the joint name filter matched nothing in the chain from " +
                    $"'{m_Chain[0].name}' to '{m_Chain[m_Chain.Count - 1].name}'. No angles will be shown.",
                    this);
            }
        }

        bool WantsReadout(Transform joint)
        {
            if (m_JointNameFilter == null || m_JointNameFilter.Length == 0)
                return true;

            for (var i = 0; i < m_JointNameFilter.Length; i++)
            {
                if (!string.IsNullOrEmpty(m_JointNameFilter[i]) &&
                    joint.name.Contains(m_JointNameFilter[i], System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        void BuildLines()
        {
            if (m_ShowSkeleton && m_Chain.Count > 1)
            {
                m_SkeletonMaterial = CreateMaterial("Chain Skeleton", m_SkeletonColour);
                m_Skeleton = CreateLine("Chain Skeleton", m_SkeletonMaterial, m_SkeletonColour);
                m_Skeleton.positionCount = m_Chain.Count;
                m_SkeletonPoints = new Vector3[m_Chain.Count];
            }

            if (m_ShowTargetLine)
            {
                m_TargetMaterial = CreateMaterial("Target Line", m_TargetColour);
                m_TargetLine = CreateLine("Target Line", m_TargetMaterial, m_TargetColour);
                m_TargetLine.positionCount = 2;
            }
        }

        LineRenderer CreateLine(string label, Material material, Color colour)
        {
            var host = new GameObject(label) { hideFlags = HideFlags.DontSave };
            host.transform.SetParent(transform, false);

            var line = host.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = material;
            line.widthMultiplier = m_LineWidth;
            line.alignment = LineAlignment.View;
            line.startColor = colour;
            line.endColor = colour;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            return line;
        }

        Material CreateMaterial(string label, Color colour)
        {
            for (var i = 0; i < k_ShaderFallbacks.Length; i++)
            {
                var shader = Shader.Find(k_ShaderFallbacks[i]);
                if (shader != null && shader.isSupported)
                {
                    return new Material(shader)
                    {
                        name = $"{name} {label}",
                        color = colour,
                        hideFlags = HideFlags.DontSave,
                    };
                }
            }

            return null;
        }
    }
}
