using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// Makes a single IK target grabbable, so a user can take hold of a limb and place it by hand.
    /// </summary>
    /// <remarks>
    /// The pose controller writes to these targets every frame, which would fight the interactor for
    /// control. This hands the target over for the duration of the grab and gives it back on
    /// release, at which point the controller folds the new position into its rest pose so the limb
    /// stays where it was left.
    /// </remarks>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class PatientLimbHandle : MonoBehaviour
    {
        [SerializeField, Tooltip("Pose controller that owns this target. Found on a parent if left empty.")]
        PatientPoseController m_Controller;

        XRGrabInteractable m_Interactable;

        void Awake()
        {
            m_Interactable = GetComponent<XRGrabInteractable>();

            if (m_Controller == null)
                m_Controller = GetComponentInParent<PatientPoseController>();
        }

        void OnEnable()
        {
            m_Interactable.selectEntered.AddListener(OnSelectEntered);
            m_Interactable.selectExited.AddListener(OnSelectExited);
        }

        void OnDisable()
        {
            m_Interactable.selectEntered.RemoveListener(OnSelectEntered);
            m_Interactable.selectExited.RemoveListener(OnSelectExited);

            // Never leave the limb stranded if this is torn down mid-grab.
            if (m_Controller != null)
                m_Controller.EndManualControl(transform);
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (m_Controller != null)
                m_Controller.BeginManualControl(transform);
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (m_Controller != null)
                m_Controller.EndManualControl(transform);
        }
    }
}
