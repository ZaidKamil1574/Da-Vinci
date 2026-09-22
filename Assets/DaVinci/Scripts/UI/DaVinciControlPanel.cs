using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// The console panel for the Da Vinci robot: buttons that put the balls back where they
    /// started, and live readouts of what each watched joint is doing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The panel owns no mechanism. Resets are delegated to a <see cref="NetworkedPoseReset"/> on
    /// each ball and readings are pulled from <see cref="JointMotionTracker"/> components on the
    /// joints, so the same numbers appear here and on the in-world angle overlays without either
    /// being the other's source of truth.
    /// </para>
    /// <para>
    /// Rows name a joint <see cref="Transform"/>, not a tracker. The trackers on the IK chain are
    /// created at runtime by <see cref="DaVinciIkChainVisualizer"/> and so cannot be dragged into a
    /// serialized field; naming the joint instead lets the panel find or add the tracker itself,
    /// and means the panel still works in a scene with no overlays at all.
    /// </para>
    /// <para>
    /// Readouts refresh on an interval rather than every frame. Every <c>SetText</c> re-meshes the
    /// label, and at 90 fps that is ninety re-meshes a second to animate a digit no one can read.
    /// Ten a second looks continuous and costs a ninth as much.
    /// </para>
    /// </remarks>
    public class DaVinciControlPanel : MonoBehaviour
    {
        /// <summary>
        /// One line of the panel showing how far a joint has turned.
        /// </summary>
        [Serializable]
        public class AngleRow
        {
            [Tooltip("Name shown at the start of the line. Defaults to the joint's own name.")]
            public string label;

            [Tooltip("The joint to read. A motion tracker is added to it if it has none.")]
            public Transform joint;

            [Tooltip("Label this row writes into.")]
            public TMP_Text text;

            [Tooltip("Also show how far the solver is twisting this joint off its own hinge.")]
            public bool showOffAxis = true;

            internal JointMotionTracker tracker;
            internal string format;
        }

        /// <summary>
        /// One line of the panel showing how fast a joint is moving.
        /// </summary>
        [Serializable]
        public class SpeedRow
        {
            [Tooltip("Name shown at the start of the line. Defaults to the joint's own name.")]
            public string label;

            [Tooltip("The joint to read. A motion tracker is added to it if it has none.")]
            public Transform joint;

            [Tooltip("Label this row writes into.")]
            public TMP_Text text;

            internal JointMotionTracker tracker;
            internal string format;
        }

        [Header("Balls")]
        [SerializeField, Tooltip("The first ball. Drag the ball object straight in — it is given a reset component automatically if it has none.")]
        Transform m_Ball1;

        [SerializeField, Tooltip("The second ball. Drag the ball object straight in — it is given a reset component automatically if it has none.")]
        Transform m_Ball2;

        [Header("Readouts")]
        [SerializeField, Tooltip("Joint angles, one line each.")]
        List<AngleRow> m_Angles = new List<AngleRow>();

        [SerializeField, Tooltip("Arm speeds, one line each.")]
        List<SpeedRow> m_Speeds = new List<SpeedRow>();

        [SerializeField, Tooltip("Line showing how far each chain's tip still is from its target.")]
        TMP_Text m_ReachText;

        [SerializeField, Tooltip("Chains whose reach is reported on the reach line.")]
        List<DaVinciIkChainVisualizer> m_Chains = new List<DaVinciIkChainVisualizer>();

        [SerializeField, Tooltip("Line showing the lever-driven joint's angle.")]
        TMP_Text m_LeverText;

        [SerializeField, Tooltip("The lever-driven joint reported on the lever line.")]
        LeverDrivenRotator m_LeverRotator;

        [Header("Overlay")]
        [SerializeField, Tooltip("Angle overlays switched on and off together by SetAngleOverlayVisible().")]
        List<JointAngleVisual> m_Overlays = new List<JointAngleVisual>();

        [SerializeField, Tooltip("Chain overlays switched on and off with them.")]
        List<DaVinciIkChainVisualizer> m_ChainOverlays = new List<DaVinciIkChainVisualizer>();

        [Header("Refresh")]
        [SerializeField, Range(0.02f, 1f), Tooltip("Seconds between readout updates. Ten a second reads as continuous and costs a fraction of a per-frame refresh.")]
        float m_RefreshInterval = 0.1f;

        float m_NextRefresh;
        string m_LeverFormat;
        NetworkedPoseReset m_Ball1Reset;
        NetworkedPoseReset m_Ball2Reset;

        void Start()
        {
            m_Ball1Reset = FindOrAddReset(m_Ball1);
            m_Ball2Reset = FindOrAddReset(m_Ball2);

            for (var i = 0; i < m_Angles.Count; i++)
                PrepareAngleRow(m_Angles[i]);

            for (var i = 0; i < m_Speeds.Count; i++)
                PrepareSpeedRow(m_Speeds[i]);

            var leverJoint = m_LeverRotator != null && m_LeverRotator.joint != null
                ? m_LeverRotator.joint.name
                : "Lever joint";

            m_LeverFormat = leverJoint + "   <b>{0:0.0}</b>°  → {1:0.0}°   {2:0}°/s";
        }

        void Update()
        {
            if (Time.time < m_NextRefresh)
                return;

            m_NextRefresh = Time.time + m_RefreshInterval;
            Refresh();
        }

        /// <summary>Puts the first ball back where the scene started it.</summary>
        public void ResetBall1() => TryReset(m_Ball1Reset, m_Ball1, nameof(m_Ball1));

        /// <summary>Puts the second ball back where the scene started it.</summary>
        public void ResetBall2() => TryReset(m_Ball2Reset, m_Ball2, nameof(m_Ball2));

        /// <summary>Puts both balls back where the scene started them.</summary>
        public void ResetBothBalls()
        {
            ResetBall1();
            ResetBall2();
        }

        /// <summary>Shows or hides every angle overlay this panel knows about.</summary>
        public void SetAngleOverlayVisible(bool value)
        {
            for (var i = 0; i < m_Overlays.Count; i++)
            {
                if (m_Overlays[i] != null)
                    m_Overlays[i].visible = value;
            }

            for (var i = 0; i < m_ChainOverlays.Count; i++)
            {
                if (m_ChainOverlays[i] != null)
                    m_ChainOverlays[i].visible = value;
            }
        }

        /// <summary>Re-zeroes every watched joint against the pose it is in right now.</summary>
        /// <remarks>
        /// Useful after parking the arms by hand: without it every angle stays measured from the
        /// imported pose, which is not where the surgeon just decided zero should be.
        /// </remarks>
        public void ZeroAngles()
        {
            for (var i = 0; i < m_Angles.Count; i++)
            {
                if (m_Angles[i].tracker != null)
                    m_Angles[i].tracker.CaptureRest();
            }

            for (var i = 0; i < m_Speeds.Count; i++)
            {
                if (m_Speeds[i].tracker != null)
                    m_Speeds[i].tracker.CaptureRest();
            }
        }

        /// <summary>
        /// Fires one ball's reset, saying so in the log when the panel has none wired.
        /// </summary>
        /// <remarks>
        /// Not named <c>Reset</c>: Unity calls a parameterless <c>Reset</c> on a MonoBehaviour when
        /// the user picks Reset in the inspector, and giving that name a second meaning here would
        /// mislead anyone reading the class.
        /// </remarks>
        static void TryReset(NetworkedPoseReset reset, Transform ball, string field)
        {
            if (reset == null)
            {
                Debug.LogWarning(
                    ball == null
                        ? $"[Da Vinci] Panel has no ball assigned for {field}."
                        : $"[Da Vinci] '{ball.name}' has no reset component, so {field} did nothing.");
                return;
            }

            reset.ResetNow();
        }

        /// <summary>
        /// Gets the reset component on <paramref name="ball"/>, adding one if it has none.
        /// </summary>
        /// <remarks>
        /// The field is a plain <see cref="Transform"/> so any ball in the scene can be dragged
        /// onto it. Typing it as <see cref="NetworkedPoseReset"/> would have made the slot reject
        /// every object that had not already been given that component by hand, which is exactly
        /// the object you want to drop on it.
        /// </remarks>
        static NetworkedPoseReset FindOrAddReset(Transform ball)
        {
            if (ball == null)
                return null;

            return ball.TryGetComponent<NetworkedPoseReset>(out var reset)
                ? reset
                : ball.gameObject.AddComponent<NetworkedPoseReset>();
        }

        void PrepareAngleRow(AngleRow row)
        {
            row.tracker = FindOrAddTracker(row.joint);
            var label = string.IsNullOrEmpty(row.label) ? NameOf(row.joint) : row.label;

            // TMP's SetText takes floats only, so the row's name is baked into the format string
            // and the update stays allocation free. The spec is TMP's own: digits after the '.'
            // are decimal places.
            row.format = row.showOffAxis
                ? label + "   <b>{0:0.0}</b>°   <alpha=#99>off-axis {1:0.0}°"
                : label + "   <b>{0:0.0}</b>°";
        }

        void PrepareSpeedRow(SpeedRow row)
        {
            row.tracker = FindOrAddTracker(row.joint);
            var label = string.IsNullOrEmpty(row.label) ? NameOf(row.joint) : row.label;

            row.format = label + "   <b>{0:0}</b>°/s   <b>{1:0.00}</b> m/s";
        }

        /// <summary>
        /// Gets the tracker on <paramref name="joint"/>, adding one if the joint has none.
        /// </summary>
        /// <remarks>
        /// Adding rather than failing means a joint can be put on the panel without also giving it
        /// an in-world overlay — the readout and the wedge are independently useful.
        /// </remarks>
        static JointMotionTracker FindOrAddTracker(Transform joint)
        {
            if (joint == null)
                return null;

            return joint.TryGetComponent<JointMotionTracker>(out var tracker)
                ? tracker
                : joint.gameObject.AddComponent<JointMotionTracker>();
        }

        void Refresh()
        {
            for (var i = 0; i < m_Angles.Count; i++)
            {
                var row = m_Angles[i];
                if (row.text == null || row.tracker == null)
                    continue;

                if (row.showOffAxis)
                    row.text.SetText(row.format, row.tracker.angle, row.tracker.offAxisAngle);
                else
                    row.text.SetText(row.format, row.tracker.angle);
            }

            for (var i = 0; i < m_Speeds.Count; i++)
            {
                var row = m_Speeds[i];
                if (row.text == null || row.tracker == null)
                    continue;

                row.text.SetText(row.format, row.tracker.angularSpeed, row.tracker.linearSpeed);
            }

            RefreshReach();
            RefreshLever();
        }

        void RefreshReach()
        {
            if (m_ReachText == null || m_Chains.Count == 0)
                return;

            // Distances are millimetres here: a surgical arm that is 4mm from its goal is a long
            // way off, and in metres that reads as 0.00.
            var nearest = float.PositiveInfinity;
            var counted = 0;
            for (var i = 0; i < m_Chains.Count; i++)
            {
                if (m_Chains[i] == null)
                    continue;

                var distance = m_Chains[i].targetDistance;
                if (float.IsInfinity(distance))
                    continue;

                nearest = Mathf.Min(nearest, distance);
                counted++;
            }

            if (counted == 0)
            {
                m_ReachText.SetText("IK reach   no target");
                return;
            }

            m_ReachText.SetText("IK reach   <b>{0:0.0}</b> mm to target", nearest * 1000f);
        }

        void RefreshLever()
        {
            if (m_LeverText == null || m_LeverRotator == null)
                return;

            m_LeverText.SetText(
                m_LeverFormat,
                m_LeverRotator.angle,
                m_LeverRotator.goalAngle,
                m_LeverRotator.angularSpeed);
        }

        static string NameOf(Transform joint) => joint != null ? joint.name : "(unassigned)";
    }
}
