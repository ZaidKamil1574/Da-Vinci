using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// Puts one object back where the scene started it — used for the balls the Da Vinci arms
    /// reach for, which are also the IK targets, so resetting a ball walks its arm home with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ObjectReset</c> already in the project does something similar, but only for an object
    /// that falls into a trigger volume and only for the client that happens to own it. A button on
    /// a console is pressed by whoever is standing at it, which is usually <i>not</i> the owner of
    /// the ball, so this has to deal with the ownership question head on.
    /// </para>
    /// <para>
    /// <b>Why a non-owner cannot simply move the object.</b> Position is replicated from the owner
    /// by <c>ClientNetworkTransform</c>. A non-owner that writes the transform sees the ball jump
    /// home and then slide straight back as the next update arrives from the owner. So the reset
    /// asks for ownership first and completes once it is granted, giving up after
    /// <see cref="m_OwnershipTimeout"/> rather than waiting forever on a client that never answers.
    /// </para>
    /// <para>
    /// The pose is captured on <see cref="Awake"/>, before anything has had a chance to move the
    /// object, so "initial position" means where the scene was authored — not where the ball
    /// happened to be when someone first pressed the button.
    /// </para>
    /// </remarks>
    public class NetworkedPoseReset : MonoBehaviour
    {
        [SerializeField, Tooltip("The object to put back. Leave empty to reset the object this sits on.")]
        Transform m_Target;

        [SerializeField, Tooltip("Where to put it. Leave empty to use the pose the scene starts it in.")]
        Transform m_RestPose;

        [SerializeField, Tooltip("Refuse to reset while someone is holding the object, rather than tearing it out of their hand.")]
        bool m_IgnoreWhileHeld = true;

        [SerializeField, Tooltip("How long to wait for ownership before giving up on a reset, in seconds.")]
        float m_OwnershipTimeout = 1.5f;

        Pose m_CapturedPose;
        Vector3 m_CapturedScale = Vector3.one;
        NetworkPhysicsInteractable m_Interactable;
        NetworkTransform m_NetworkTransform;
        NetworkObject m_NetworkObject;
        Rigidbody m_Rigidbody;
        Coroutine m_PendingReset;

        /// <summary>The object this component puts back.</summary>
        public Transform target => m_Target;

        /// <summary>Where the object is returned to.</summary>
        public Pose restPose => m_RestPose != null
            ? new Pose(m_RestPose.position, m_RestPose.rotation)
            : m_CapturedPose;

        /// <summary>Whether a hand currently owns the object, which blocks the reset.</summary>
        public bool isHeld => m_Interactable != null && m_Interactable.isInteracting;

        void Awake()
        {
            if (m_Target == null)
                m_Target = transform;

            m_CapturedPose = new Pose(m_Target.position, m_Target.rotation);
            m_CapturedScale = m_Target.localScale;

            m_Target.TryGetComponent(out m_Interactable);
            m_Target.TryGetComponent(out m_NetworkTransform);
            m_Target.TryGetComponent(out m_NetworkObject);
            m_Rigidbody = m_Target.GetComponentInChildren<Rigidbody>();
        }

        /// <summary>
        /// Puts the object back. Safe to wire straight to a UI button.
        /// </summary>
        /// <remarks>
        /// Returns immediately whether or not the reset succeeds — over the network the move can
        /// only happen once ownership arrives, and a button must not block the frame waiting for it.
        /// </remarks>
        public void ResetNow()
        {
            if (m_Target == null)
                return;

            if (m_IgnoreWhileHeld && isHeld)
            {
                Debug.Log($"[Da Vinci] '{m_Target.name}' is being held, so it was left alone.", this);
                return;
            }

            // Offline, or spawned and already ours: nothing to wait for.
            if (!IsSpawned() || m_NetworkObject.IsOwner)
            {
                Place();
                return;
            }

            if (m_PendingReset != null)
                StopCoroutine(m_PendingReset);

            m_PendingReset = StartCoroutine(ResetWhenOwned());
        }

        /// <summary>
        /// Asks for ownership, then places the object once the request is granted.
        /// </summary>
        IEnumerator ResetWhenOwned()
        {
            // RequestOwnership also silences the ClientNetworkTransform for the duration, which is
            // what stops the old owner's updates from fighting the move that follows.
            if (m_Interactable != null)
                m_Interactable.RequestOwnership();
            else
                m_NetworkObject.ChangeOwnership(NetworkManager.Singleton.LocalClientId);

            var deadline = Time.time + m_OwnershipTimeout;
            while (Time.time < deadline)
            {
                if (!IsSpawned())
                    yield break;

                if (m_NetworkObject.IsOwner)
                {
                    Place();
                    m_PendingReset = null;
                    yield break;
                }

                yield return null;
            }

            Debug.LogWarning(
                $"[Da Vinci] Timed out waiting for ownership of '{m_Target.name}', so it was not " +
                "reset. Another player is most likely still holding it.", this);

            m_PendingReset = null;
        }

        void Place()
        {
            var pose = restPose;

            // Teleport rather than a plain transform write: it tells the network transform this is
            // a discontinuity, so remote clients snap the ball home instead of interpolating it
            // across the room at grab speed.
            if (IsSpawned() && m_NetworkTransform != null && m_NetworkTransform.enabled)
                m_NetworkTransform.Teleport(pose.position, pose.rotation, m_CapturedScale);
            else
                m_Target.SetPositionAndRotation(pose.position, pose.rotation);

            m_Target.localScale = m_CapturedScale;

            if (m_Interactable != null)
            {
                // Clears the interactable's velocity history as well as the rigidbody's, so the
                // ball does not inherit the throw it was on when the button was pressed.
                m_Interactable.ResetObjectPhysics();
            }
            else if (m_Rigidbody != null && !m_Rigidbody.isKinematic)
            {
                m_Rigidbody.linearVelocity = Vector3.zero;
                m_Rigidbody.angularVelocity = Vector3.zero;
            }
        }

        bool IsSpawned() => m_NetworkObject != null && m_NetworkObject.IsSpawned;

        void OnDrawGizmosSelected()
        {
            var pose = Application.isPlaying
                ? restPose
                : new Pose(
                    m_RestPose != null ? m_RestPose.position : (m_Target != null ? m_Target : transform).position,
                    Quaternion.identity);

            Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.9f);
            Gizmos.DrawWireSphere(pose.position, 0.04f);
        }
    }
}
