using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace XRMultiplayer.DaVinci.Editor
{
    /// <summary>
    /// Builds and checks Animation Rigging setups for the Da Vinci arms.
    /// </summary>
    /// <remarks>
    /// Animation Rigging needs four separate pieces wired together before a constraint does
    /// anything: an Animator, a RigBuilder beside it, a Rig registered in that builder's layer
    /// list, and the constraint parented under that Rig. Miss any one and the constraint sits in
    /// the inspector looking correct while never evaluating, with no warning. These commands build
    /// the whole structure at once, and check an existing one for the mistakes that produce a
    /// silent no-op.
    /// </remarks>
    public static class DaVinciChainIkSetup
    {
        /// <summary>
        /// How far up the hierarchy to place the chain root by default. From HAND_4 this lands on
        /// ROTARY MECHANISM_3, giving the solver the three wrist joints to work with.
        /// </summary>
        const int k_DefaultRootDistance = 4;

        /// <summary>ChainIKConstraint rejects a chain of two transforms, so this is the floor.</summary>
        const int k_MinChainLength = 3;

        [MenuItem("GameObject/Da Vinci/Create Chain IK For Selection", false, 11)]
        static void CreateChainIk()
        {
            var tip = Selection.activeTransform;
            if (tip == null)
            {
                Report("Select the tip bone first — the joint you want to reach the target, such as HAND_4.");
                return;
            }

            var animator = tip.GetComponentInParent<Animator>();
            if (animator == null)
            {
                Report($"'{tip.name}' has no Animator above it. Add one to the model root (Da+Vinci) first. " +
                       "Leave its Controller and Avatar empty — Animation Rigging drives the Animator through its own graph.");
                return;
            }

            var root = FindChainRoot(tip, animator.transform);
            if (root == null)
            {
                Report($"'{tip.name}' is too close to the Animator root to form a chain. " +
                       $"ChainIKConstraint needs at least {k_MinChainLength} transforms from root to tip.");
                return;
            }

            var rig = GetOrCreateRig(animator);
            var target = CreateTarget(tip, rig.transform);
            var constraint = CreateConstraint(tip, root, target, rig.transform);

            Selection.activeGameObject = target.gameObject;

            Debug.Log(
                $"[Da Vinci] Chain IK ready.\n" +
                $"  Root   : {root.name}\n" +
                $"  Tip    : {tip.name}\n" +
                $"  Target : {target.name}  (selected — move it and the chain follows)\n" +
                $"  Chain  : {ChainLength(tip, root)} transforms",
                constraint);
        }

        [MenuItem("GameObject/Da Vinci/Validate Rig Setup", false, 12)]
        static void ValidateRigSetup()
        {
            var selection = Selection.activeTransform;
            if (selection == null)
            {
                Report("Select the model root, or any object inside the rig, and run this again.");
                return;
            }

            var animator = selection.GetComponentInParent<Animator>();
            var issues = new List<string>();

            if (animator == null)
            {
                Report($"No Animator found on or above '{selection.name}'. Add one to the model root.");
                return;
            }

            var rigBuilder = animator.GetComponent<RigBuilder>();
            if (rigBuilder == null)
            {
                issues.Add($"'{animator.name}' has an Animator but no Rig Builder. Constraints never evaluate without one.");
            }
            else
            {
                // An entry with an unassigned Rig looks like a populated list in the inspector but
                // resolves to null, so the whole rig goes quiet with nothing to indicate why.
                var emptyLayers = 0;
                foreach (var layer in rigBuilder.layers)
                {
                    if (layer == null || layer.rig == null)
                        emptyLayers++;
                }

                if (emptyLayers > 0)
                {
                    issues.Add(
                        $"'{rigBuilder.name}' has {emptyLayers} Rig Builder layer(s) with an empty Rig slot. " +
                        "The list looks filled in but resolves to nothing, so no constraint under it runs. " +
                        "Drag the Rig object into the slot, or remove the empty row.");
                }

                if (rigBuilder.layers.Count == 0)
                    issues.Add($"'{rigBuilder.name}' has no Rig Builder layers at all.");
            }

            var constraints = animator.GetComponentsInChildren<ChainIKConstraint>(true);
            if (constraints.Length == 0)
                issues.Add("No ChainIKConstraint found anywhere under the Animator.");

            foreach (var constraint in constraints)
                ValidateConstraint(constraint, animator, rigBuilder, issues);

            if (issues.Count == 0)
            {
                Report($"Rig setup on '{animator.name}' looks correct — {constraints.Length} chain constraint(s) wired up.");
                return;
            }

            var message = new StringBuilder();
            for (var i = 0; i < issues.Count; i++)
                message.AppendLine($"{i + 1}. {issues[i]}").AppendLine();

            Debug.LogWarning($"[Da Vinci] Rig setup problems:\n\n{message}", animator);
            Report(message.ToString());
        }

        static void ValidateConstraint(
            ChainIKConstraint constraint,
            Animator animator,
            RigBuilder rigBuilder,
            List<string> issues)
        {
            var label = constraint.name;
            var data = constraint.data;

            if (data.root == null || data.tip == null || data.target == null)
            {
                issues.Add($"'{label}': Root, Tip and Target must all be assigned.");
                return;
            }

            // The constraint must live under a Rig, and that Rig must be registered in the builder.
            var rig = constraint.GetComponentInParent<Rig>();
            if (rig == null)
            {
                issues.Add(
                    $"'{label}' is not underneath a Rig object. A constraint sitting directly on a bone is " +
                    "never collected. Move it to a child of a GameObject that has the Rig component.");
            }
            else if (rigBuilder != null && !LayersContain(rigBuilder, rig))
            {
                issues.Add($"'{label}' is under Rig '{rig.name}', but that Rig is not in {rigBuilder.name}'s Layers list.");
            }

            // Root must be an ancestor of tip, with room for at least one joint between them.
            var length = ChainLength(data.tip, data.root);
            if (length < 0)
                issues.Add($"'{label}': Root '{data.root.name}' is not an ancestor of Tip '{data.tip.name}'.");
            else if (length < k_MinChainLength)
                issues.Add($"'{label}': chain is only {length} transforms. ChainIKConstraint needs at least {k_MinChainLength}.");

            // A target parented inside the chain moves with the arm, so the arm chases itself.
            if (data.target.IsChildOf(data.root))
            {
                issues.Add(
                    $"'{label}': Target '{data.target.name}' is inside the chain it drives ('{data.root.name}'). " +
                    "It will move with the arm and feed back on itself. Parent it outside the bones, under the Rig.");
            }

            if (!data.tip.IsChildOf(animator.transform))
                issues.Add($"'{label}': Tip '{data.tip.name}' is not under the Animator, so it is not in the animation stream.");
        }

        static bool LayersContain(RigBuilder rigBuilder, Rig rig)
        {
            foreach (var layer in rigBuilder.layers)
            {
                if (layer != null && layer.rig == rig)
                    return true;
            }

            return false;
        }

        static Rig GetOrCreateRig(Animator animator)
        {
            var rigBuilder = animator.GetComponent<RigBuilder>();
            if (rigBuilder == null)
                rigBuilder = Undo.AddComponent<RigBuilder>(animator.gameObject);

            Undo.RecordObject(rigBuilder, "Register Rig Layer");

            // A layer whose Rig slot was left empty is the single most common reason a rig silently
            // does nothing: the list looks populated in the inspector but resolves to null. Drop
            // those before looking for a rig to reuse.
            rigBuilder.layers.RemoveAll(layer => layer == null || layer.rig == null);

            // Reuse the first registered rig rather than stacking up "Rig 1", "Rig 2" on every run.
            foreach (var layer in rigBuilder.layers)
            {
                if (layer.rig != null)
                    return layer.rig;
            }

            var existing = animator.GetComponentInChildren<Rig>(true);
            if (existing == null)
            {
                var rigObject = new GameObject("Rig");
                Undo.RegisterCreatedObjectUndo(rigObject, "Create Rig");
                Undo.SetTransformParent(rigObject.transform, animator.transform, "Create Rig");
                rigObject.transform.localPosition = Vector3.zero;
                rigObject.transform.localRotation = Quaternion.identity;
                rigObject.transform.localScale = Vector3.one;
                existing = Undo.AddComponent<Rig>(rigObject);
            }

            rigBuilder.layers.Add(new RigLayer(existing));

            // Layer changes on a prefab instance are not picked up without this.
            if (PrefabUtility.IsPartOfPrefabInstance(rigBuilder))
                EditorUtility.SetDirty(rigBuilder);

            return existing;
        }

        static Transform CreateTarget(Transform tip, Transform rigRoot)
        {
            var target = new GameObject($"{tip.name}_Target").transform;
            Undo.RegisterCreatedObjectUndo(target.gameObject, "Create IK Target");

            // Parented to the Rig, deliberately outside the bone chain.
            Undo.SetTransformParent(target, rigRoot, "Create IK Target");
            target.SetPositionAndRotation(tip.position, tip.rotation);
            target.localScale = Vector3.one;

            return target;
        }

        static ChainIKConstraint CreateConstraint(Transform tip, Transform root, Transform target, Transform rigRoot)
        {
            var host = new GameObject($"{tip.name}_ChainIK");
            Undo.RegisterCreatedObjectUndo(host, "Create Chain IK");
            Undo.SetTransformParent(host.transform, rigRoot, "Create Chain IK");
            host.transform.localPosition = Vector3.zero;
            host.transform.localRotation = Quaternion.identity;
            host.transform.localScale = Vector3.one;

            var constraint = Undo.AddComponent<ChainIKConstraint>(host);

            // data is a struct, so it has to be read out, edited and written back.
            var data = constraint.data;
            data.root = root;
            data.tip = tip;
            data.target = target;
            constraint.data = data;

            return constraint;
        }

        /// <summary>
        /// Walks up from <paramref name="tip"/> to pick a chain root, stopping short of
        /// <paramref name="limit"/> so the root always stays inside the bone hierarchy.
        /// </summary>
        static Transform FindChainRoot(Transform tip, Transform limit)
        {
            var current = tip;
            for (var i = 0; i < k_DefaultRootDistance; i++)
            {
                if (current.parent == null || current.parent == limit)
                    break;

                current = current.parent;
            }

            return ChainLength(tip, current) >= k_MinChainLength ? current : null;
        }

        /// <summary>
        /// Number of transforms from <paramref name="tip"/> up to and including
        /// <paramref name="root"/>, or -1 when root is not an ancestor of tip.
        /// </summary>
        static int ChainLength(Transform tip, Transform root)
        {
            var count = 1;
            var current = tip;
            while (current != null && current != root)
            {
                current = current.parent;
                count++;
            }

            return current == root ? count : -1;
        }

        static void Report(string message)
        {
            EditorUtility.DisplayDialog("Da Vinci Rig Setup", message, "OK");
        }
    }
}
