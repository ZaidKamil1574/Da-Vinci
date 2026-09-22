using UnityEngine;
using UnityEngine.XR.Content.Interaction;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// Turns one joint of the Da Vinci model from an <see cref="XRLever"/>: lever up drives the
    /// joint one way, lever down drives it back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No networking code, deliberately.</b> <c>NetworkXRLever</c> already replicates the
    /// lever's boolean, and every client derives the same angle from it here, so the joint arrives
    /// at the same pose everywhere without a second thing to keep in sync. Adding a
    /// <c>NetworkVariable</c> for the angle would replicate a value that is already implied by one
    /// being replicated, and the two would disagree whenever a packet for one arrived before the
    /// other. Nothing in this file reads or writes any network property of the lever.
    /// </para>
    /// <para>
    /// <b>Why the lever's value is polled rather than subscribed to.</b> <c>NetworkXRLever</c>
    /// temporarily detaches its own listeners while it applies a remote value, so an event-driven
    /// reading has to be right about which callbacks survive that. Reading <see cref="XRLever.value"/>
    /// each frame is correct regardless of how the value got there — a local hand, a remote client,
    /// or a value restored on spawn — and costs one boolean read.
    /// </para>
    /// <para>
    /// <b>Why the rotation is written in <c>LateUpdate</c>.</b> Unity evaluates the Animation
    /// Rigging graph in the animation pass, between <c>Update</c> and <c>LateUpdate</c>. Any
    /// constraint that has this joint in its chain rewrites the joint there, so a rotation written
    /// in <c>Update</c> would be overwritten before it was ever drawn. Writing afterwards makes
    /// this component the last writer of the frame.
    /// </para>
    /// <para>
    /// The lever is a rate control, not a position control: it names a direction and the joint
    /// travels there at <see cref="m_Speed"/>, stopping at the limit. Mapping the lever's two
    /// positions straight onto two angles would make the joint jump between them.
    /// </para>
    /// </remarks>
    // After the rig has solved, so this joint's rotation survives to the end of the frame.
    [DefaultExecutionOrder(210)]
    public class LeverDrivenRotator : MonoBehaviour
    {
        [SerializeField, Tooltip("The lever that steers this joint. Its 'on' position drives toward the up angle, its 'off' position toward the down angle.")]
        XRLever m_Lever;

        [SerializeField, Tooltip("The joint to turn, for example ROTARY MECHANISM_1.002.")]
        Transform m_Joint;

        [Header("Motion")]
        [SerializeField, Tooltip("Axis the joint turns about, in its own local space at the rest pose.")]
        Vector3 m_RotationAxis = Vector3.up;

        [SerializeField, Tooltip("Angle reached while the lever is up, in degrees from the rest pose.")]
        float m_UpAngle = 60f;

        [SerializeField, Tooltip("Angle reached while the lever is down, in degrees from the rest pose.")]
        float m_DownAngle = -60f;

        [SerializeField, Tooltip("How fast the joint travels between the two angles, in degrees per second.")]
        float m_Speed = 30f;

        [SerializeField, Tooltip("Swap which lever position drives which way, without having to negate both angles.")]
        bool m_Invert;

        [SerializeField, Tooltip("Hold the joint still whenever nobody is holding the lever. With this off the joint keeps travelling to the end of its range after the hand lets go.")]
        bool m_HoldWhenReleased = true;

        Quaternion m_RestLocalRotation = Quaternion.identity;
        Vector3 m_Axis = Vector3.up;
        float m_Angle;
        float m_AngularSpeed;

        /// <summary>Current rotation away from the rest pose, in degrees.</summary>
        public float angle => m_Angle;

        /// <summary>The angle the joint is currently travelling toward, in degrees.</summary>
        public float goalAngle => (m_Lever != null && m_Lever.value) != m_Invert ? m_UpAngle : m_DownAngle;

        /// <summary>How fast the joint is turning right now, in degrees per second.</summary>
        public float angularSpeed => m_AngularSpeed;

        /// <summary>Whether the joint is still travelling toward its goal.</summary>
        public bool isMoving => !Mathf.Approximately(m_Angle, goalAngle);

        /// <summary>The joint this lever turns.</summary>
        public Transform joint => m_Joint;

        void Awake()
        {
            if (m_Lever == null || m_Joint == null)
            {
                Debug.LogError(
                    $"[Da Vinci] {name} needs both a lever and a joint to drive. Disabling.", this);
                enabled = false;
                return;
            }

            // The imported pose is the mechanical zero. Composing every frame from this, rather
            // than accumulating onto the live rotation, keeps the joint from drifting away from it
            // over a long session.
            m_RestLocalRotation = m_Joint.localRotation;
            m_Axis = m_RotationAxis.sqrMagnitude > Mathf.Epsilon ? m_RotationAxis.normalized : Vector3.up;

            // Start where the lever already is, so the joint does not sweep across its whole travel
            // on the first frame just because the lever was authored in the 'on' position.
            m_Angle = goalAngle;
            Apply();
        }

        void OnValidate()
        {
            m_Speed = Mathf.Max(0f, m_Speed);
        }

        void LateUpdate()
        {
            // A lever that is not being held is not a command to keep going. Without this the joint
            // carries on to whichever end of its travel the lever was last left at, so the arm can
            // never be parked anywhere in between — letting go anywhere short of the limit still
            // ends with it fully down.
            if (m_HoldWhenReleased && m_Lever != null && !m_Lever.isSelected)
            {
                m_AngularSpeed = 0f;
                Apply();
                return;
            }

            var previous = m_Angle;
            m_Angle = Mathf.MoveTowards(m_Angle, goalAngle, m_Speed * Time.deltaTime);

            m_AngularSpeed = Time.deltaTime > 0f ? Mathf.Abs(m_Angle - previous) / Time.deltaTime : 0f;

            Apply();
        }

        /// <summary>Returns the joint to the pose it was imported in.</summary>
        public void ResetToRest()
        {
            m_Angle = 0f;
            m_AngularSpeed = 0f;
            Apply();
        }

        void Apply()
        {
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

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(pivot - axis * 0.05f, pivot + axis * 0.05f);
            Gizmos.DrawWireSphere(pivot, 0.006f);
        }
    }
}
