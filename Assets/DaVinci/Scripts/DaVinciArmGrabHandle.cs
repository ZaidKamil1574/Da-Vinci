using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// The grab point on an instrument. While held, the arm's IK target follows the hand, which is
    /// how the surgeon walks each arm to its port during setup.
    /// </summary>
    /// <remarks>
    /// This deliberately does not derive from <c>XRGrabInteractable</c>. That component moves the
    /// object it is attached to, which would fight the solver for control of the same transforms.
    /// Here the hand only supplies a goal position and the arm reaches for it, so the mechanism
    /// stays the single owner of its own pose.
    /// </remarks>
    [RequireComponent(typeof(Collider))]
    public class DaVinciArmGrabHandle : XRBaseInteractable
    {
        [SerializeField, Tooltip("The arm this handle drives.")]
        DaVinciArm m_Arm;

        [SerializeField, Tooltip("How quickly the IK goal chases the hand. Lower values feel heavier, which suits a machine of this mass.")]
        float m_FollowSharpness = 12f;

        [SerializeField, Tooltip("Ports this handle will consider seating into. Leave empty to search the scene on start.")]
        DaVinciTrocarPort[] m_Ports;

        Vector3 m_GoalPosition;
        bool m_HasGoal;

        /// <summary>The arm driven by this handle.</summary>
        public DaVinciArm arm => m_Arm;

        protected override void Awake()
        {
            base.Awake();

            if (m_Arm == null)
                m_Arm = GetComponentInParent<DaVinciArm>();

            if (m_Ports == null || m_Ports.Length == 0)
                m_Ports = FindObjectsByType<DaVinciTrocarPort>(FindObjectsInactive.Exclude);
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);

            // Start the goal at the tip rather than at the hand, so grabbing does not snap the arm.
            m_GoalPosition = m_Arm != null && m_Arm.toolTip != null ? m_Arm.toolTip.position : transform.position;
            m_HasGoal = true;
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            m_HasGoal = false;

            if (m_Arm == null || m_Arm.isDocked)
                return;

            // Releasing inside a port is what actually seats the arm.
            for (var i = 0; i < m_Ports.Length; i++)
            {
                if (m_Ports[i] != null && m_Ports[i].CanCapture(m_Arm))
                {
                    m_Arm.Dock(m_Ports[i]);
                    return;
                }
            }
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);

            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic || !m_HasGoal || m_Arm == null)
                return;

            if (interactorsSelecting.Count == 0)
                return;

            var hand = interactorsSelecting[0].GetAttachTransform(this);
            if (hand == null)
                return;

            // Exponential smoothing, framerate independent so the arm feels the same on a 72Hz
            // standalone headset as on a 90Hz tethered one.
            var t = 1f - Mathf.Exp(-m_FollowSharpness * Time.deltaTime);
            m_GoalPosition = Vector3.Lerp(m_GoalPosition, hand.position, t);

            m_Arm.SolveTo(m_GoalPosition);
        }

        void OnDrawGizmosSelected()
        {
            if (!m_HasGoal)
                return;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(m_GoalPosition, 0.01f);
        }
    }
}
