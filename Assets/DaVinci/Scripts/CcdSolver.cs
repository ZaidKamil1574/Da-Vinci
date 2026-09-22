using System.Collections.Generic;
using UnityEngine;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// Cyclic Coordinate Descent solver constrained to one rotation axis per joint.
    /// </summary>
    /// <remarks>
    /// This exists because the stock <c>ChainIKConstraint</c> in com.unity.animation.rigging treats
    /// every joint as a ball joint. A surgical arm is a chain of single-axis revolute joints, so an
    /// unconstrained solver produces poses the real mechanism cannot reach. Here each correction is
    /// projected onto the plane perpendicular to the joint's own axis before it is applied, which
    /// makes an out-of-axis pose unrepresentable rather than merely penalised.
    /// </remarks>
    public static class CcdSolver
    {
        /// <summary>Below this projected length the swing angle is numerically meaningless.</summary>
        const float k_MinProjectedLength = 1e-5f;

        /// <summary>
        /// Rotates <paramref name="joints"/> so that <paramref name="effector"/> moves toward
        /// <paramref name="targetPosition"/>, respecting each joint's axis and angle limits.
        /// </summary>
        /// <param name="joints">Chain ordered from the base outward. Solved from the tip inward each iteration.</param>
        /// <param name="effector">The transform being driven to the target. Usually the last joint's tip.</param>
        /// <param name="targetPosition">World-space goal for the effector.</param>
        /// <param name="iterations">Maximum full passes over the chain.</param>
        /// <param name="tolerance">Distance at which the solve is considered converged, in metres.</param>
        /// <returns>Final distance from the effector to the target, in metres.</returns>
        public static float Solve(
            IReadOnlyList<RevoluteJoint> joints,
            Transform effector,
            Vector3 targetPosition,
            int iterations,
            float tolerance)
        {
            if (joints == null || joints.Count == 0 || effector == null)
                return float.PositiveInfinity;

            var distance = Vector3.Distance(effector.position, targetPosition);

            for (var iteration = 0; iteration < iterations && distance > tolerance; iteration++)
            {
                // Tip inward. The distal joints are light and fast in the real mechanism, so letting
                // them absorb the correction first keeps the heavy setup joints comparatively still.
                for (var i = joints.Count - 1; i >= 0; i--)
                {
                    var joint = joints[i];
                    if (!joint.isValid || joint.weight <= 0f)
                        continue;

                    var pivot = joint.joint.position;
                    var axis = joint.worldAxis;

                    // Only the component of each vector in the joint's plane of rotation is reachable.
                    var toEffector = Vector3.ProjectOnPlane(effector.position - pivot, axis);
                    var toTarget = Vector3.ProjectOnPlane(targetPosition - pivot, axis);

                    if (toEffector.sqrMagnitude < k_MinProjectedLength || toTarget.sqrMagnitude < k_MinProjectedLength)
                        continue;

                    var swing = Vector3.SignedAngle(toEffector, toTarget, axis);
                    if (Mathf.Abs(swing) < Mathf.Epsilon)
                        continue;

                    joint.Rotate(swing * joint.weight);
                }

                var previous = distance;
                distance = Vector3.Distance(effector.position, targetPosition);

                // Joint limits can leave the chain unable to improve. Stop rather than spin.
                if (previous - distance < tolerance * 0.01f)
                    break;
            }

            return distance;
        }
    }
}
