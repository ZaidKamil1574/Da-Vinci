using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace XRMultiplayer.DaVinci.Editor
{
    /// <summary>
    /// Fits a character with a grabbable arm: a two-bone IK constraint plus a target the user can take
    /// hold of, so a limb can be repositioned without tearing the mesh.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bones are matched on the segment after the namespace colon, so <c>mixamorig2:RightArm</c> and
    /// <c>mixamorig8:RightArm</c> both resolve without the command needing to know which export it is
    /// looking at. This is the path that works when the Animator has no avatar — as it does here,
    /// since the avatar was cleared so the pose clips would bind by transform path.
    /// </para>
    /// <para>
    /// The constraint is created at weight zero and the target is placed exactly on the hand, so
    /// running this changes nothing visible until somebody grabs it.
    /// </para>
    /// </remarks>
    public static class GrabbableLimbSetup
    {
        const string k_Title = "Grabbable Limb Setup";
        const string k_RigName = "Limb Rig";

        /// <summary>Radius of the grab handle, in world metres.</summary>
        const float k_HandleRadius = 0.07f;

        /// <summary>How far the hint sits off the elbow, in world metres.</summary>
        const float k_HintDistance = 0.3f;

        [MenuItem("Tools/Da Vinci/Patient Poses/Make Right Hand Grabbable", false, 80)]
        public static void MakeRightHandGrabbable() => Build("Right", "RightShoulder", "RightArm", "RightForeArm", "RightHand");

        [MenuItem("Tools/Da Vinci/Patient Poses/Make Left Hand Grabbable", false, 81)]
        public static void MakeLeftHandGrabbable() => Build("Left", "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand");

        static void Build(string side, string shoulderName, string rootName, string midName, string tipName)
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog(k_Title, "Leave play mode first.", "OK");
                return;
            }

            var selected = Selection.activeGameObject;
            var animator = selected != null ? selected.GetComponentInParent<Animator>() : null;
            if (animator == null)
            {
                EditorUtility.DisplayDialog(k_Title,
                    "Select the character (or any part of it) first. It needs an Animator.", "OK");
                return;
            }

            var character = animator.gameObject;
            var root = FindBone(character.transform, rootName);
            var mid = FindBone(character.transform, midName);
            var tip = FindBone(character.transform, tipName);

            if (root == null || mid == null || tip == null)
            {
                EditorUtility.DisplayDialog(k_Title,
                    $"Could not find the {side.ToLowerInvariant()} arm chain on \"{character.name}\".\n\n" +
                    $"Looked for bones ending in: {rootName}, {midName}, {tipName}.", "OK");
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(character, "Make Hand Grabbable");
            var notes = new List<string>();

            // ---- rig scaffolding -------------------------------------------------------

            var builder = character.GetComponent<RigBuilder>();
            if (builder == null)
            {
                builder = Undo.AddComponent<RigBuilder>(character);
                notes.Add("Added a RigBuilder.");
            }

            var rig = FindChild(character.transform, k_RigName);
            if (rig == null)
            {
                var go = new GameObject(k_RigName);
                Undo.RegisterCreatedObjectUndo(go, "Make Hand Grabbable");
                Undo.SetTransformParent(go.transform, character.transform, "Make Hand Grabbable");
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                rig = go.transform;
                Undo.AddComponent<Rig>(go);
                notes.Add($"Created \"{k_RigName}\".");
            }

            var rigComponent = rig.GetComponent<Rig>();
            if (rigComponent == null)
                rigComponent = Undo.AddComponent<Rig>(rig.gameObject);

            var registered = false;
            foreach (var layer in builder.layers)
            {
                if (layer.rig == rigComponent)
                {
                    registered = true;
                    break;
                }
            }

            if (!registered)
            {
                builder.layers.Add(new RigLayer(rigComponent));
                notes.Add("Registered the rig with the RigBuilder.");
            }

            // ---- constraint ------------------------------------------------------------

            var constraintName = $"{side} Arm IK";
            var constraintTransform = FindChild(rig, constraintName);
            if (constraintTransform == null)
            {
                var go = new GameObject(constraintName);
                Undo.RegisterCreatedObjectUndo(go, "Make Hand Grabbable");
                Undo.SetTransformParent(go.transform, rig, "Make Hand Grabbable");
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                constraintTransform = go.transform;
            }

            var constraint = constraintTransform.GetComponent<TwoBoneIKConstraint>();
            if (constraint == null)
                constraint = Undo.AddComponent<TwoBoneIKConstraint>(constraintTransform.gameObject);

            var target = EnsureChild(constraintTransform, $"{side} Hand Target");
            var hint = EnsureChild(constraintTransform, $"{side} Elbow Hint");

            // The target starts exactly on the hand, so the solve is a no-op and nothing moves until
            // it is grabbed.
            target.SetPositionAndRotation(tip.position, tip.rotation);
            hint.position = ResolveHint(root, mid, tip);

            // data is a struct, so it has to be read, modified and written back.
            var data = constraint.data;
            data.root = root;
            data.mid = mid;
            data.tip = tip;
            data.target = target;
            data.hint = hint;
            data.targetPositionWeight = 1f;
            data.targetRotationWeight = 1f;
            data.hintWeight = 1f;
            constraint.data = data;

            // Off until somebody takes hold. The clip owns the arm the rest of the time.
            constraint.weight = 0f;
            EditorUtility.SetDirty(constraint);
            notes.Add($"Constrained {root.name} -> {mid.name} -> {tip.name}.");

            // ---- grab handle -----------------------------------------------------------

            var collider = target.GetComponent<SphereCollider>();
            if (collider == null)
                collider = Undo.AddComponent<SphereCollider>(target.gameObject);

            // The character may be imported at a fraction of a unit, and a child inherits that.
            var scale = target.lossyScale;
            var uniform = Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z))));
            collider.radius = k_HandleRadius / uniform;
            collider.isTrigger = false;

            var body = target.GetComponent<Rigidbody>();
            if (body == null)
                body = Undo.AddComponent<Rigidbody>(target.gameObject);

            body.isKinematic = true;
            body.useGravity = false;

            var grab = target.GetComponent<XRGrabInteractable>();
            if (grab == null)
                grab = Undo.AddComponent<XRGrabInteractable>(target.gameObject);

            // Instantaneous writes the transform directly, which matches how the handle is driven the
            // rest of the time. Routing it through the physics timestep would have the interactor and
            // the solver moving one transform on two clocks.
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.trackPosition = true;
            grab.trackRotation = true;
            grab.throwOnDetach = false;

            // The target has to stay under the constraint for the solve to keep resolving it.
            grab.retainTransformParent = true;

            var handle = target.GetComponent<GrabbableLimbIK>();
            if (handle == null)
                handle = Undo.AddComponent<GrabbableLimbIK>(target.gameObject);

            handle.Bind(constraint, target, tip);
            EditorUtility.SetDirty(handle);
            notes.Add("Made the target grabbable.");

            EditorUtility.SetDirty(character);
            EditorSceneManager.MarkSceneDirty(character.scene);

            Debug.Log($"[{k_Title}] {side} hand is grabbable on \"{character.name}\".\n  " +
                      string.Join("\n  ", notes) +
                      "\n  Constraint starts at weight 0; it rises only while the target is held.", character);
        }

        /// <summary>
        /// Places the hint so the elbow keeps bending the way the current pose already bends it.
        /// </summary>
        static Vector3 ResolveHint(Transform root, Transform mid, Transform tip)
        {
            var chord = tip.position - root.position;
            var direction = Vector3.zero;

            if (chord.sqrMagnitude > 1e-8f)
            {
                // How far the elbow sits off the straight line from shoulder to hand. That offset is
                // the plane the joint bends in.
                var projected = root.position + Vector3.Project(mid.position - root.position, chord.normalized);
                direction = mid.position - projected;
            }

            // A straight arm has no bend plane to read; behind the elbow is the anatomical answer.
            if (direction.sqrMagnitude < 1e-4f)
                direction = -mid.forward;

            return mid.position + direction.normalized * k_HintDistance;
        }

        /// <summary>
        /// Finds a bone by the segment after the namespace colon, so any Mixamo export resolves.
        /// </summary>
        static Transform FindBone(Transform root, string boneName)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var name = t.name;
                var colon = name.LastIndexOf(':');
                var bare = colon >= 0 ? name.Substring(colon + 1) : name;

                if (string.Equals(bare, boneName, System.StringComparison.OrdinalIgnoreCase))
                    return t;
            }

            return null;
        }

        static Transform FindChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child;
            }

            return null;
        }

        static Transform EnsureChild(Transform parent, string name)
        {
            var existing = FindChild(parent, name);
            if (existing != null)
                return existing;

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Make Hand Grabbable");
            Undo.SetTransformParent(go.transform, parent, "Make Hand Grabbable");
            go.transform.localScale = Vector3.one;
            return go.transform;
        }
    }
}
