using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace XRMultiplayer.DaVinci.Editor
{
    /// <summary>
    /// Builds <see cref="DaVinciArm"/> components from the imported Da Vinci hierarchy.
    /// </summary>
    /// <remarks>
    /// The FBX carries no bones and no joint metadata, only nested meshes, so the chain has to be
    /// recovered from the hierarchy itself. Each arm is a single long spine with small decorative
    /// branches (fasteners, knurled knobs, indicator lights) hanging off it, so the deepest path
    /// from the arm root is the kinematic chain. That is what this walks.
    /// </remarks>
    public static class DaVinciArmBuilder
    {
        /// <summary>Name fragment identifying the node that carries the remote centre of motion.</summary>
        const string k_RemoteCentreMarker = "ROTARY MECHANISM_3";

        /// <summary>Name prefix of each arm's root node in the imported model.</summary>
        const string k_ArmRootPrefix = "Cylinder_2";

        [MenuItem("GameObject/Da Vinci/Build Arms From Selection", false, 10)]
        static void BuildFromSelection()
        {
            var root = Selection.activeTransform;
            if (root == null)
            {
                EditorUtility.DisplayDialog(
                    "Da Vinci Arm Builder",
                    "Select the root of the imported Da Vinci model first.",
                    "OK");
                return;
            }

            var armRoots = FindArmRoots(root);
            if (armRoots.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Da Vinci Arm Builder",
                    $"No arm roots found under '{root.name}'. Expected child nodes named '{k_ArmRootPrefix}*'.",
                    "OK");
                return;
            }

            foreach (var armRoot in armRoots)
                BuildArm(armRoot);

            EditorUtility.DisplayDialog(
                "Da Vinci Arm Builder",
                $"Built {armRoots.Count} arm(s).\n\nEvery joint axis defaults to local +Y and still has to be " +
                "calibrated. Select an arm and use the joint sliders in the inspector to find each real axis.",
                "OK");
        }

        /// <summary>Collects the arm root nodes beneath <paramref name="root"/>.</summary>
        public static List<Transform> FindArmRoots(Transform root)
        {
            var results = new List<Transform>();
            foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name.StartsWith(k_ArmRootPrefix, System.StringComparison.Ordinal))
                    results.Add(candidate);
            }

            return results;
        }

        /// <summary>
        /// Adds and configures a <see cref="DaVinciArm"/> on <paramref name="armRoot"/>.
        /// </summary>
        public static DaVinciArm BuildArm(Transform armRoot)
        {
            var chain = FindDeepestChain(armRoot);
            if (chain.Count < 2)
            {
                Debug.LogWarning($"[Da Vinci] '{armRoot.name}' has no chain deep enough to rig.", armRoot);
                return null;
            }

            var arm = armRoot.GetComponent<DaVinciArm>();
            if (arm == null)
            {
                arm = Undo.AddComponent<DaVinciArm>(armRoot.gameObject);
            }

            // The tip is the last link, and is driven rather than driving, so it is not a joint.
            var jointCount = chain.Count - 1;
            var remoteCentreIndex = FindRemoteCentreIndex(chain, jointCount);

            var serialized = new SerializedObject(arm);
            var jointsProperty = serialized.FindProperty("m_Joints");
            jointsProperty.arraySize = jointCount;

            for (var i = 0; i < jointCount; i++)
            {
                var element = jointsProperty.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("m_Joint").objectReferenceValue = chain[i];

                // Left at +Y on purpose. A wrong-but-obvious default is easier to spot and fix than
                // a guess that happens to look plausible on some joints and not others.
                element.FindPropertyRelative("m_LocalAxis").vector3Value = Vector3.up;
                element.FindPropertyRelative("m_MinAngle").floatValue = -90f;
                element.FindPropertyRelative("m_MaxAngle").floatValue = 90f;

                // The imported FBX pose is the mechanical zero, so record it now while the model is
                // guaranteed to be untouched.
                element.FindPropertyRelative("m_RestLocalRotation").quaternionValue = chain[i].localRotation;
                element.FindPropertyRelative("m_RestCaptured").boolValue = true;

                // Distal joints are light and quick on the real machine; proximal ones carry the
                // mass of the whole arm. Weighting the solve this way keeps the base comparatively
                // still instead of swinging the cart around to satisfy a small hand movement.
                element.FindPropertyRelative("m_Weight").floatValue = i < remoteCentreIndex ? 0.35f : 1f;
            }

            serialized.FindProperty("m_RemoteCentreJointIndex").intValue = remoteCentreIndex;
            serialized.FindProperty("m_RemoteCentre").objectReferenceValue = chain[Mathf.Min(remoteCentreIndex, chain.Count - 1)];
            serialized.FindProperty("m_ToolTip").objectReferenceValue = chain[chain.Count - 1];
            serialized.ApplyModifiedProperties();

            // Undo.AddComponent already registered the creation, and ApplyModifiedProperties
            // registers the field changes. Registering the component as "created" a second time
            // would make Undo delete an arm that was already there when this was re-run.
            return arm;
        }

        /// <summary>
        /// Walks from <paramref name="root"/> down the longest available path. Branches shorter than
        /// the spine are structural detail and are skipped.
        /// </summary>
        public static List<Transform> FindDeepestChain(Transform root)
        {
            var chain = new List<Transform> { root };
            var current = root;

            while (current.childCount > 0)
            {
                Transform deepestChild = null;
                var deepestDepth = -1;

                for (var i = 0; i < current.childCount; i++)
                {
                    var child = current.GetChild(i);
                    var depth = Depth(child);
                    if (depth > deepestDepth)
                    {
                        deepestDepth = depth;
                        deepestChild = child;
                    }
                }

                if (deepestChild == null)
                    break;

                chain.Add(deepestChild);
                current = deepestChild;
            }

            return chain;
        }

        static int Depth(Transform node)
        {
            var deepest = 0;
            for (var i = 0; i < node.childCount; i++)
                deepest = Mathf.Max(deepest, Depth(node.GetChild(i)) + 1);

            return deepest;
        }

        static int FindRemoteCentreIndex(List<Transform> chain, int jointCount)
        {
            for (var i = 0; i < jointCount; i++)
            {
                if (chain[i].name.StartsWith(k_RemoteCentreMarker, System.StringComparison.Ordinal))
                    return Mathf.Min(i + 1, jointCount);
            }

            // No marker found. Split halfway so the arm is still usable, and say so rather than
            // letting a silently wrong split turn up later as strange docking behaviour.
            Debug.LogWarning(
                $"[Da Vinci] No '{k_RemoteCentreMarker}' node in this chain. Falling back to a midpoint split; " +
                "set the remote centre joint index by hand.");
            return jointCount / 2;
        }
    }
}
