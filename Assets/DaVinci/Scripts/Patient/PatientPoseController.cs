using System.Collections.Generic;
using UnityEngine;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// Blends the bedridden patient between a supine <i>resting</i> pose and an upright
    /// <i>awake</i> pose, and lets a user drag individual IK targets by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The avatar's rig only constrains the arms, legs and head, so sitting the patient up cannot
    /// be done through the IK targets alone — the spine itself has to bend. This component rotates
    /// the spine chain directly and then carries the IK targets along with it, so the hands and
    /// head keep their position relative to the chest as the torso rises.
    /// </para>
    /// <para>
    /// The bend axis is derived from the avatar rather than authored, so it works whichever way the
    /// bed faces. The spine direction (hips to head) is crossed with world up, which yields the
    /// body's own left-right axis; a positive rotation about it always lifts the chest toward
    /// vertical. Nothing here depends on the bone's local axis convention.
    /// </para>
    /// <para>
    /// The IK targets are held as offsets from a bone rather than as world positions: each hand
    /// target rides its shoulder and the head target rides the neck. Those bones already move with
    /// the bend, so the targets follow the body exactly. An earlier version swung the targets round
    /// the hips on an arc of their own, which does not match where a four-bone spine actually puts
    /// the head, and the head constraint then stretched the neck out to reach it.
    /// </para>
    /// <para>
    /// Because every offset is relative to a bone, moving or rotating the patient — or the whole
    /// bed — does not invalidate the captured rest pose.
    /// </para>
    /// </remarks>
    public class PatientPoseController : MonoBehaviour
    {
        /// <summary>The two authored poses the patient blends between.</summary>
        public enum PatientState
        {
            /// <summary>Lying flat on the bed.</summary>
            Resting,

            /// <summary>Sat upright, as if just woken.</summary>
            Awake
        }

        [SerializeField, Tooltip("Root of the patient. All pose maths is done in this transform's local space. Defaults to this GameObject.")]
        Transform m_Root;

        [SerializeField, Tooltip("Hip bone. Acts as the pivot the torso rotates about when sitting up.")]
        Transform m_Hips;

        [SerializeField, Tooltip("Head bone. Only used to work out which way the body is lying, so the bend axis can be derived automatically.")]
        Transform m_Head;

        [SerializeField, Tooltip("Spine bones ordered from the hips outward. The sit-up angle is spread across these, which is what gives the torso a curve rather than a hinge.")]
        List<Transform> m_SpineChain = new List<Transform>();

        [SerializeField, Tooltip("Share of the total sit-up angle taken by each spine bone, matched by index. Values are normalised, so they are relative weights rather than degrees.")]
        List<float> m_SpineWeights = new List<float>();

        [SerializeField, Tooltip("Total angle the torso lifts through when awake. 60 is a patient sat up talking to a clinician; lower it for someone propped on a pillow.")]
        float m_SitUpAngle = 60f;

        [SerializeField, Tooltip("IK targets that ride with the torso, typically the two hand targets and the head target. Without these the arms would be left behind as the chest rises.")]
        List<Transform> m_FollowTargets = new List<Transform>();

        [SerializeField, Tooltip("Bone each target rides on, matched by index: the shoulders for the hand targets, the neck for the head. Each must be a bone the constraints do not themselves drive.")]
        List<Transform> m_FollowReferences = new List<Transform>();

        [SerializeField, Tooltip("Seconds taken to blend between the two poses.")]
        float m_BlendDuration = 1.5f;

        [SerializeField, Tooltip("Shape of the blend over time.")]
        AnimationCurve m_BlendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [SerializeField, Tooltip("Pose the patient starts in when play begins.")]
        PatientState m_StartState = PatientState.Resting;

        // Rest pose, captured once and never mutated by the blend itself.
        readonly List<Quaternion> m_RestSpineLocalRotations = new List<Quaternion>();
        readonly List<Vector3> m_RestTargetPositions = new List<Vector3>();
        readonly List<Quaternion> m_RestTargetRotations = new List<Quaternion>();
        readonly HashSet<Transform> m_HeldTargets = new HashSet<Transform>();

        Vector3 m_AxisLocal = Vector3.right;
        float m_Blend;
        float m_TargetBlend;
        bool m_Captured;

        /// <summary>The pose the patient is currently heading toward.</summary>
        public PatientState CurrentState => m_TargetBlend > 0.5f ? PatientState.Awake : PatientState.Resting;

        /// <summary>How far the blend has travelled, 0 at resting and 1 at awake.</summary>
        public float Blend => m_Blend;

        void Reset()
        {
            m_Root = transform;
            AutoBind();
        }

        void Awake()
        {
            if (m_Root == null)
                m_Root = transform;

            CaptureRestPose();

            m_TargetBlend = m_StartState == PatientState.Awake ? 1f : 0f;
            m_Blend = m_TargetBlend;
        }

        void LateUpdate()
        {
            // Run after the rigging graph has evaluated, so these writes are the ones that stick.
            if (!m_Captured)
                return;

            if (!Mathf.Approximately(m_Blend, m_TargetBlend))
            {
                var step = m_BlendDuration > 0f ? Time.deltaTime / m_BlendDuration : 1f;
                m_Blend = Mathf.MoveTowards(m_Blend, m_TargetBlend, step);
            }

            ApplyPose(m_BlendCurve.Evaluate(m_Blend));
        }

        /// <summary>Lays the patient back down.</summary>
        public void GoToResting() => m_TargetBlend = 0f;

        /// <summary>Sits the patient up.</summary>
        public void GoToAwake() => m_TargetBlend = 1f;

        /// <summary>Moves to <paramref name="state"/>.</summary>
        public void SetState(PatientState state) => m_TargetBlend = state == PatientState.Awake ? 1f : 0f;

        /// <summary>Switches to whichever pose the patient is not currently in.</summary>
        public void Toggle() => m_TargetBlend = m_TargetBlend > 0.5f ? 0f : 1f;

        /// <summary>Snaps straight to <paramref name="state"/> with no blend.</summary>
        public void SnapTo(PatientState state)
        {
            SetState(state);
            m_Blend = m_TargetBlend;
            if (m_Captured)
                ApplyPose(m_BlendCurve.Evaluate(m_Blend));
        }

        /// <summary>
        /// Stops this component driving <paramref name="target"/> so a user can drag it freely.
        /// </summary>
        public void BeginManualControl(Transform target)
        {
            if (target != null)
                m_HeldTargets.Add(target);
        }

        /// <summary>
        /// Hands <paramref name="target"/> back and keeps it wherever the user let go, by folding
        /// its new position into the rest pose.
        /// </summary>
        public void EndManualControl(Transform target)
        {
            if (target == null || !m_HeldTargets.Remove(target) || !m_Captured)
                return;

            var index = m_FollowTargets.IndexOf(target);
            var reference = ReferenceFor(index);
            if (index < 0 || reference == null)
                return;

            // Re-express where the user left it in the reference bone's space. Because the offset
            // is stored relative to a bone rather than to a pose, this holds in either state and
            // there is no torso rotation to unwind.
            m_RestTargetPositions[index] = reference.InverseTransformPoint(target.position);
            m_RestTargetRotations[index] = Quaternion.Inverse(reference.rotation) * target.rotation;
        }

        /// <summary>
        /// Records the pose the avatar is in right now as the resting pose, and works out the axis
        /// the torso will rotate about. Called on <c>Awake</c>, and by the setup tool in the editor.
        /// </summary>
        public void CaptureRestPose()
        {
            if (m_Root == null)
                m_Root = transform;

            m_RestSpineLocalRotations.Clear();
            foreach (var bone in m_SpineChain)
                m_RestSpineLocalRotations.Add(bone != null ? bone.localRotation : Quaternion.identity);

            m_RestTargetPositions.Clear();
            m_RestTargetRotations.Clear();
            for (var i = 0; i < m_FollowTargets.Count; i++)
            {
                var target = m_FollowTargets[i];
                var reference = ReferenceFor(i);

                if (target == null || reference == null)
                {
                    m_RestTargetPositions.Add(Vector3.zero);
                    m_RestTargetRotations.Add(Quaternion.identity);
                    continue;
                }

                // Held in the reference bone's space, not the root's. The bone already travels with
                // the torso as the spine bends, so the target inherits that motion exactly instead
                // of being sent along a guessed arc of its own.
                m_RestTargetPositions.Add(reference.InverseTransformPoint(target.position));
                m_RestTargetRotations.Add(Quaternion.Inverse(reference.rotation) * target.rotation);
            }

            m_AxisLocal = ResolveBendAxis();
            m_Captured = true;
        }

        /// <summary>
        /// Derives the body's left-right axis from the way it is lying, so a positive sit-up angle
        /// always lifts the chest toward vertical no matter how the bed is oriented.
        /// </summary>
        Vector3 ResolveBendAxis()
        {
            var fallback = Vector3.right;
            if (m_Hips == null || m_Head == null)
                return fallback;

            var spineDirection = m_Root.InverseTransformPoint(m_Head.position) - m_Root.InverseTransformPoint(m_Hips.position);
            if (spineDirection.sqrMagnitude < 1e-8f)
                return fallback;

            var up = m_Root.InverseTransformDirection(Vector3.up);
            var axis = Vector3.Cross(spineDirection.normalized, up);

            // Degenerate only if the patient is already stood upright, in which case any lateral
            // axis is as good as another.
            return axis.sqrMagnitude < 1e-6f ? fallback : axis.normalized;
        }

        void ApplyPose(float t)
        {
            var totalAngle = m_SitUpAngle * t;

            // Restore the rest pose first so the blend is always computed from a fixed origin
            // rather than accumulating frame to frame.
            for (var i = 0; i < m_SpineChain.Count && i < m_RestSpineLocalRotations.Count; i++)
            {
                if (m_SpineChain[i] != null)
                    m_SpineChain[i].localRotation = m_RestSpineLocalRotations[i];
            }

            if (!Mathf.Approximately(totalAngle, 0f))
            {
                var worldAxis = m_Root.TransformDirection(m_AxisLocal);
                var totalWeight = 0f;
                foreach (var weight in m_SpineWeights)
                    totalWeight += Mathf.Max(0f, weight);

                for (var i = 0; i < m_SpineChain.Count; i++)
                {
                    var bone = m_SpineChain[i];
                    if (bone == null)
                        continue;

                    // Even split when weights are missing or all zero.
                    var share = totalWeight > 0f && i < m_SpineWeights.Count
                        ? Mathf.Max(0f, m_SpineWeights[i]) / totalWeight
                        : 1f / m_SpineChain.Count;

                    // Rotating a parent already carries its children, so applying each bone's share
                    // in order builds up a curved torso rather than a single hinge.
                    bone.rotation = Quaternion.AngleAxis(totalAngle * share, worldAxis) * bone.rotation;
                }
            }

            // Targets simply ride their reference bone. Carrying them round the hip pivot instead
            // was what stretched the neck: the spine's bend is spread across four bones, so the
            // head does not travel on a circle centred at the hips, and the head constraint then
            // hauled the skull out to meet a target that had gone somewhere the body had not.
            for (var i = 0; i < m_FollowTargets.Count && i < m_RestTargetPositions.Count; i++)
            {
                var target = m_FollowTargets[i];
                var reference = ReferenceFor(i);

                if (target == null || reference == null || m_HeldTargets.Contains(target))
                    continue;

                target.position = reference.TransformPoint(m_RestTargetPositions[i]);
                target.rotation = reference.rotation * m_RestTargetRotations[i];
            }
        }

        /// <summary>
        /// Fills in the bone and target references by name. Safe to call repeatedly; it only ever
        /// replaces fields it can resolve.
        /// </summary>
        public void AutoBind()
        {
            if (m_Root == null)
                m_Root = transform;

            m_Hips = FindDescendant("mixamorig8:Hips") ?? m_Hips;
            m_Head = FindDescendant("mixamorig8:Head") ?? m_Head;

            var spine = new List<Transform>();
            foreach (var boneName in new[] { "mixamorig8:Spine", "mixamorig8:Spine1", "mixamorig8:Spine2", "mixamorig8:Neck" })
            {
                var bone = FindDescendant(boneName);
                if (bone != null)
                    spine.Add(bone);
            }

            if (spine.Count > 0)
            {
                m_SpineChain = spine;

                // Most of the bend belongs low in the spine, tapering off toward the neck.
                m_SpineWeights = new List<float>();
                foreach (var bone in spine)
                    m_SpineWeights.Add(bone.name.EndsWith("Neck") ? 0.5f : 1f);
            }

            // Each target rides the nearest bone upstream of the constraint that drives it: the
            // shoulder for a hand (the arm IK starts at the upper arm) and the neck for the head
            // (the head constraint drives the skull). Picking a bone the constraint itself writes
            // would close a loop, with the target chasing a bone that is chasing the target.
            var pairs = new[]
            {
                ("Left Arm IK_target", "mixamorig8:LeftShoulder"),
                ("Right Arm IK_target", "mixamorig8:RightShoulder"),
                ("Head Target", "mixamorig8:Neck")
            };

            var targets = new List<Transform>();
            var references = new List<Transform>();
            foreach (var (targetName, referenceName) in pairs)
            {
                var target = FindDescendant(targetName);
                if (target == null)
                    continue;

                targets.Add(target);
                references.Add(FindDescendant(referenceName));
            }

            if (targets.Count > 0)
            {
                m_FollowTargets = targets;
                m_FollowReferences = references;
            }
        }

        /// <summary>The bone target <paramref name="index"/> rides on, if one is bound.</summary>
        Transform ReferenceFor(int index) =>
            index >= 0 && index < m_FollowReferences.Count ? m_FollowReferences[index] : null;

        Transform FindDescendant(string targetName)
        {
            foreach (var child in GetComponentsInChildren<Transform>(true))
            {
                if (child.name == targetName)
                    return child;
            }

            return null;
        }
    }
}
