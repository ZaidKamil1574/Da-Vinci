using System;
using UnityEngine;
using UnityEngine.Events;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// Raised when an arm seats in or leaves a <see cref="DaVinciTrocarPort"/>.
    /// </summary>
    /// <remarks><see cref="UnityEvent{T0}"/> is abstract, so it needs a concrete subclass to serialize.</remarks>
    [Serializable]
    public class DaVinciArmEvent : UnityEvent<DaVinciArm> { }

    /// <summary>
    /// An incision site an arm can be docked to. While an arm is docked its cannula is held at
    /// <see cref="remoteCentrePosition"/> and the instrument pivots about that point.
    /// </summary>
    public class DaVinciTrocarPort : MonoBehaviour
    {
        [SerializeField, Tooltip("The incision point. Leave empty to use this object's own transform.")]
        Transform m_RemoteCentre;

        [SerializeField, Tooltip("How close the cannula must be before the port will accept the arm, in metres.")]
        float m_CaptureRadius = 0.04f;

        [SerializeField, Range(0f, 90f), Tooltip("How closely the instrument must line up with the port axis before it will seat, in degrees.")]
        float m_CaptureAngle = 25f;

        [SerializeField, Tooltip("Raised when an arm seats in this port.")]
        DaVinciArmEvent m_Docked = new DaVinciArmEvent();

        [SerializeField, Tooltip("Raised when an arm leaves this port.")]
        DaVinciArmEvent m_Undocked = new DaVinciArmEvent();

        DaVinciArm m_OccupyingArm;

        /// <summary>World-space incision point the cannula is held at.</summary>
        public Vector3 remoteCentrePosition => m_RemoteCentre != null ? m_RemoteCentre.position : transform.position;

        /// <summary>The axis the instrument is expected to enter along.</summary>
        public Vector3 approachAxis => m_RemoteCentre != null ? m_RemoteCentre.forward : transform.forward;

        /// <summary>The arm currently seated here, or <see langword="null"/>.</summary>
        public DaVinciArm occupyingArm => m_OccupyingArm;

        /// <summary>Whether another arm already occupies this port.</summary>
        public bool isOccupied => m_OccupyingArm != null;

        /// <summary>Raised when an arm seats in this port.</summary>
        public DaVinciArmEvent docked => m_Docked;

        /// <summary>Raised when an arm leaves this port.</summary>
        public DaVinciArmEvent undocked => m_Undocked;

        /// <summary>
        /// Whether <paramref name="arm"/> is close enough and square enough to seat right now.
        /// </summary>
        /// <remarks>
        /// Both tests matter. Position alone would let an instrument seat while lying across the
        /// patient, which reads as obviously wrong even to someone who has never seen the machine.
        /// </remarks>
        public bool CanCapture(DaVinciArm arm)
        {
            if (arm == null || arm.remoteCentre == null || isOccupied)
                return false;

            if (Vector3.Distance(arm.remoteCentre.position, remoteCentrePosition) > m_CaptureRadius)
                return false;

            var instrumentAxis = arm.toolTip != null
                ? arm.toolTip.position - arm.remoteCentre.position
                : arm.remoteCentre.forward;

            if (instrumentAxis.sqrMagnitude < Mathf.Epsilon)
                return false;

            return Vector3.Angle(instrumentAxis.normalized, approachAxis) <= m_CaptureAngle;
        }

        internal void OnArmDocked(DaVinciArm arm)
        {
            m_OccupyingArm = arm;
            m_Docked.Invoke(arm);
        }

        internal void OnArmUndocked(DaVinciArm arm)
        {
            if (m_OccupyingArm != arm)
                return;

            m_OccupyingArm = null;
            m_Undocked.Invoke(arm);
        }

        void OnDrawGizmos()
        {
            var centre = remoteCentrePosition;

            Gizmos.color = isOccupied ? Color.green : new Color(1f, 0.8f, 0.2f);
            Gizmos.DrawWireSphere(centre, m_CaptureRadius);

            Gizmos.color = new Color(1f, 1f, 1f, 0.6f);
            Gizmos.DrawLine(centre, centre + approachAxis * 0.1f);
        }
    }
}
