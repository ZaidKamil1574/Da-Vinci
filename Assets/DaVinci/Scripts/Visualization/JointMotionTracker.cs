using UnityEngine;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// Measures how far, and how fast, one transform in the Da Vinci hierarchy has rotated away
    /// from the pose it started in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This component only reads. Nothing here writes to <see cref="joint"/>, so it can sit on a
    /// bone that a <c>ChainIKConstraint</c> owns without fighting the solver for control of it.
    /// </para>
    /// <para>
    /// Sampling happens in <c>LateUpdate</c> because the rig evaluates during the animation pass,
    /// which Unity runs after <c>Update</c> and before <c>LateUpdate</c>. Reading in <c>Update</c>
    /// would report the previous frame's pose — a lag that is invisible in the angle but shows up
    /// in the speed as a spike on the frame the arm stops.
    /// </para>
    /// <para>
    /// <b>Why a signed angle about one axis rather than the raw quaternion angle.</b>
    /// <c>ChainIKConstraint</c> treats every bone as a ball joint, so the rotation it leaves on a
    /// joint is rarely a pure turn about that joint's real hinge.
    /// <see cref="Quaternion.ToAngleAxis"/> would describe that as an unsigned number, which reads
    /// the same 12° whether the joint is 12° up or 12° down. Projecting onto the measurement axis
    /// gives back the number an engineer would read off the machine, and
    /// <see cref="offAxisAngle"/> keeps what the projection discards visible instead of quietly
    /// dropping it.
    /// </para>
    /// </remarks>
    // Runs before JointAngleVisual, which draws what this measures. Both work in LateUpdate.
    // ExecuteAlways so the readings — and the overlay drawn from them — exist while the scene is
    // being set up, not only once play has started.
    [ExecuteAlways]
    [DefaultExecutionOrder(200)]
    public class JointMotionTracker : MonoBehaviour
    {
        /// <summary>Rotation below which the auto-detected axis is still just numerical noise.</summary>
        const float k_AxisLatchAngle = 1.5f;

        /// <summary>Shortest reference arm that still gives a stable direction, in metres.</summary>
        const float k_MinReferenceLength = 1e-4f;

        [SerializeField, Tooltip("Transform to measure. Leave empty to measure the object this sits on.")]
        Transform m_Joint;

        [SerializeField, Tooltip("Hinge axis in the joint's own local space. Leave at zero to detect it from the first real motion.")]
        Vector3 m_MeasureAxis = Vector3.zero;

        [SerializeField, Tooltip("Child used as the pointer the angle is measured to. Leave empty to take the first child.")]
        Transform m_ReferenceChild;

        [SerializeField, Range(0f, 0.5f), Tooltip("Smoothing applied to the speed readouts, in seconds. Zero reports the raw per-frame rate, which is too noisy to read.")]
        float m_SpeedSmoothing = 0.12f;

        // Serialized, and deliberately so. With the component running in edit mode, a domain
        // reload would otherwise re-zero every joint against whatever pose it happened to be in,
        // so a joint you had turned by hand to check the readout would silently read 0 again.
        // RevoluteJoint in this project serializes its rest pose for the same reason.
        [SerializeField, HideInInspector]
        Quaternion m_RestLocalRotation = Quaternion.identity;

        [SerializeField, HideInInspector]
        bool m_RestCaptured;

        Vector3 m_LocalReference = Vector3.forward;
        Vector3 m_ResolvedAxis = Vector3.up;
        float m_ReferenceLength = 0.05f;
        bool m_AxisLatched;

        Quaternion m_PreviousWorldRotation = Quaternion.identity;
        Vector3 m_PreviousWorldPosition;

        float m_Angle;
        float m_OffAxisAngle;
        float m_AngularSpeed;
        float m_LinearSpeed;

        /// <summary>The transform being measured.</summary>
        public Transform joint => m_Joint;

        /// <summary>Signed rotation away from the rest pose about <see cref="worldAxis"/>, in degrees.</summary>
        public float angle => m_Angle;

        /// <summary>
        /// How much of the rest-to-current rotation is <i>not</i> about the measurement axis, in
        /// degrees. Large values mean the solver is posing this joint in a way the real single-axis
        /// mechanism could not reach.
        /// </summary>
        public float offAxisAngle => m_OffAxisAngle;

        /// <summary>Smoothed world angular speed, in degrees per second.</summary>
        public float angularSpeed => m_AngularSpeed;

        /// <summary>Smoothed world speed of the joint's own origin, in metres per second.</summary>
        public float linearSpeed => m_LinearSpeed;

        /// <summary>The measurement axis in world space.</summary>
        /// <remarks>
        /// Taken from the rest pose, not the current one. Rotating about an axis leaves that axis
        /// fixed, so anchoring it to rest keeps the drawn arc from wobbling as the joint turns.
        /// </remarks>
        public Vector3 worldAxis => ParentRotation() * (m_RestLocalRotation * m_ResolvedAxis);

        /// <summary>Where the reference arm pointed at rest, projected into the plane of rotation.</summary>
        public Vector3 restDirection => Project(ParentRotation() * (m_RestLocalRotation * m_LocalReference));

        /// <summary>Where the reference arm points now, projected into the plane of rotation.</summary>
        public Vector3 currentDirection => Project(m_Joint.rotation * m_LocalReference);

        /// <summary>Distance from the joint to its reference child at rest, in metres.</summary>
        public float referenceLength => m_ReferenceLength;

        /// <summary>Whether a rest pose has been recorded and the readings are meaningful.</summary>
        public bool isReady => m_RestCaptured && m_Joint != null;

        void OnEnable()
        {
            if (m_Joint == null)
                m_Joint = transform;

            // Only zero a joint that has never been zeroed. Re-capturing on every enable would
            // bake whatever pose the scene was last saved in as the new mechanical zero.
            if (!m_RestCaptured)
                CaptureRest();
            else
                Reacquire();
        }

        /// <summary>
        /// Restores the parts of the measurement that are not serialized, after a reload.
        /// </summary>
        /// <remarks>
        /// The rest pose survives a domain reload but the reference direction and the latched axis
        /// do not, and both are derived rather than authored. Recomputing them leaves the angle
        /// exactly where it was; re-capturing the rest pose would not.
        /// </remarks>
        void Reacquire()
        {
            m_AxisLatched = m_MeasureAxis.sqrMagnitude > Mathf.Epsilon;
            if (m_AxisLatched)
                m_ResolvedAxis = m_MeasureAxis.normalized;

            ResolveReference();

            m_PreviousWorldRotation = m_Joint.rotation;
            m_PreviousWorldPosition = m_Joint.position;
        }

        /// <summary>Re-zeroes the joint against the pose it is in right now.</summary>
        [ContextMenu("Zero Angle Here")]
        void ZeroHere() => CaptureRest();

        void LateUpdate()
        {
            if (!isReady)
                return;

            var delta = Quaternion.Inverse(m_RestLocalRotation) * m_Joint.localRotation;
            ResolveAxis(delta);

            var axis = worldAxis;
            var rest = restDirection;
            var current = currentDirection;

            // A degenerate projection means the reference arm has swung onto the axis itself, where
            // the angle is undefined. Holding the last reading beats printing a number that snaps.
            if (rest.sqrMagnitude > k_MinReferenceLength && current.sqrMagnitude > k_MinReferenceLength)
                m_Angle = Vector3.SignedAngle(rest, current, axis);

            // What the projection threw away: the full rotation, minus the part about the axis.
            m_OffAxisAngle = Mathf.Max(0f, Quaternion.Angle(Quaternion.identity, delta) - Mathf.Abs(m_Angle));

            SampleSpeed();
        }

        /// <summary>
        /// Records the joint's current pose as the zero the angle is measured from.
        /// </summary>
        /// <remarks>
        /// The imported FBX pose is the mechanical rest pose, so this runs once on
        /// <see cref="Awake"/>. Call it again only to re-zero against a pose the arm is deliberately
        /// parked in.
        /// </remarks>
        public void CaptureRest()
        {
            if (m_Joint == null)
                return;

            m_RestLocalRotation = m_Joint.localRotation;
            m_AxisLatched = m_MeasureAxis.sqrMagnitude > Mathf.Epsilon;

            if (m_AxisLatched)
                m_ResolvedAxis = m_MeasureAxis.normalized;

            ResolveReference();

            m_PreviousWorldRotation = m_Joint.rotation;
            m_PreviousWorldPosition = m_Joint.position;
            m_Angle = 0f;
            m_OffAxisAngle = 0f;
            m_AngularSpeed = 0f;
            m_LinearSpeed = 0f;
            m_RestCaptured = true;
        }

        /// <summary>
        /// Settles on the axis to measure about, once the joint has moved far enough for its
        /// rotation to actually point somewhere.
        /// </summary>
        /// <remarks>
        /// Latching on the first real motion, rather than re-reading every frame, is what makes the
        /// sign of <see cref="angle"/> mean something. A freshly-read axis always points along the
        /// direction of travel, so the angle would come back positive on the way out and positive
        /// again on the way home.
        /// </remarks>
        void ResolveAxis(Quaternion delta)
        {
            if (m_AxisLatched)
                return;

            delta.ToAngleAxis(out var swing, out var axis);
            if (swing > 180f)
            {
                swing = 360f - swing;
                axis = -axis;
            }

            if (swing < k_AxisLatchAngle || axis.sqrMagnitude < Mathf.Epsilon)
                return;

            m_ResolvedAxis = axis.normalized;
            m_AxisLatched = true;

            // The reference arm was chosen against the placeholder axis, so it may now lie along the
            // real one, where the angle is undefined. Pick it again.
            ResolveReference();
        }

        /// <summary>
        /// Chooses the local direction the angle is measured to: the joint's own structure where it
        /// has any, otherwise whichever cardinal axis is furthest from the hinge.
        /// </summary>
        void ResolveReference()
        {
            var child = m_ReferenceChild != null ? m_ReferenceChild
                : m_Joint.childCount > 0 ? m_Joint.GetChild(0)
                : null;

            var reference = Vector3.zero;
            if (child != null)
            {
                reference = Vector3.ProjectOnPlane(child.localPosition, m_ResolvedAxis);
                m_ReferenceLength = Mathf.Max(k_MinReferenceLength, Vector3.Distance(m_Joint.position, child.position));
            }

            // No usable child, or the child sits straight along the hinge: fall back to the cardinal
            // direction least aligned with the axis, which is guaranteed to project to something.
            if (reference.sqrMagnitude < k_MinReferenceLength)
            {
                var fallback = Mathf.Abs(m_ResolvedAxis.x) < 0.9f ? Vector3.right : Vector3.up;
                reference = Vector3.ProjectOnPlane(fallback, m_ResolvedAxis);
            }

            m_LocalReference = reference.normalized;
        }

        void SampleSpeed()
        {
            // Edit mode ticks irregularly and can report a zero or enormous delta between repaints,
            // which would throw a nonsense speed onto the label. Clamped to a sane frame's worth.
            var dt = Time.deltaTime;
            if (dt <= 0f || dt > 0.5f)
            {
                m_PreviousWorldRotation = m_Joint.rotation;
                m_PreviousWorldPosition = m_Joint.position;
                return;
            }

            var rotation = m_Joint.rotation;
            var position = m_Joint.position;

            var angularRate = Quaternion.Angle(m_PreviousWorldRotation, rotation) / dt;
            var linearRate = Vector3.Distance(m_PreviousWorldPosition, position) / dt;

            // Exponential smoothing, framerate independent so the readout settles at the same pace
            // on a 72Hz standalone headset as on a 90Hz tethered one.
            var t = m_SpeedSmoothing > 0f ? 1f - Mathf.Exp(-dt / m_SpeedSmoothing) : 1f;
            m_AngularSpeed = Mathf.Lerp(m_AngularSpeed, angularRate, t);
            m_LinearSpeed = Mathf.Lerp(m_LinearSpeed, linearRate, t);

            m_PreviousWorldRotation = rotation;
            m_PreviousWorldPosition = position;
        }

        Quaternion ParentRotation() => m_Joint.parent != null ? m_Joint.parent.rotation : Quaternion.identity;

        Vector3 Project(Vector3 direction) => Vector3.ProjectOnPlane(direction, worldAxis);
    }
}
