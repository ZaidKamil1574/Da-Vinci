using UnityEngine;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// Plays named poses on a plain humanoid character through its Animator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately the whole of the runtime side. On a model with no Animation Rigging there is
    /// exactly one thing writing the bones — the Animator — so a pose change is a crossfade and
    /// nothing else. Everything that went wrong on the rigged patient came from three systems
    /// writing the same bones on different clocks: the clip, the constraints, and per-frame code in
    /// <c>LateUpdate</c>. None of that applies here.
    /// </para>
    /// <para>
    /// Limbs move convincingly because a crossfade interpolates each bone's local rotation between
    /// two poses that are both anatomically valid. Nothing can hyperextend, because the shortest
    /// path between two believable elbow angles is a believable elbow angle. That is how ordinary
    /// character animation works, and it needs no IK.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Animator))]
    public class HumanoidPoseLibrary : MonoBehaviour
    {
        [SerializeField, Tooltip("Animator holding the pose states. Defaults to the one on this object.")]
        Animator m_Animator;

        [SerializeField, Tooltip("State played on start.")]
        string m_StartPose = "Sleeping";

        [SerializeField, Tooltip("Seconds taken to move between poses.")]
        float m_BlendDuration = 1.5f;

        /// <summary>The pose currently playing or being blended to.</summary>
        public string CurrentPose { get; private set; }

        void Awake()
        {
            if (m_Animator == null)
                m_Animator = GetComponent<Animator>();

            if (!string.IsNullOrEmpty(m_StartPose))
                Snap(m_StartPose);
        }

        /// <summary>Blends to <paramref name="poseName"/> over the configured duration.</summary>
        public void Play(string poseName)
        {
            if (!Ready(poseName))
                return;

            CurrentPose = poseName;
            m_Animator.CrossFadeInFixedTime(poseName, Mathf.Max(0f, m_BlendDuration));
        }

        /// <summary>Jumps straight to <paramref name="poseName"/> with no blend.</summary>
        public void Snap(string poseName)
        {
            if (!Ready(poseName))
                return;

            CurrentPose = poseName;
            m_Animator.Play(poseName, 0, 0f);
        }

        /// <summary>Lays the patient down asleep. Wired to a UI button.</summary>
        public void PlaySleeping() => Play("Sleeping");

        /// <summary>Sits the patient up awake. Wired to a UI button.</summary>
        public void PlayWaking() => Play("Waking");

        /// <summary>Switches between the two standard poses.</summary>
        public void Toggle() => Play(CurrentPose == "Waking" ? "Sleeping" : "Waking");

        bool Ready(string poseName)
        {
            if (m_Animator == null)
                m_Animator = GetComponent<Animator>();

            if (m_Animator == null || m_Animator.runtimeAnimatorController == null)
            {
                Debug.LogWarning($"[Patient] {name} has no Animator controller, so \"{poseName}\" cannot play.", this);
                return false;
            }

            return !string.IsNullOrEmpty(poseName);
        }
    }
}
