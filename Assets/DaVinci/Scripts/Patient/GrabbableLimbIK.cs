using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// Lets a user take hold of a limb and place it, by handing control of the arm to a two-bone IK
    /// solve for as long as the target is held.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Dragging a hand bone directly is what tears the mesh. The skin is weighted across the forearm
    /// and the hand, so moving the hand away from the elbow stretches the vertices between them —
    /// nothing has rotated the shoulder and elbow to follow. A two-bone solve fixes that by doing the
    /// rotating: the hand is a goal, and the arm bends to reach it.
    /// </para>
    /// <para>
    /// The grab therefore goes on a <i>target</i>, never on the bone. A bone carrying an
    /// <see cref="XRGrabInteractable"/> would be moved by the interactor and by the animation graph on
    /// two different clocks, which is the same collision that produced every earlier defect here.
    /// </para>
    /// <para>
    /// Constraint weight is the release valve. At zero the animation clip owns the arm completely and
    /// the target simply rides the hand, so the next grab starts from wherever the current pose left it
    /// rather than snapping. It rises only while someone is holding on. That ordering also avoids a
    /// feedback loop: the target only follows the hand during the frames the solver is not moving it.
    /// </para>
    /// </remarks>
    public class GrabbableLimbIK : MonoBehaviour
    {
        [SerializeField, Tooltip("The two-bone constraint this handle drives.")]
        TwoBoneIKConstraint m_Constraint;

        [SerializeField, Tooltip("The constraint's target. This object is what the user actually grabs.")]
        Transform m_Target;

        [SerializeField, Tooltip("The bone at the end of the chain, usually the hand. The target rides this while the solver is idle.")]
        Transform m_TipBone;

        [SerializeField, Tooltip("Grab interactable on the target. Found on this object if left empty.")]
        XRGrabInteractable m_Interactable;

        [SerializeField, Tooltip("Keep the limb where the user left it after they let go. Turn off to have it settle back into the animated pose.")]
        bool m_HoldAfterRelease = true;

        [SerializeField, Range(0.5f, 20f), Tooltip("How quickly control passes between the animation and the hand, in weight per second.")]
        float m_BlendSpeed = 6f;

        float m_Weight;
        bool m_Engaged;

        /// <summary>How much of the arm the IK currently owns, 0 animation to 1 hand.</summary>
        public float Weight => m_Weight;

        /// <summary>Whether the limb is currently being posed by hand rather than by the clip.</summary>
        public bool IsEngaged => m_Engaged;

        void Awake()
        {
            if (m_Interactable == null)
                m_Interactable = GetComponent<XRGrabInteractable>();

            if (m_Target == null)
                m_Target = transform;

            if (m_Constraint != null)
                m_Constraint.weight = 0f;
        }

        void LateUpdate()
        {
            if (m_Constraint == null || m_Target == null)
                return;

            var held = m_Interactable != null && m_Interactable.isSelected;
            if (held)
                m_Engaged = true;

            var goal = held || (m_Engaged && m_HoldAfterRelease) ? 1f : 0f;
            m_Weight = Mathf.MoveTowards(m_Weight, goal, m_BlendSpeed * Time.deltaTime);
            m_Constraint.weight = m_Weight;

            // Only while the solver is idle. Following the hand at any other time would be a loop:
            // the target chasing a bone the constraint is moving toward that same target.
            if (!held && m_Weight <= 0.001f && m_TipBone != null)
                m_Target.SetPositionAndRotation(m_TipBone.position, m_TipBone.rotation);
        }

        /// <summary>
        /// Hands the limb back to the animation, blending out so it settles rather than snaps.
        /// Worth calling when the character changes pose.
        /// </summary>
        public void ReturnToAnimation() => m_Engaged = false;

        /// <summary>Wires the handle up. Used by the setup command.</summary>
        public void Bind(TwoBoneIKConstraint constraint, Transform target, Transform tipBone)
        {
            m_Constraint = constraint;
            m_Target = target;
            m_TipBone = tipBone;
        }
    }
}
