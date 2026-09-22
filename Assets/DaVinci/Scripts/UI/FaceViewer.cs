using UnityEngine;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// Turns a world-space panel to face whoever is looking at it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A panel placed by a setup script has to guess which way the reader will be standing, and a
    /// guess that lands backwards leaves the text mirrored and unreadable — which is exactly what
    /// happens when the guess comes from the model's own forward axis and the player approaches the
    /// cart from the other side. Turning to face the camera removes the guess.
    /// </para>
    /// <para>
    /// Yaw only, by default. A panel that also pitched would tip away from vertical whenever the
    /// reader looked down at the patient, which reads as the panel falling over. Keeping it upright
    /// costs nothing in legibility at these angles.
    /// </para>
    /// <para>
    /// The project's existing <c>Billboard</c> helper does something similar, but it caches
    /// <see cref="Camera.main"/> in <c>Awake</c> and dereferences it unguarded, so it throws in any
    /// scene where the camera arrives late — which is every XR scene, since the rig spawns the
    /// camera. This one re-acquires and tolerates its absence.
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [DefaultExecutionOrder(300)]
    public class FaceViewer : MonoBehaviour
    {
        [SerializeField, Tooltip("Keep the panel upright and turn it only about the vertical axis.")]
        bool m_YawOnly = true;

        [SerializeField, Tooltip("Turn the panel's back to the viewer instead of its face. Only needed if the content reads mirrored.")]
        bool m_Invert;

        [SerializeField, Range(0f, 30f), Tooltip("How quickly the panel swings round, in turns per second. Zero snaps instantly.")]
        float m_Sharpness = 8f;

        Camera m_Camera;

        void LateUpdate()
        {
            var viewer = Viewer();
            if (viewer == null)
                return;

            var direction = transform.position - viewer.position;
            if (m_Invert)
                direction = -direction;

            if (m_YawOnly)
            {
                direction = Vector3.ProjectOnPlane(direction, Vector3.up);

                // Directly above or below the panel there is no yaw that faces the viewer, so the
                // last good heading is kept rather than snapping to an arbitrary one.
                if (direction.sqrMagnitude < 1e-6f)
                    return;
            }
            else if (direction.sqrMagnitude < 1e-6f)
            {
                return;
            }

            var goal = Quaternion.LookRotation(direction.normalized, Vector3.up);

            transform.rotation = m_Sharpness > 0f
                ? Quaternion.Slerp(transform.rotation, goal, 1f - Mathf.Exp(-m_Sharpness * Time.deltaTime))
                : goal;
        }

        Transform Viewer()
        {
            if (m_Camera == null)
                m_Camera = Camera.main;

#if UNITY_EDITOR
            if (m_Camera == null && UnityEditor.SceneView.lastActiveSceneView != null)
                m_Camera = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif

            return m_Camera != null ? m_Camera.transform : null;
        }
    }
}
