using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace XRMultiplayer.DaVinci.Editor
{
    /// <summary>
    /// Builds, captures and wires up poses for a plain humanoid character that has no Animation
    /// Rigging on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything here addresses bones through <see cref="Animator.GetBoneTransform"/> and the
    /// <see cref="HumanBodyBones"/> enum, so nothing depends on how the model names its bones. And
    /// every rotation is expressed as "aim this bone at that direction" via
    /// <see cref="Quaternion.FromToRotation"/>, so nothing depends on which local axis the exporter
    /// happened to run down the bone either. The same commands work on any Humanoid rig.
    /// </para>
    /// <para>
    /// Poses are stored as AnimationClips and played by an Animator. On a model with no rig that is
    /// the entire mechanism: one writer, no constraints to fight, and no per-frame code.
    /// </para>
    /// </remarks>
    public static class HumanoidPoseTool
    {
        const string k_Title = "Humanoid Pose Tool";
        const string k_Folder = "Assets/DaVinci/Generated";

        /// <summary>How far the pelvis slides toward the edge of the bed when sitting up.</summary>
        const float k_EdgeSlide = 0.32f;

        [MenuItem("Tools/Da Vinci/Patient Poses/Build Sleeping Pose", false, 20)]
        public static void BuildSleeping()
        {
            if (!Begin(out var animator, out var hips))
                return;

            Undo.RegisterFullObjectHierarchyUndo(animator.gameObject, "Build Sleeping Pose");

            var spineDirection = Flatten(BonePosition(animator, HumanBodyBones.Head) - hips.position);
            var lateral = Vector3.Cross(spineDirection, Vector3.up).normalized;
            var footward = -spineDirection;

            // Torso straight, in line with the body.
            Aim(animator, HumanBodyBones.Hips, HumanBodyBones.Spine, spineDirection);
            Aim(animator, HumanBodyBones.Spine, HumanBodyBones.Chest, spineDirection);
            Aim(animator, HumanBodyBones.Chest, HumanBodyBones.Neck, spineDirection);
            Aim(animator, HumanBodyBones.Neck, HumanBodyBones.Head, spineDirection);

            // Legs straight out, slightly apart.
            Aim(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, (footward - lateral * 0.08f).normalized);
            Aim(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, (footward + lateral * 0.08f).normalized);
            Aim(animator, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, footward);
            Aim(animator, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, footward);

            // Arms resting at the sides, a touch away from the body and a touch into the mattress.
            Aim(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, (footward - lateral * 0.35f - Vector3.up * 0.1f).normalized);
            Aim(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, (footward + lateral * 0.35f - Vector3.up * 0.1f).normalized);
            Aim(animator, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, (footward - lateral * 0.12f).normalized);
            Aim(animator, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, (footward + lateral * 0.12f).normalized);

            Finish(animator, "sleeping");
        }

        [MenuItem("Tools/Da Vinci/Patient Poses/Build Waking Pose", false, 21)]
        public static void BuildWaking()
        {
            if (!Begin(out var animator, out var hips))
                return;

            Undo.RegisterFullObjectHierarchyUndo(animator.gameObject, "Build Waking Pose");

            var spineDirection = Flatten(BonePosition(animator, HumanBodyBones.Head) - hips.position);
            var lateral = Vector3.Cross(spineDirection, Vector3.up).normalized;

            // Which side the patient's own right is, read off the hand rather than assumed.
            var rightHand = BonePosition(animator, HumanBodyBones.RightHand);
            var outward = lateral * (Vector3.Dot(rightHand - hips.position, lateral) >= 0f ? 1f : -1f);

            // Torso comes upright: the spine now runs vertically out of the hips.
            Aim(animator, HumanBodyBones.Hips, HumanBodyBones.Spine, Vector3.up);
            Aim(animator, HumanBodyBones.Spine, HumanBodyBones.Chest, Vector3.up);
            Aim(animator, HumanBodyBones.Chest, HumanBodyBones.Neck, Vector3.up);
            Aim(animator, HumanBodyBones.Neck, HumanBodyBones.Head, Vector3.up);

            // Turn the pelvis so the body faces off the side of the bed. A person facing f has
            // their hip axis along cross(up, f), so aiming the hip axis there sets the facing
            // without needing to know which way the model calls forward.
            TurnHipsToFace(animator, hips, outward);

            // Thighs out over the edge, shins hanging down.
            Aim(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, outward);
            Aim(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, outward);
            Aim(animator, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, Vector3.down);
            Aim(animator, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, Vector3.down);

            // Arms hang, elbows a little forward so they do not clip the torso.
            var forward = outward;
            Aim(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, (Vector3.down + forward * 0.15f).normalized);
            Aim(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, (Vector3.down + forward * 0.15f).normalized);
            Aim(animator, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, (Vector3.down + forward * 0.35f).normalized);
            Aim(animator, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, (Vector3.down + forward * 0.35f).normalized);

            // Slide the pelvis to the edge. The pelvis rather than the root, because a clip stores
            // bones relative to the root and a root move would not be captured at all.
            hips.position += outward * k_EdgeSlide;

            Finish(animator, "waking");
        }

        [MenuItem("Tools/Da Vinci/Patient Poses/Capture As Sleeping", false, 40)]
        public static void CaptureSleeping() => Capture("Sleeping", true);

        [MenuItem("Tools/Da Vinci/Patient Poses/Capture As Waking", false, 41)]
        public static void CaptureWaking() => Capture("Waking", false);

        /// <summary>
        /// Bakes the pose the character is in right now into a clip and stores it as a state.
        /// </summary>
        static void Capture(string poseName, bool makeDefault)
        {
            if (!Begin(out var animator, out _))
                return;

            if (!AssetDatabase.IsValidFolder(k_Folder))
                AssetDatabase.CreateFolder("Assets/DaVinci", "Generated");

            var clip = BuildPoseClip(animator.transform, out var boneCount);
            var clipPath = $"{k_Folder}/{animator.name}_{poseName}.anim";
            AssetDatabase.DeleteAsset(clipPath);
            AssetDatabase.CreateAsset(clip, clipPath);

            var controllerPath = $"{k_Folder}/{animator.name}_Poses.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath)
                             ?? AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            var stateMachine = controller.layers[0].stateMachine;
            AnimatorState state = null;
            foreach (var child in stateMachine.states)
            {
                if (child.state != null && child.state.name == poseName)
                {
                    state = child.state;
                    break;
                }
            }

            state ??= stateMachine.AddState(poseName);
            state.motion = clip;
            if (makeDefault)
                stateMachine.defaultState = state;

            Undo.RecordObject(animator, "Capture Pose");
            animator.runtimeAnimatorController = controller;

            // A Humanoid Animator routes everything through muscle space, where plain transform
            // curves like these are ignored. Clearing the avatar binds the clip by path instead.
            animator.avatar = null;
            animator.applyRootMotion = false;
            EditorUtility.SetDirty(animator);

            var library = animator.GetComponent<HumanoidPoseLibrary>();
            if (library == null)
                Undo.AddComponent<HumanoidPoseLibrary>(animator.gameObject);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(animator.gameObject.scene);

            Debug.Log($"[{k_Title}] Captured \"{poseName}\" from {animator.name}.\n" +
                      $"  {boneCount} bones into {clipPath}\n  Stored as a state in {controllerPath}",
                animator);
        }

        /// <summary>
        /// Takes two separately posed copies of the same character and turns them into one
        /// character that animates between the poses.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The two objects are only ever used as pose references. Their bones are read into clips,
        /// both keyed by paths relative to their own root — identical models give identical paths,
        /// so a clip read off one plays correctly on the other. The sleeping copy becomes the live
        /// patient and the waking copy is switched off, having served its purpose.
        /// </para>
        /// <para>
        /// The transition is produced by the crossfade itself. Interpolating each bone's local
        /// rotation between two poses that are both anatomically valid gives a natural-looking
        /// movement without a frame of it being authored, which is exactly how ordinary character
        /// animation works.
        /// </para>
        /// <para>
        /// If the waking copy was posed standing somewhere else in the room, that displacement lives
        /// on its root, and a clip stores bones relative to the root rather than the root itself. It
        /// is folded into the hips instead so the patient still moves to the edge of the bed.
        /// </para>
        /// </remarks>
        [MenuItem("Tools/Da Vinci/Patient Poses/Build Transition From Two Objects", false, 60)]
        public static void BuildTransitionFromTwoObjects()
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog(k_Title, "Leave play mode first.", "OK");
                return;
            }

            if (!ResolvePair(out var sleeping, out var waking))
                return;

            // An Animator is the thing that plays the clips, so if the character has not got one it
            // is added rather than refused. Nothing about this command needs a Humanoid avatar: the
            // poses are matched by transform path, which works on any skeleton.
            var animator = sleeping.GetComponent<Animator>();
            var addedAnimator = animator == null;
            if (addedAnimator)
                animator = Undo.AddComponent<Animator>(sleeping);

            var mismatched = ComparePaths(sleeping.transform, waking.transform);
            if (mismatched > 0)
            {
                if (!EditorUtility.DisplayDialog(k_Title,
                        $"\"{sleeping.name}\" and \"{waking.name}\" differ in {mismatched} bone path(s), so they may " +
                        "not be the same model. The waking pose will only apply to bones that match.\n\nContinue?",
                        "Continue", "Cancel"))
                    return;
            }

            if (!AssetDatabase.IsValidFolder(k_Folder))
                AssetDatabase.CreateFolder("Assets/DaVinci", "Generated");

            var controllerPath = $"{k_Folder}/{sleeping.name}_Poses.controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var stateMachine = controller.layers[0].stateMachine;

            var sleepClip = BakeFrom(sleeping.transform, sleeping.transform, $"{k_Folder}/{sleeping.name}_Sleeping.anim", out var sleepBones, out var sleepNote);
            var wakeClip = BakeFrom(waking.transform, sleeping.transform, $"{k_Folder}/{sleeping.name}_Waking.anim", out var wakeBones, out var wakeNote);

            var sleepState = stateMachine.AddState("Sleeping");
            sleepState.motion = sleepClip;
            stateMachine.defaultState = sleepState;

            var wakeState = stateMachine.AddState("Waking");
            wakeState.motion = wakeClip;

            Undo.RecordObject(animator, "Build Pose Transition");
            animator.runtimeAnimatorController = controller;

            // A Humanoid Animator routes everything through muscle space, where plain transform
            // curves like these are ignored. Clearing the avatar binds the clip by path instead.
            animator.avatar = null;
            animator.applyRootMotion = false;
            EditorUtility.SetDirty(animator);

            if (sleeping.GetComponent<HumanoidPoseLibrary>() == null)
                Undo.AddComponent<HumanoidPoseLibrary>(sleeping);

            Undo.RecordObject(waking, "Build Pose Transition");
            waking.SetActive(false);
            EditorUtility.SetDirty(waking);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(sleeping.scene);

            Debug.Log($"[{k_Title}] Built the transition on \"{sleeping.name}\".\n" +
                      (addedAnimator ? "  Added an Animator, which it did not have.\n" : string.Empty) +
                      $"  Sleeping: {sleepBones} bones ({sleepNote})\n  Waking: {wakeBones} bones from \"{waking.name}\" ({wakeNote})\n" +
                      $"  Controller: {controllerPath}\n" +
                      $"  Switched \"{waking.name}\" off; it is only a pose reference now.\n" +
                      "  Point the Wake Up button at HumanoidPoseLibrary.PlayWaking.", sleeping);
        }

        /// <summary>
        /// Repoints the patient panel's buttons at the character that actually has the poses on it.
        /// </summary>
        /// <remarks>
        /// The panel was built for the earlier rigged patient, so its buttons still call that
        /// object's pose controller. Pressing Wake Up therefore drives a character that is no longer
        /// the one in the bed, which looks exactly like the button doing nothing.
        /// </remarks>
        [MenuItem("Tools/Da Vinci/Patient Poses/Wire Patient Buttons To This Character", false, 61)]
        public static void WirePatientButtons()
        {
            var library = Object.FindAnyObjectByType<HumanoidPoseLibrary>(FindObjectsInactive.Include);
            if (library == null)
            {
                EditorUtility.DisplayDialog(k_Title,
                    "No HumanoidPoseLibrary in the scene. Build the transition first.", "OK");
                return;
            }

            // The library falls back to GetComponent at runtime, but an empty field in the Inspector
            // reads as broken, and it costs nothing to fill in.
            var animator = library.GetComponent<Animator>();
            if (animator != null)
            {
                var so = new SerializedObject(library);
                var field = so.FindProperty("m_Animator");
                if (field != null && field.objectReferenceValue == null)
                {
                    field.objectReferenceValue = animator;
                    so.ApplyModifiedProperties();
                }
            }

            var wired = 0;
            foreach (var button in Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var label = button.gameObject.name.ToLowerInvariant();
                var waking = label.Contains("wake") || label.Contains("awake");
                var sleeping = label.Contains("rest") || label.Contains("sleep");
                if (!waking && !sleeping)
                    continue;

                Undo.RecordObject(button, "Wire Patient Buttons");

                for (var i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                    UnityEventTools.RemovePersistentListener(button.onClick, i);

                if (waking)
                    UnityEventTools.AddVoidPersistentListener(button.onClick, library.PlayWaking);
                else
                    UnityEventTools.AddVoidPersistentListener(button.onClick, library.PlaySleeping);

                EditorUtility.SetDirty(button);
                wired++;
            }

            EditorSceneManager.MarkSceneDirty(library.gameObject.scene);
            Debug.Log($"[{k_Title}] Pointed {wired} button(s) at \"{library.name}\".", library);
        }

        /// <summary>Finds the two posed copies, by name where possible and by selection otherwise.</summary>
        static bool ResolvePair(out GameObject sleeping, out GameObject waking)
        {
            sleeping = null;
            waking = null;

            var selected = Selection.gameObjects;
            if (selected.Length == 2)
            {
                foreach (var go in selected)
                {
                    if (go.name.ToLowerInvariant().Contains("wak"))
                        waking = go;
                    else
                        sleeping = go;
                }

                // Names gave nothing to go on, so fall back to selection order.
                if (sleeping == null || waking == null)
                {
                    sleeping = selected[0];
                    waking = selected[1];
                }

                return true;
            }

            EditorUtility.DisplayDialog(k_Title,
                "Select both posed objects — the sleeping one and the waking one.\n\n" +
                "Whichever is named like \"Waking\" is treated as the target pose; otherwise the first " +
                "selected is the sleeping one.", "OK");
            return false;
        }

        /// <summary>
        /// Finds the pelvis, without requiring the model to be set up as Humanoid.
        /// </summary>
        /// <remarks>
        /// Prefers the avatar when there is one. Otherwise it falls back to the skin's own root
        /// bone, which is the pelvis on essentially every rig, since that is the bone a skinned mesh
        /// is authored around.
        /// </remarks>
        static Transform FindHips(Transform root)
        {
            var animator = root.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                var mapped = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (mapped != null)
                    return mapped;
            }

            // By name next, ahead of the skin's root bone. A skinned mesh is not obliged to be
            // rooted at the pelvis — it is often the armature node above it — and picking that
            // instead rebases a transform nobody was asking about while leaving the hips exactly
            // where they were. The symptom is the rebase appearing to do nothing at all.
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t != root && t.name.EndsWith("Hips", System.StringComparison.OrdinalIgnoreCase))
                    return t;
            }

            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.rootBone != null)
                    return skin.rootBone;
            }

            return null;
        }

        /// <summary>Counts bone paths present in one hierarchy but not the other.</summary>
        static int ComparePaths(Transform a, Transform b)
        {
            var inA = new HashSet<string>();
            foreach (var t in a.GetComponentsInChildren<Transform>(true))
            {
                if (t != a)
                    inA.Add(AnimationUtility.CalculateTransformPath(t, a));
            }

            var mismatched = 0;
            foreach (var t in b.GetComponentsInChildren<Transform>(true))
            {
                if (t != b && !inA.Contains(AnimationUtility.CalculateTransformPath(t, b)))
                    mismatched++;
            }

            return mismatched;
        }

        /// <summary>
        /// Bakes <paramref name="source"/>'s pose into a clip, folding any difference between the
        /// two roots into the hips so a pose authored elsewhere in the room still lands correctly.
        /// </summary>
        static AnimationClip BakeFrom(Transform source, Transform liveRoot, string path, out int boneCount, out string note)
        {
            var clip = BuildPoseClip(source, out boneCount);
            note = source == liveRoot ? "read in place, no rebase needed" : "NOT REBASED - no hips found";

            if (source != liveRoot)
            {
                var hips = FindHips(source);
                if (hips != null)
                {
                    // The two copies are separate prefabs and do not agree on where the root sits
                    // relative to the body — here by about a metre. Left alone, the clip carries the
                    // reference's own internal offset, which only lands correctly if the live root
                    // happens to sit exactly where that reference's root sits. It does not, so the
                    // hips ended up on the floor.
                    //
                    // Rebasing fixes that: the hips are placed so they land at the world position
                    // the reference's hips occupy, measured against the live hierarchy. The pose
                    // then appears exactly where the reference object appears, whatever either
                    // root's own offset happens to be.
                    var hipsPath = AnimationUtility.CalculateTransformPath(hips, source);
                    var slash = hipsPath.LastIndexOf('/');
                    var parentPath = slash >= 0 ? hipsPath.Substring(0, slash) : string.Empty;

                    // Measured against the live counterpart of whatever the hips hang off, so this
                    // holds whether that is the root itself or an armature node beneath it.
                    var liveParent = string.IsNullOrEmpty(parentPath) ? liveRoot : liveRoot.Find(parentPath);
                    if (liveParent != null)
                    {
                        var before = hips.localPosition;
                        var wanted = liveParent.InverseTransformPoint(hips.position);
                        SetConstant(clip, hipsPath, "m_LocalPosition.x", wanted.x);
                        SetConstant(clip, hipsPath, "m_LocalPosition.y", wanted.y);
                        SetConstant(clip, hipsPath, "m_LocalPosition.z", wanted.z);
                        note = $"rebased \"{hipsPath}\" y {before.y:0.000} -> {wanted.y:0.000}";
                    }
                    else
                    {
                        note = $"NOT REBASED - no live counterpart for \"{parentPath}\"";
                    }
                }
            }

            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        // ---- pose primitives -------------------------------------------------------------

        /// <summary>
        /// Turns <paramref name="bone"/> so the line to <paramref name="child"/> points along
        /// <paramref name="direction"/>. Children follow, as they would in any joint.
        /// </summary>
        /// <remarks>
        /// Stated as a direction to aim at rather than an angle about an axis, so it holds whatever
        /// local axis convention the model was exported with.
        /// </remarks>
        static void Aim(Animator animator, HumanBodyBones bone, HumanBodyBones child, Vector3 direction)
        {
            var from = animator.GetBoneTransform(bone);
            var to = animator.GetBoneTransform(child);
            if (from == null || to == null || direction.sqrMagnitude < 1e-8f)
                return;

            var current = to.position - from.position;
            if (current.sqrMagnitude < 1e-8f)
                return;

            from.rotation = Quaternion.FromToRotation(current.normalized, direction.normalized) * from.rotation;
        }

        /// <summary>Yaws the pelvis so the body faces <paramref name="facing"/>.</summary>
        static void TurnHipsToFace(Animator animator, Transform hips, Vector3 facing)
        {
            var left = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var right = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (left == null || right == null)
                return;

            var hipAxis = Flatten(right.position - left.position);
            var wanted = Vector3.Cross(Vector3.up, facing).normalized;
            hips.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(hipAxis, wanted, Vector3.up), Vector3.up) * hips.rotation;
        }

        static Vector3 BonePosition(Animator animator, HumanBodyBones bone)
        {
            var t = animator.GetBoneTransform(bone);
            return t != null ? t.position : animator.transform.position;
        }

        static Vector3 Flatten(Vector3 direction)
        {
            var flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            return flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
        }

        // ---- plumbing --------------------------------------------------------------------

        static bool Begin(out Animator animator, out Transform hips)
        {
            animator = null;
            hips = null;

            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog(k_Title, "Leave play mode first.", "OK");
                return false;
            }

            var selected = Selection.activeGameObject;
            if (selected != null)
                animator = selected.GetComponentInParent<Animator>();

            if (animator == null)
            {
                EditorUtility.DisplayDialog(k_Title,
                    "Select the character (or any part of it) first. It needs an Animator with a Humanoid avatar.", "OK");
                return false;
            }

            if (!animator.isHuman)
            {
                EditorUtility.DisplayDialog(k_Title,
                    $"\"{animator.name}\" is not set up as Humanoid. Set its model's Rig > Animation Type to Humanoid " +
                    "and apply, then run this again.", "OK");
                return false;
            }

            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null)
            {
                EditorUtility.DisplayDialog(k_Title, "The avatar has no hip bone mapped.", "OK");
                return false;
            }

            return true;
        }

        static void Finish(Animator animator, string what)
        {
            EditorUtility.SetDirty(animator.gameObject);
            EditorSceneManager.MarkSceneDirty(animator.gameObject.scene);
            Debug.Log($"[{k_Title}] Built a {what} pose on {animator.name}. Adjust anything that looks off, " +
                      $"then use Tools > Da Vinci > Patient Poses > Capture As ... to store it.", animator);
        }

        static AnimationClip BuildPoseClip(Transform root, out int boneCount)
        {
            var clip = new AnimationClip();
            boneCount = 0;

            foreach (var bone in root.GetComponentsInChildren<Transform>(true))
            {
                if (bone == root)
                    continue;

                var path = AnimationUtility.CalculateTransformPath(bone, root);
                var p = bone.localPosition;
                var r = bone.localRotation;

                SetConstant(clip, path, "m_LocalPosition.x", p.x);
                SetConstant(clip, path, "m_LocalPosition.y", p.y);
                SetConstant(clip, path, "m_LocalPosition.z", p.z);
                SetConstant(clip, path, "m_LocalRotation.x", r.x);
                SetConstant(clip, path, "m_LocalRotation.y", r.y);
                SetConstant(clip, path, "m_LocalRotation.z", r.z);
                SetConstant(clip, path, "m_LocalRotation.w", r.w);
                boneCount++;
            }

            clip.EnsureQuaternionContinuity();

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        static void SetConstant(AnimationClip clip, string path, string property, float value)
        {
            var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), property);
            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 1f / 60f, value));
        }
    }
}
