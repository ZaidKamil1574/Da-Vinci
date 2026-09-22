using System.Collections.Generic;
using UnityEngine;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// One patient-cart arm of the Da Vinci model, solved as a chain of single-axis revolute joints.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The arm has two operating modes, matching the real machine:
    /// </para>
    /// <para>
    /// <b>Undocked</b> — the whole chain is free. The surgeon drags the instrument to the port and
    /// the entire arm follows.
    /// </para>
    /// <para>
    /// <b>Docked</b> — the cannula is seated in a trocar port and must stay there. The arm then
    /// pivots about that fixed point, its <i>remote centre of motion</i>. This is enforced by task
    /// priority rather than by a penalty: the proximal joints are solved first with the remote
    /// centre as their effector and the port as their goal, pinning the centre in place; the distal
    /// joints are solved afterwards to aim the instrument. Because the second solve only moves
    /// joints beyond the remote centre, it cannot disturb the first.
    /// </para>
    /// </remarks>
    public class DaVinciArm : MonoBehaviour
    {
        [SerializeField, Tooltip("Joints from the arm base outward to the instrument tip, in chain order.")]
        List<RevoluteJoint> m_Joints = new List<RevoluteJoint>();

        [SerializeField, Tooltip("Index of the first joint beyond the remote centre of motion. Joints before it position the arm; joints from it onward aim the instrument once docked.")]
        int m_RemoteCentreJointIndex = 7;

        [SerializeField, Tooltip("The cannula pivot. While docked this transform is held at the trocar port. Usually a child of the last joint before the remote centre index.")]
        Transform m_RemoteCentre;

        [SerializeField, Tooltip("Instrument tip driven toward the surgeon's hand. Usually an empty child at the end of the forceps.")]
        Transform m_ToolTip;

        [Header("Solver")]
        [SerializeField, Range(1, 32), Tooltip("Solver passes per frame. Higher converges further but costs more.")]
        int m_Iterations = 8;

        [SerializeField, Tooltip("Distance at which a solve is considered converged, in metres.")]
        float m_Tolerance = 0.001f;

        readonly List<RevoluteJoint> m_ProximalJoints = new List<RevoluteJoint>();
        readonly List<RevoluteJoint> m_DistalJoints = new List<RevoluteJoint>();

        DaVinciTrocarPort m_DockedPort;

        /// <summary>All joints, base outward.</summary>
        public IReadOnlyList<RevoluteJoint> joints => m_Joints;

        /// <summary>The cannula pivot held at the port while docked.</summary>
        public Transform remoteCentre => m_RemoteCentre;

        /// <summary>The instrument tip driven toward the surgeon's hand.</summary>
        public Transform toolTip => m_ToolTip;

        /// <summary>The port this arm is currently docked to, or <see langword="null"/> when free.</summary>
        public DaVinciTrocarPort dockedPort => m_DockedPort;

        /// <summary>Whether the arm is seated in a trocar port and pivoting about its remote centre.</summary>
        public bool isDocked => m_DockedPort != null;

        void Awake()
        {
            // Only fills in joints that have never been captured. Re-capturing unconditionally
            // would bake whatever pose the scene happened to be saved in as the new mechanical zero.
            CaptureRestPose(false);
            RebuildChains();
        }

        void OnValidate()
        {
            m_RemoteCentreJointIndex = Mathf.Clamp(m_RemoteCentreJointIndex, 0, Mathf.Max(0, m_Joints.Count));
            if (Application.isPlaying)
                RebuildChains();
        }

        /// <summary>
        /// Records the current pose as every joint's zero angle. The imported FBX pose is the
        /// mechanical rest pose, so this runs once at startup before anything moves.
        /// </summary>
        /// <param name="force">
        /// When <see langword="false"/>, joints that already have a rest pose keep it. Pass
        /// <see langword="true"/> only when the arm is deliberately posed at its mechanical zero.
        /// </param>
        public void CaptureRestPose(bool force = true)
        {
            for (var i = 0; i < m_Joints.Count; i++)
            {
                if (force || !m_Joints[i].restCaptured)
                    m_Joints[i].CaptureRest();
            }
        }

        /// <summary>Returns every joint to its captured rest angle.</summary>
        public void ResetToRestPose()
        {
            for (var i = 0; i < m_Joints.Count; i++)
                m_Joints[i].ResetToRest();
        }

        /// <summary>
        /// Splits the chain at the remote centre. Called on <see cref="Awake"/> and whenever the
        /// split index changes.
        /// </summary>
        public void RebuildChains()
        {
            m_ProximalJoints.Clear();
            m_DistalJoints.Clear();

            var split = Mathf.Clamp(m_RemoteCentreJointIndex, 0, m_Joints.Count);
            for (var i = 0; i < m_Joints.Count; i++)
            {
                if (!m_Joints[i].isValid)
                    continue;

                if (i < split)
                    m_ProximalJoints.Add(m_Joints[i]);
                else
                    m_DistalJoints.Add(m_Joints[i]);
            }
        }

        /// <summary>
        /// Drives the instrument tip toward <paramref name="targetPosition"/>.
        /// </summary>
        /// <param name="targetPosition">World-space goal, normally the surgeon's grab point.</param>
        /// <returns>Remaining distance from the tip to the target, in metres.</returns>
        public float SolveTo(Vector3 targetPosition)
        {
            if (m_ToolTip == null)
                return float.PositiveInfinity;

            if (!isDocked)
                return CcdSolver.Solve(m_Joints, m_ToolTip, targetPosition, m_Iterations, m_Tolerance);

            // Priority one: keep the cannula in the port. Without this the arm would tear out of
            // the incision as soon as the surgeon pulled sideways.
            if (m_RemoteCentre != null && m_ProximalJoints.Count > 0)
            {
                CcdSolver.Solve(
                    m_ProximalJoints,
                    m_RemoteCentre,
                    m_DockedPort.remoteCentrePosition,
                    m_Iterations,
                    m_Tolerance);
            }

            // Priority two: aim the instrument, using only joints beyond the remote centre so the
            // pin established above is preserved exactly.
            return CcdSolver.Solve(m_DistalJoints, m_ToolTip, targetPosition, m_Iterations, m_Tolerance);
        }

        /// <summary>
        /// Seats the arm in <paramref name="port"/> and switches to remote-centre motion.
        /// </summary>
        public void Dock(DaVinciTrocarPort port)
        {
            if (port == null || m_DockedPort == port)
                return;

            m_DockedPort = port;
            port.OnArmDocked(this);
        }

        /// <summary>Releases the arm from its port and returns it to free motion.</summary>
        public void Undock()
        {
            if (m_DockedPort == null)
                return;

            var port = m_DockedPort;
            m_DockedPort = null;
            port.OnArmUndocked(this);
        }

        void OnDrawGizmosSelected()
        {
            for (var i = 0; i < m_Joints.Count; i++)
            {
                var joint = m_Joints[i];
                if (!joint.isValid)
                    continue;

                // Proximal joints hold the remote centre, distal joints aim the instrument.
                Gizmos.color = i < m_RemoteCentreJointIndex ? new Color(0.3f, 0.7f, 1f) : new Color(1f, 0.6f, 0.2f);

                var pivot = joint.joint.position;
                var axis = Application.isPlaying
                    ? joint.worldAxis
                    : joint.joint.TransformDirection(joint.localAxis);

                Gizmos.DrawLine(pivot - axis * 0.05f, pivot + axis * 0.05f);
                Gizmos.DrawSphere(pivot, 0.006f);

                if (i + 1 < m_Joints.Count && m_Joints[i + 1].isValid)
                {
                    Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
                    Gizmos.DrawLine(pivot, m_Joints[i + 1].joint.position);
                }
            }

            if (m_RemoteCentre != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(m_RemoteCentre.position, 0.012f);
            }

            if (m_ToolTip != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(m_ToolTip.position, 0.008f);
            }
        }
    }
}
