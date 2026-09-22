using System;
using UnityEngine;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// A single revolute degree of freedom in a <see cref="DaVinciArm"/>.
    /// </summary>
    /// <remarks>
    /// The Da Vinci model is a rigid mechanism with no skinning, so a joint is just a transform in
    /// the imported hierarchy that is allowed to rotate about one axis. Everything between two
    /// joints is rigid structure and is carried along by the parenting that already exists in the FBX.
    /// </remarks>
    [Serializable]
    public class RevoluteJoint
    {
        [SerializeField, Tooltip("Transform this joint rotates. Must be part of the arm's imported chain.")]
        Transform m_Joint;

        [SerializeField, Tooltip("Rotation axis expressed in the joint's own local space at the rest pose.")]
        Vector3 m_LocalAxis = Vector3.up;

        [SerializeField, Tooltip("Lower rotation limit in degrees, measured from the rest pose.")]
        float m_MinAngle = -90f;

        [SerializeField, Tooltip("Upper rotation limit in degrees, measured from the rest pose.")]
        float m_MaxAngle = 90f;

        [SerializeField, Range(0f, 1f), Tooltip("Share of each solver correction this joint absorbs. Lower values hold the heavy proximal joints still and let the wrist do the work.")]
        float m_Weight = 1f;

        // Serialized because Apply() composes from it. If this were only a runtime field it would
        // reset to identity on every domain reload, and the first Apply() would then flatten the
        // imported pose instead of rotating away from it.
        [SerializeField, HideInInspector]
        Quaternion m_RestLocalRotation = Quaternion.identity;

        [SerializeField, HideInInspector]
        bool m_RestCaptured;

        float m_Angle;

        /// <summary>The transform driven by this joint.</summary>
        public Transform joint => m_Joint;

        /// <summary>Rotation axis in the joint's local space. Normalized on use.</summary>
        public Vector3 localAxis => m_LocalAxis.sqrMagnitude > Mathf.Epsilon ? m_LocalAxis.normalized : Vector3.up;

        /// <summary>Lower rotation limit in degrees, relative to the rest pose.</summary>
        public float minAngle => m_MinAngle;

        /// <summary>Upper rotation limit in degrees, relative to the rest pose.</summary>
        public float maxAngle => m_MaxAngle;

        /// <summary>Share of each solver correction this joint absorbs, in the range [0, 1].</summary>
        public float weight => m_Weight;

        /// <summary>Local rotation captured by the most recent <see cref="CaptureRest"/> call.</summary>
        public Quaternion restLocalRotation => m_RestLocalRotation;

        /// <summary>Current rotation in degrees, relative to the rest pose.</summary>
        public float angle
        {
            get => m_Angle;
            set
            {
                m_Angle = Mathf.Clamp(value, m_MinAngle, m_MaxAngle);
                Apply();
            }
        }

        /// <summary>Whether this joint is usable by the solver.</summary>
        public bool isValid => m_Joint != null && m_RestCaptured;

        /// <summary>Whether a rest pose has been recorded for this joint.</summary>
        public bool restCaptured => m_RestCaptured;

        /// <summary>
        /// The rotation axis in world space.
        /// </summary>
        /// <remarks>
        /// This is independent of <see cref="angle"/>: rotating about an axis leaves that axis fixed,
        /// so the world axis only depends on the parent's rotation and the captured rest pose.
        /// </remarks>
        public Vector3 worldAxis => m_Joint.parent != null
            ? m_Joint.parent.rotation * (m_RestLocalRotation * localAxis)
            : m_RestLocalRotation * localAxis;

        /// <summary>
        /// Records the joint's current local rotation as its zero angle. Call this once on the
        /// imported model before any solving, while the arm is in its authored rest pose.
        /// </summary>
        public void CaptureRest()
        {
            if (m_Joint == null)
                return;

            m_RestLocalRotation = m_Joint.localRotation;
            m_RestCaptured = true;
            m_Angle = 0f;
        }

        /// <summary>
        /// Writes <see cref="angle"/> back onto the transform.
        /// </summary>
        /// <remarks>
        /// Composed from the captured rest rotation rather than accumulated, so repeated solving
        /// cannot drift the joint away from its mechanical zero.
        /// </remarks>
        public void Apply()
        {
            // Applying before a rest pose exists would overwrite the imported pose with the
            // identity rotation and silently collapse the model.
            if (m_Joint == null || !m_RestCaptured)
                return;

            m_Joint.localRotation = m_RestLocalRotation * Quaternion.AngleAxis(m_Angle, localAxis);
        }

        /// <summary>
        /// Adds <paramref name="delta"/> degrees, clamped to the joint limits, and returns the
        /// amount actually applied after clamping.
        /// </summary>
        public float Rotate(float delta)
        {
            var previous = m_Angle;
            m_Angle = Mathf.Clamp(m_Angle + delta, m_MinAngle, m_MaxAngle);
            Apply();
            return m_Angle - previous;
        }

        /// <summary>Returns the joint to its captured rest pose.</summary>
        public void ResetToRest()
        {
            m_Angle = 0f;
            Apply();
        }
    }
}
