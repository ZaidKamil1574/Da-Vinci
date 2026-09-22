using UnityEngine;
using UnityEngine.XR.Content.Interaction;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// Turns one joint of the Da Vinci model from an <see cref="XRKnob"/> dial: the dial's position
    /// maps straight onto an angle, so turning it aims the joint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No networking code, deliberately.</b> Nothing here reads or writes any network property
    /// of the dial, and nothing is added to it. <c>NetworkXRKnob</c> already replicates the dial's
    /// value, and every client derives the same angle from it here, so the joint arrives at the
    /// same pose everywhere without a second thing to keep in sync. Replicating the angle as well
    /// would duplicate a value that is already implied by one being replicated, and the two would
    /// disagree whenever a packet for one arrived before the other.
    /// </para>
    /// <para>
    /// <b>Why the dial's value is polled rather than subscribed to.</b> The networked wrapper
    /// detaches its own listeners while applying a remote value, so an event-driven reading has to
    /// be right about which callbacks survive that. Reading <see cref="XRKnob.value"/> each frame
    /// is correct however the value got there — a local hand, a remote client, or a value restored
    /// on spawn — and costs one float read.
    /// </para>
    /// <para>
    /// <b>Why the rotation is written in <c>LateUpdate</c>.</b> Unity evaluates the Animation
    /// Rigging graph between <c>Update</c> and <c>LateUpdate</c>, and HAND_4.002 sits inside a
    /// chain that a constraint rewrites there. A rotation written in <c>Update</c> would be
    /// overwritten before it was ever drawn. Writing afterwards makes this the last writer of the
    /// frame.
    /// </para>
    /// <para>
    /// Unlike the lever, a dial is a <i>position</i> control: it names an angle rather than a
    /// direction to travel in, so the value maps straight through. The optional speed limit only
    /// exists to take the edge off a value that arrives as a jump.
    /// </para>
    /// </remarks>
    // After the rig has solved, so this joint's rotation survives to the end of the frame.
    [DefaultExecutionOrder(210)]
    public class KnobDrivenRotator : MonoBehaviour
    {
        [SerializeField, Tooltip("The dial that aims this joint. Read only; nothing on it is modified.")]
        XRKnob m_Knob;

        [SerializeField, Tooltip("The joint to turn, for example HAND_4.002.")]
        Transform m_Joint;

        [Header("Motion")]
        [SerializeField, Tooltip("Axis the joint turns about, in its own local space at the rest pose.")]
        Vector3 m_RotationAxis = Vector3.up;

        [SerializeField, Tooltip("Angle at the dial's zero position, in degrees from the rest pose.")]
        float m_MinAngle = -90f;

        [SerializeField, Tooltip("Angle at the dial's full position, in degrees from the rest pose.")]
        float m_MaxAngle = 90f;

        [SerializeField, Tooltip("Degrees per second the joint may travel. Zero follows the dial exactly, which is what a position control should normally do.")]
        float m_MaxSpeed;

        [SerializeField, Tooltip("Swap which end of the dial's travel maps to which angle, without having to negate both.")]
        bool m_Invert;

        Quaternion m_RestLocalRotation = Quaternion.identity;
        Vector3 m_Axis = Vector3.up;
        float m_Angle;

        /// <summary>Current rotation away from the rest pose, in degrees.</summary>
        public float angle => m_Angle;

        /// <summary>The angle the dial is currently asking for, in degrees.</summary>
        public float goalAngle
        {
            get
            {
                if (m_Knob == null)
                    return 0f;

                var t = Mathf.Clamp01(m_Invert ? 1f - m_Knob.value : m_Knob.value);
                return Mathf.Lerp(m_MinAngle, m_MaxAngle, t);
            }
        }

        /// <summary>The joint this dial turns.</summary>
        public Transform joint => m_Joint;

        void Awake()
        {
            if (m_Knob == null || m_Joint == null)
            {
                Debug.LogError($"[Da Vinci] {name} needs both a dial and a joint to drive. Disabling.", this);
                enabled = false;
                return;
            }

            // The imported pose is the mechanical zero. Composing every frame from this, rather
            // than accumulating onto the live rotation, keeps the joint from drifting away from it
            // over a long session.
            m_RestLocalRotation = m_Joint.localRotation;
            m_Axis = m_RotationAxis.sqrMagnitude > Mathf.Epsilon ? m_RotationAxis.normalized : Vector3.up;

            // Start where the dial already is, so the joint does not sweep across its travel on the
            // first frame just because the dial was authored part-turned.
            m_Angle = goalAngle;
            Apply();
        }

        void OnValidate()
        {
            m_MaxSpeed = Mathf.Max(0f, m_MaxSpeed);
        }

        void LateUpdate()
        {
            m_Angle = m_MaxSpeed > 0f
                ? Mathf.MoveTowards(m_Angle, goalAngle, m_MaxSpeed * Time.deltaTime)
                : goalAngle;

            Apply();
        }

        /// <summary>Returns the joint to the pose it was imported in.</summary>
        public void ResetToRest()
        {
            m_Angle = 0f;
            Apply();
        }

        void Apply()
        {
            if (m_Joint != null)
                m_Joint.localRotation = m_RestLocalRotation * Quaternion.AngleAxis(m_Angle, m_Axis);
        }

        void OnDrawGizmosSelected()
        {
            if (m_Joint == null)
                return;

            var pivot = m_Joint.position;
            var axis = m_Joint.TransformDirection(m_RotationAxis.sqrMagnitude > Mathf.Epsilon
                ? m_RotationAxis.normalized
                : Vector3.up);

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(pivot - axis * 0.05f, pivot + axis * 0.05f);
            Gizmos.DrawWireSphere(pivot, 0.006f);
        }
    }
}
