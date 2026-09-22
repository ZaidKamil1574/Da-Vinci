using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// Raised when an instrument is fitted to or pulled from a <see cref="DaVinciInstrumentMount"/>.
    /// </summary>
    /// <remarks><see cref="UnityEvent{T0}"/> is abstract, so it needs a concrete subclass to serialize.</remarks>
    [Serializable]
    public class DaVinciInstrumentEvent : UnityEvent<DaVinciInstrument> { }

    /// <summary>
    /// The coupling at an arm tip that a swappable instrument seats into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built on <see cref="XRSocketInteractor"/> rather than written from scratch, because the
    /// socket already does the hard part: it takes the tool off whichever hand is holding it,
    /// aligns it, and keeps it aligned while the arm moves. What is added here is the part specific
    /// to a surgical arm — only accepting instruments, and standing the fixed forceps down while a
    /// tool is fitted so the two do not occupy the same space.
    /// </para>
    /// <para>
    /// The mount deliberately rides on the arm rather than in the world. A socket parented to the
    /// tip inherits the IK chain's motion, so a fitted tool tracks the arm with no follow code and
    /// no question of update order.
    /// </para>
    /// </remarks>
    public class DaVinciInstrumentMount : XRSocketInteractor
    {
        [SerializeField, Tooltip("The arm's fixed instrument, hidden while a swappable tool is fitted so the two do not overlap.")]
        Renderer m_DefaultInstrument;

        [SerializeField, Tooltip("Hide the fixed instrument whenever a tool is fitted. Turn off to have tools mount alongside it instead of replacing it.")]
        bool m_HideDefaultWhenFitted = true;

        [SerializeField, Tooltip("Raised when an instrument seats in this mount.")]
        DaVinciInstrumentEvent m_Fitted = new DaVinciInstrumentEvent();

        [SerializeField, Tooltip("Raised when an instrument leaves this mount.")]
        DaVinciInstrumentEvent m_Removed = new DaVinciInstrumentEvent();

        /// <summary>The instrument currently seated here, or <see langword="null"/>.</summary>
        public DaVinciInstrument fittedInstrument { get; private set; }

        /// <summary>Whether a tool is currently fitted.</summary>
        public bool hasInstrument => fittedInstrument != null;

        /// <summary>Raised when an instrument seats in this mount.</summary>
        public DaVinciInstrumentEvent fitted => m_Fitted;

        /// <summary>Raised when an instrument leaves this mount.</summary>
        public DaVinciInstrumentEvent removed => m_Removed;

        /// <inheritdoc />
        public override bool CanSelect(IXRSelectInteractable interactable) =>
            base.CanSelect(interactable) && IsInstrument(interactable);

        /// <inheritdoc />
        public override bool CanHover(IXRHoverInteractable interactable) =>
            base.CanHover(interactable) && IsInstrument(interactable);

        /// <inheritdoc />
        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);

            fittedInstrument = GetInstrument(args.interactableObject);
            ApplyDefaultVisibility();
            m_Fitted.Invoke(fittedInstrument);
        }

        /// <inheritdoc />
        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);

            var leaving = fittedInstrument;
            fittedInstrument = null;
            ApplyDefaultVisibility();
            m_Removed.Invoke(leaving);
        }

        void ApplyDefaultVisibility()
        {
            if (m_DefaultInstrument == null)
                return;

            m_DefaultInstrument.enabled = !m_HideDefaultWhenFitted || fittedInstrument == null;
        }

        /// <summary>Points the mount at the arm's fixed instrument.</summary>
        public void SetDefaultInstrument(Renderer instrument)
        {
            m_DefaultInstrument = instrument;
            ApplyDefaultVisibility();
        }

        static bool IsInstrument(IXRInteractable interactable) => GetInstrument(interactable) != null;

        static DaVinciInstrument GetInstrument(IXRInteractable interactable)
        {
            var target = interactable?.transform;
            return target != null ? target.GetComponent<DaVinciInstrument>() : null;
        }
    }
}
