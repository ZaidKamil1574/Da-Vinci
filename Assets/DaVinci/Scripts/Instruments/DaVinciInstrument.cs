using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// A swappable tool that can be fitted to an arm, mirroring the modular instruments a real
    /// da Vinci carries.
    /// </summary>
    /// <remarks>
    /// The marker is what lets the mount tell a surgical instrument from any other grabbable object
    /// in the room. Without it a socket at the arm tip would happily accept a coffee cup.
    /// </remarks>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class DaVinciInstrument : MonoBehaviour
    {
        [SerializeField, Tooltip("Name shown when this instrument is fitted.")]
        string m_DisplayName;

        [SerializeField, Tooltip("The end that seats into the arm. Should sit at the base of the shaft, pointing back down it.")]
        Transform m_MountPoint;

        XRGrabInteractable m_Interactable;

        /// <summary>Name shown when this instrument is fitted.</summary>
        public string displayName => string.IsNullOrEmpty(m_DisplayName) ? name : m_DisplayName;

        /// <summary>The end that seats into the arm.</summary>
        public Transform mountPoint => m_MountPoint != null ? m_MountPoint : transform;

        /// <summary>The grab interactable this instrument is picked up by.</summary>
        public XRGrabInteractable interactable
        {
            get
            {
                if (m_Interactable == null)
                    m_Interactable = GetComponent<XRGrabInteractable>();

                return m_Interactable;
            }
        }

        /// <summary>Assigns the seat point, and makes the grab use it as its attach transform.</summary>
        public void SetMountPoint(Transform mountPoint)
        {
            m_MountPoint = mountPoint;

            // The socket aligns the interactable's attach transform with its own, so the seat point
            // has to be that attach transform. Left at the default the tool would socket by its
            // centre and hang through the arm.
            if (interactable != null)
                interactable.attachTransform = mountPoint;
        }
    }
}
