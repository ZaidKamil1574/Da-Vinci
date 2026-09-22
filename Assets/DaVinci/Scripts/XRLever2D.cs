using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// Raised when an <see cref="XRLever2D"/> tilts. The value is each axis in the range -1 to 1.
    /// </summary>
    [System.Serializable]
    public class Lever2DValueChangeEvent : UnityEvent<Vector2> { }

    /// <summary>
    /// A lever that tilts on two axes at once and reports a continuous value, rather than snapping
    /// between on and off on a single axis like <c>XRLever</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a separate component rather than a change to <c>XRLever</c> on purpose. That lever's
    /// value is a <see cref="bool"/>, and <c>NetworkXRLever</c> replicates it as a bool. Widening it
    /// in place would break every existing lever in the template scene and its network wrapper along
    /// with them.
    /// </para>
    /// <para>
    /// X tilt is the forward/back axis and reports in <c>value.y</c>; Z tilt is the left/right axis
    /// and reports in <c>value.x</c>, so the output reads like a thumbstick.
    /// </para>
    /// </remarks>
    public class XRLever2D : XRBaseInteractable
    {
        [SerializeField, Tooltip("The object that is visually grabbed and tilted.")]
        Transform m_Handle;

        [SerializeField, Tooltip("Maximum tilt away from centre on each axis, in degrees.")]
        float m_MaxTiltAngle = 35f;

        [SerializeField, Range(0f, 0.5f), Tooltip("Fraction of travel around centre that reads as zero, to stop the arm creeping when the lever is nearly upright.")]
        float m_DeadZone = 0.08f;

        [SerializeField, Tooltip("Return the lever to centre when released.")]
        bool m_SelfCentring = true;

        [SerializeField, Tooltip("How quickly a released lever returns to centre, in units per second.")]
        float m_CentringSpeed = 4f;

        [SerializeField]
        Lever2DValueChangeEvent m_OnValueChange = new Lever2DValueChangeEvent();

        IXRSelectInteractor m_Interactor;
        Vector2 m_Value;

        /// <summary>The current tilt, each axis in the range -1 to 1. X is left/right, Y is forward/back.</summary>
        public Vector2 value
        {
            get => m_Value;
            set => SetValue(value, true);
        }

        /// <summary>Raised whenever the tilt changes.</summary>
        public Lever2DValueChangeEvent onValueChange => m_OnValueChange;

        /// <summary>The transform tilted to show the lever's position.</summary>
        public Transform handle => m_Handle;

        protected override void OnEnable()
        {
            base.OnEnable();
            selectEntered.AddListener(StartGrab);
            selectExited.AddListener(EndGrab);
            SetHandleAngle(m_Value);
        }

        protected override void OnDisable()
        {
            selectEntered.RemoveListener(StartGrab);
            selectExited.RemoveListener(EndGrab);
            base.OnDisable();
        }

        void StartGrab(SelectEnterEventArgs args) => m_Interactor = args.interactorObject;

        void EndGrab(SelectExitEventArgs args) => m_Interactor = null;

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);

            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic)
                return;

            if (isSelected && m_Interactor != null)
            {
                SetValue(ReadHandPosition(), false);
            }
            else if (m_SelfCentring && m_Value != Vector2.zero)
            {
                SetValue(Vector2.MoveTowards(m_Value, Vector2.zero, m_CentringSpeed * Time.deltaTime), false);
            }
        }

        /// <summary>
        /// Converts the hand's offset from the lever's pivot into a tilt on both axes.
        /// </summary>
        /// <remarks>
        /// Mirrors how XRLever reads its hand direction — local space, then an angle from the
        /// component that lies in the plane of travel — but keeps both axes instead of zeroing one.
        /// </remarks>
        Vector2 ReadHandPosition()
        {
            var handPosition = m_Interactor.GetAttachTransform(this).position;
            var pivot = m_Handle != null ? m_Handle.position : transform.position;
            var direction = transform.InverseTransformDirection(handPosition - pivot);

            // Below the pivot the angles flip sign and the lever snaps; treat it as centred instead.
            if (direction.y <= 0.0001f)
                return m_Value;

            var forwardBack = Mathf.Atan2(direction.z, direction.y) * Mathf.Rad2Deg;
            var leftRight = Mathf.Atan2(-direction.x, direction.y) * Mathf.Rad2Deg;

            return new Vector2(
                Mathf.Clamp(leftRight / m_MaxTiltAngle, -1f, 1f),
                Mathf.Clamp(forwardBack / m_MaxTiltAngle, -1f, 1f));
        }

        void SetValue(Vector2 newValue, bool forceRotation)
        {
            newValue = new Vector2(ApplyDeadZone(newValue.x), ApplyDeadZone(newValue.y));

            if (newValue == m_Value && !forceRotation)
                return;

            m_Value = newValue;
            SetHandleAngle(m_Value);
            m_OnValueChange.Invoke(m_Value);
        }

        /// <summary>
        /// Flattens small tilts to zero and rescales what remains, so the lever still reaches full
        /// travel instead of losing the dead zone off the top of its range.
        /// </summary>
        float ApplyDeadZone(float axis)
        {
            var magnitude = Mathf.Abs(axis);
            if (magnitude <= m_DeadZone)
                return 0f;

            return Mathf.Sign(axis) * Mathf.InverseLerp(m_DeadZone, 1f, magnitude);
        }

        void SetHandleAngle(Vector2 tilt)
        {
            if (m_Handle == null)
                return;

            m_Handle.localRotation = Quaternion.Euler(tilt.y * m_MaxTiltAngle, 0f, -tilt.x * m_MaxTiltAngle);
        }
    }
}
