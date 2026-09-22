using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// Freezes the Da Vinci machine, and everything reading from it, on a controller button.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pausing holds the arms' inputs still rather than stopping the solver.</b> The arms are
    /// posed by ChainIK constraints that chase the ball targets, so the honest way to stop the
    /// machine is to stop what it is chasing: the targets are pinned where they stand and the
    /// lever-driven joint stops advancing. Disabling the rig instead would drop every arm out of
    /// its solved pose and collapse the model back to the imported one.
    /// </para>
    /// <para>
    /// <see cref="Time.timeScale"/> would have been one line, but it stops the whole application:
    /// the other player's avatar, the networking, the menus, and every other client if it were
    /// replicated. This is a local instrument freeze, not a world pause.
    /// </para>
    /// <para>
    /// The action is built in code rather than pulled from an asset so the button works in a fresh
    /// scene with nothing wired. It is bound to the right controller's A button, with the left
    /// controller's X, a gamepad's south button and the space bar alongside it so the pause can be
    /// tested without putting a headset on.
    /// </para>
    /// </remarks>
    // Ahead of the rotator and the readouts, so a frame that starts paused is already paused by the
    // time anything acts on it.
    [DefaultExecutionOrder(190)]
    public class DaVinciPauseControl : MonoBehaviour
    {
        /// <summary>
        /// The control in the scene, or <see langword="null"/> when there is none.
        /// </summary>
        /// <remarks>
        /// A static handle so the graphs and the lever drive can ask whether they are paused without
        /// every one of them needing a reference dragged into it. Readers should go through
        /// <see cref="IsPaused"/>, which copes with there being no control at all.
        /// </remarks>
        public static DaVinciPauseControl active { get; private set; }

        [SerializeField, Tooltip("Optional. An action from your own input asset. Leave empty to use the built-in A-button binding.")]
        InputActionProperty m_PauseAction;

        [SerializeField, Tooltip("The IK targets to pin while paused. Normally the two balls the arms reach for.")]
        List<Transform> m_Targets = new List<Transform>();

        [SerializeField, Tooltip("Lever-driven joints that stop advancing while paused.")]
        List<LeverDrivenRotator> m_Rotators = new List<LeverDrivenRotator>();

        [SerializeField, Tooltip("Start the scene paused.")]
        bool m_StartPaused;

        InputAction m_Fallback;
        readonly List<Pose> m_HeldPoses = new List<Pose>();
        readonly List<bool> m_HeldKinematic = new List<bool>();
        bool m_Paused;

        /// <summary>Whether the machine is currently frozen.</summary>
        public bool isPaused => m_Paused;

        /// <summary>Whether anything in the scene is holding the machine frozen.</summary>
        public static bool IsPaused => active != null && active.m_Paused;

        void OnEnable()
        {
            active = this;

            if (m_PauseAction.reference != null || m_PauseAction.action != null)
            {
                m_PauseAction.action.performed += OnPressed;
                m_PauseAction.action.Enable();
            }
            else
            {
                m_Fallback = new InputAction("Da Vinci Pause", InputActionType.Button);
                m_Fallback.AddBinding("<XRController>{RightHand}/primaryButton");
                m_Fallback.AddBinding("<XRController>{LeftHand}/primaryButton");
                m_Fallback.AddBinding("<Gamepad>/buttonSouth");
                m_Fallback.AddBinding("<Keyboard>/space");
                m_Fallback.performed += OnPressed;
                m_Fallback.Enable();
            }

            if (m_StartPaused)
                SetPaused(true);
        }

        void OnDisable()
        {
            if (m_Paused)
                SetPaused(false);

            if (m_Fallback != null)
            {
                m_Fallback.performed -= OnPressed;
                m_Fallback.Disable();
                m_Fallback.Dispose();
                m_Fallback = null;
            }
            else if (m_PauseAction.action != null)
            {
                m_PauseAction.action.performed -= OnPressed;
            }

            if (active == this)
                active = null;
        }

        void LateUpdate()
        {
            if (!m_Paused)
                return;

            // Held every frame, not just on the frame the pause began: a hand can still grab a
            // target while paused, and without this it would drag the arm along with it.
            for (var i = 0; i < m_Targets.Count && i < m_HeldPoses.Count; i++)
            {
                if (m_Targets[i] != null)
                    m_Targets[i].SetPositionAndRotation(m_HeldPoses[i].position, m_HeldPoses[i].rotation);
            }
        }

        void OnPressed(InputAction.CallbackContext _) => Toggle();

        /// <summary>Flips between running and frozen. Safe to wire to a UI button.</summary>
        public void Toggle() => SetPaused(!m_Paused);

        /// <summary>Freezes or releases the machine.</summary>
        public void SetPaused(bool paused)
        {
            if (paused == m_Paused)
                return;

            m_Paused = paused;

            if (paused)
                Hold();
            else
                Release();
        }

        void Hold()
        {
            m_HeldPoses.Clear();
            m_HeldKinematic.Clear();

            for (var i = 0; i < m_Targets.Count; i++)
            {
                var target = m_Targets[i];
                if (target == null)
                {
                    m_HeldPoses.Add(default);
                    m_HeldKinematic.Add(false);
                    continue;
                }

                m_HeldPoses.Add(new Pose(target.position, target.rotation));

                // Kinematic as well as pinned: a ball left dynamic would keep accumulating gravity
                // while held, and shoot off the moment the pause was lifted.
                var body = target.GetComponentInChildren<Rigidbody>();
                m_HeldKinematic.Add(body != null && body.isKinematic);

                if (body != null)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.isKinematic = true;
                }
            }

            SetRotatorsEnabled(false);
        }

        void Release()
        {
            for (var i = 0; i < m_Targets.Count && i < m_HeldKinematic.Count; i++)
            {
                if (m_Targets[i] == null)
                    continue;

                var body = m_Targets[i].GetComponentInChildren<Rigidbody>();
                if (body != null)
                    body.isKinematic = m_HeldKinematic[i];
            }

            SetRotatorsEnabled(true);
        }

        void SetRotatorsEnabled(bool value)
        {
            for (var i = 0; i < m_Rotators.Count; i++)
            {
                if (m_Rotators[i] != null)
                    m_Rotators[i].enabled = value;
            }
        }
    }
}
