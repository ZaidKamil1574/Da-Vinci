using UnityEngine;
using UnityEngine.XR.Content.Interaction;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// Drives a Da Vinci arm's IK target from console controls, and steps aside when someone grabs
    /// the target directly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything here writes the <b>IK target</b>, never the arm. The rotary mechanism and every
    /// joint below it belong to the ChainIKConstraint, which rewrites them each frame from the rig's
    /// animation pass. A lever wired straight to ROTARY MECHANISM_1 would be overwritten before it
    /// was ever drawn. Moving the target moves the whole chain, because the chain's root is the
    /// rotary mechanism — so steering the target is how you steer the rotary mechanism.
    /// </para>
    /// <para>
    /// The levers are rate controls, not position controls: tilt moves the target while it is held
    /// over, centre holds position. Mapping tilt directly to position would cap the reach at
    /// whatever the lever's travel maps to and make the arm spring back to centre on release.
    /// </para>
    /// <para>
    /// The reference frame is captured once, on <see cref="Awake"/>. Re-reading a pivot that sits
    /// inside the solved chain closes a loop — the target moves, the arm chases it, the pivot turns,
    /// which moves the target again — and the arm never settles.
    /// </para>
    /// <para>
    /// No networking code: NetworkXRLever2D, NetworkXRLever and NetworkXRKnob already replicate
    /// their values, and the target pose follows from those values, so each client arrives at the
    /// same pose on its own. The exception is the grab, which moves the target transform directly
    /// and needs a NetworkTransform on the target to replicate.
    /// </para>
    /// </remarks>
    public class DaVinciArmConsole : MonoBehaviour
    {
        [Header("Controls")]
        [SerializeField, Tooltip("Two-axis lever driving the target horizontally. Forward/back and left/right.")]
        XRLever2D m_TravelLever;

        [SerializeField, Tooltip("Two-axis lever driving the target vertically (forward/back axis) and turning it (left/right axis).")]
        XRLever2D m_LiftLever;

        [SerializeField, Tooltip("Optional dial giving absolute turn, layered on top of the lift lever's turn.")]
        XRKnob m_Dial;

        [Header("Driven")]
        [SerializeField, Tooltip("The Chain IK target this console moves. Not the arm itself — the target it reaches for.")]
        Transform m_Target;

        [SerializeField, Tooltip("Grab interactable on the target. While it is held the console stands aside, then picks up from wherever it was left.")]
        XRBaseInteractable m_TargetGrab;

        [SerializeField, Tooltip("Transform whose axes the travel directions are read from. Only read on the first frame. Leave empty for world axes.")]
        Transform m_DirectionFrame;

        [Header("Travel")]
        [SerializeField, Tooltip("Horizontal speed at full lever tilt, in metres per second.")]
        float m_TravelSpeed = 0.25f;

        [SerializeField, Tooltip("Vertical speed at full lever tilt, in metres per second.")]
        float m_LiftSpeed = 0.2f;

        [SerializeField, Tooltip("Turn rate at full lever tilt, in degrees per second.")]
        float m_TurnSpeed = 60f;

        [Header("Workspace")]
        [SerializeField, Tooltip("How far the target may travel horizontally from where it started, in metres.")]
        float m_ReachRadius = 0.5f;

        [SerializeField, Tooltip("Lowest the target may go relative to its start, in metres.")]
        float m_MinHeight = -0.2f;

        [SerializeField, Tooltip("Highest the target may go relative to its start, in metres.")]
        float m_MaxHeight = 0.4f;

        [SerializeField, Tooltip("Turn limit either side of centre, in degrees.")]
        float m_MaxYaw = 120f;

        Vector3 m_BasePosition;
        Quaternion m_BaseRotation;
        Vector3 m_Right = Vector3.right;
        Vector3 m_Forward = Vector3.forward;
        Vector3 m_Offset;
        float m_Yaw;
        bool m_Ready;

        /// <summary>Whether a hand currently owns the target, leaving the console idle.</summary>
        public bool isGrabbed => m_TargetGrab != null && m_TargetGrab.isSelected;

        void Awake()
        {
            if (m_Target == null)
            {
                Debug.LogError($"[Da Vinci] {name} has no IK target assigned. Disabling.", this);
                enabled = false;
                return;
            }

            m_BasePosition = m_Target.position;
            m_BaseRotation = m_Target.rotation;

            // Captured once, and flattened so lever travel stays horizontal even if the frame is tilted.
            if (m_DirectionFrame != null)
            {
                m_Right = Flatten(m_DirectionFrame.right, Vector3.right);
                m_Forward = Flatten(m_DirectionFrame.forward, Vector3.forward);
            }

            m_Ready = true;
        }

        void OnValidate()
        {
            m_ReachRadius = Mathf.Max(0f, m_ReachRadius);
            m_MaxHeight = Mathf.Max(m_MinHeight, m_MaxHeight);
            m_MaxYaw = Mathf.Max(0f, m_MaxYaw);
        }

        void Update()
        {
            if (!m_Ready)
                return;

            // A hand beats the console. Track what it does so the console resumes from there
            // instead of snapping the target back to where the levers last left it.
            if (isGrabbed)
            {
                m_Offset = m_Target.position - m_BasePosition;
                m_BaseRotation = m_Target.rotation * Quaternion.Inverse(Swing());
                return;
            }

            var travel = m_TravelLever != null ? m_TravelLever.value : Vector2.zero;
            var lift = m_LiftLever != null ? m_LiftLever.value : Vector2.zero;

            var motion = (m_Right * travel.x + m_Forward * travel.y) * m_TravelSpeed
                         + Vector3.up * (lift.y * m_LiftSpeed);

            m_Offset = ClampToWorkspace(m_Offset + motion * Time.deltaTime);

            var turn = lift.x * m_TurnSpeed * Time.deltaTime;
            m_Yaw = Mathf.Clamp(m_Yaw + turn, -m_MaxYaw, m_MaxYaw);

            m_Target.position = m_BasePosition + m_Offset;
            m_Target.rotation = Swing() * m_BaseRotation;
        }

        /// <summary>Total turn: the lift lever's accumulated rate plus the dial's absolute angle.</summary>
        Quaternion Swing()
        {
            var dialYaw = m_Dial != null ? Mathf.Lerp(-m_MaxYaw, m_MaxYaw, Mathf.Clamp01(m_Dial.value)) : 0f;
            return Quaternion.AngleAxis(m_Yaw + dialYaw, Vector3.up);
        }

        /// <summary>
        /// Holds the target inside a cylinder around its start, so the levers cannot walk the arm
        /// out to somewhere the solver has no hope of reaching.
        /// </summary>
        Vector3 ClampToWorkspace(Vector3 offset)
        {
            var horizontal = new Vector3(offset.x, 0f, offset.z);
            if (horizontal.magnitude > m_ReachRadius)
                horizontal = horizontal.normalized * m_ReachRadius;

            horizontal.y = Mathf.Clamp(offset.y, m_MinHeight, m_MaxHeight);
            return horizontal;
        }

        static Vector3 Flatten(Vector3 direction, Vector3 fallback)
        {
            var flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            return flat.sqrMagnitude > 0.0001f ? flat.normalized : fallback;
        }

        void OnDrawGizmosSelected()
        {
            if (m_Target == null)
                return;

            var origin = Application.isPlaying ? m_BasePosition : m_Target.position;

            // The cylinder the target is allowed to move within.
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
            DrawCircle(origin + Vector3.up * m_MinHeight, m_ReachRadius);
            DrawCircle(origin + Vector3.up * m_MaxHeight, m_ReachRadius);
            Gizmos.DrawLine(origin + Vector3.up * m_MinHeight, origin + Vector3.up * m_MaxHeight);
        }

        static void DrawCircle(Vector3 centre, float radius)
        {
            const int steps = 32;
            var previous = centre + new Vector3(radius, 0f, 0f);
            for (var i = 1; i <= steps; i++)
            {
                var angle = i / (float)steps * Mathf.PI * 2f;
                var point = centre + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(previous, point);
                previous = point;
            }
        }
    }
}
