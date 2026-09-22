using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace XRMultiplayer.DaVinci.EditorTools
{
    /// <summary>
    /// One-shot scene wiring for the bedridden patient: body colliders, grabbable hands, the pose
    /// controller and the world-space panel that drives it.
    /// </summary>
    /// <remarks>
    /// Written as an editor tool rather than authored into the scene file directly because the
    /// component references, prefab overrides and serialized UnityEvents involved are all far
    /// safer to build through the API than to hand-write as YAML. Re-running is harmless; every
    /// step reuses what it finds.
    /// </remarks>
    public static class PatientSetupTool
    {
        const string k_PatientName = "VR Patient IK";
        const string k_PanelName = "Patient State Panel";

        static readonly string[] k_HandTargets = { "Left Arm IK_target", "Right Arm IK_target" };

        [MenuItem("DaVinci/Patient/Setup Patient Controls")]
        public static void Setup()
        {
            var patient = FindPatient();
            if (patient == null)
            {
                EditorUtility.DisplayDialog("Patient Setup",
                    $"Could not find a GameObject called \"{k_PatientName}\" in the open scene.", "OK");
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(patient, "Setup Patient Controls");

            var log = new List<string>();
            var controller = SetupController(patient, log);

            // Must run before anything caches the pose: the targets ship at standing coordinates,
            // so until they are snapped the "rest" pose is the contorted one.
            SnapTargetsToPose();

            // The pose only survives play mode if the Animator is actually outputting it.
            BakeRestingPoseClip();

            SetupBodyColliders(patient, log);
            SetupGrabbableHands(patient, controller, log);
            SetupPanel(patient, controller, log);

            EditorUtility.SetDirty(patient);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(patient.scene);

            Debug.Log("[Patient Setup] Complete.\n  " + string.Join("\n  ", log), patient);
        }

        static GameObject FindPatient()
        {
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == k_PatientName)
                    return root;

                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == k_PatientName)
                        return child.gameObject;
                }
            }

            return null;
        }

        static PatientPoseController SetupController(GameObject patient, List<string> log)
        {
            var controller = patient.GetComponent<PatientPoseController>();
            if (controller == null)
            {
                controller = Undo.AddComponent<PatientPoseController>(patient);
                log.Add("Added PatientPoseController.");
            }

            controller.AutoBind();
            controller.CaptureRestPose();
            EditorUtility.SetDirty(controller);
            log.Add("Bound spine bones and IK targets, and captured the current pose as Resting.");
            return controller;
        }

        /// <summary>
        /// Replaces the empty MeshCollider that shipped on the patient with capsules sized from the
        /// avatar's own bone spacing, each on a kinematic body so it can follow the pose without
        /// forcing a static-collider rebuild every frame.
        /// </summary>
        static void SetupBodyColliders(GameObject patient, List<string> log)
        {
            foreach (var mesh in patient.GetComponentsInChildren<MeshCollider>(true))
            {
                if (mesh.sharedMesh == null)
                {
                    log.Add($"Removed inert MeshCollider (no mesh assigned) from \"{mesh.gameObject.name}\".");
                    Undo.DestroyObjectImmediate(mesh);
                }
            }

            AddCapsule(patient, "mixamorig8:Spine", "mixamorig8:Neck", 0.14f, log);
            AddCapsule(patient, "mixamorig8:LeftUpLeg", "mixamorig8:LeftLeg", 0.09f, log);
            AddCapsule(patient, "mixamorig8:RightUpLeg", "mixamorig8:RightLeg", 0.09f, log);

            var head = Find(patient, "mixamorig8:Head");
            if (head != null && head.GetComponent<SphereCollider>() == null)
            {
                var sphere = Undo.AddComponent<SphereCollider>(head.gameObject);
                sphere.radius = 0.11f;
                EnsureKinematicBody(head.gameObject);
                log.Add("Added head collider.");
            }
        }

        static void AddCapsule(GameObject patient, string boneName, string childName, float radius, List<string> log)
        {
            var bone = Find(patient, boneName);
            var child = Find(patient, childName);
            if (bone == null || child == null || bone.GetComponent<CapsuleCollider>() != null)
                return;

            var capsule = Undo.AddComponent<CapsuleCollider>(bone.gameObject);
            var offset = bone.InverseTransformPoint(child.position);

            // Align the capsule with whichever local axis actually runs down the bone, rather than
            // assuming a convention the rig may not follow.
            var axis = 0;
            if (Mathf.Abs(offset.y) >= Mathf.Abs(offset.x) && Mathf.Abs(offset.y) >= Mathf.Abs(offset.z))
                axis = 1;
            else if (Mathf.Abs(offset.z) >= Mathf.Abs(offset.x))
                axis = 2;

            capsule.direction = axis;
            capsule.center = offset * 0.5f;
            capsule.height = offset.magnitude + radius * 2f;
            capsule.radius = radius;

            EnsureKinematicBody(bone.gameObject);
            log.Add($"Added capsule collider on \"{boneName}\" (height {capsule.height:0.00}m).");
        }

        static Rigidbody EnsureKinematicBody(GameObject target)
        {
            var body = target.GetComponent<Rigidbody>();
            if (body == null)
                body = Undo.AddComponent<Rigidbody>(target);

            body.isKinematic = true;
            body.useGravity = false;
            return body;
        }

        static void SetupGrabbableHands(GameObject patient, PatientPoseController controller, List<string> log)
        {
            foreach (var targetName in k_HandTargets)
            {
                var target = Find(patient, targetName);
                if (target == null)
                {
                    log.Add($"WARNING: could not find \"{targetName}\"; skipped.");
                    continue;
                }

                if (target.GetComponent<SphereCollider>() == null)
                {
                    var collider = Undo.AddComponent<SphereCollider>(target.gameObject);
                    collider.radius = 0.07f;
                }

                EnsureKinematicBody(target.gameObject);

                var grab = target.GetComponent<XRGrabInteractable>();
                if (grab == null)
                    grab = Undo.AddComponent<XRGrabInteractable>(target.gameObject);

                // Instantaneous writes the transform directly, which matches how the pose controller
                // drives these targets. Routing it through the physics timestep instead would have
                // the interactor and the rig moving the same transform on different clocks.
                grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
                grab.trackPosition = true;
                grab.trackRotation = true;

                // The target has to stay under the rig for the IK constraint to keep resolving it,
                // and a thrown hand makes no sense here.
                grab.retainTransformParent = true;
                grab.throwOnDetach = false;

                if (target.GetComponent<PatientLimbHandle>() == null)
                    Undo.AddComponent<PatientLimbHandle>(target.gameObject);

                EditorUtility.SetDirty(target.gameObject);
                log.Add($"Made \"{targetName}\" grabbable.");
            }
        }

        static void SetupPanel(GameObject patient, PatientPoseController controller, List<string> log)
        {
            var existing = GameObject.Find(k_PanelName);
            if (existing != null)
            {
                log.Add("Panel already present; left as is.");
                return;
            }

            var hips = Find(patient, "mixamorig8:Hips");
            var head = Find(patient, "mixamorig8:Head");
            if (hips == null || head == null)
            {
                log.Add("WARNING: no hips/head bone, so the panel could not be placed.");
                return;
            }

            // The body's own left-right axis, so the panel lands beside the bed whichever way it faces.
            var spineDirection = (head.position - hips.position).normalized;
            var lateral = Vector3.Cross(spineDirection, Vector3.up).normalized;
            if (lateral.sqrMagnitude < 1e-6f)
                lateral = patient.transform.right;

            var panel = new GameObject(k_PanelName, typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(panel, "Create Patient Panel");

            var canvas = panel.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rect = panel.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(420f, 180f);
            rect.localScale = Vector3.one * 0.001f;
            rect.position = hips.position + Vector3.up * 0.55f + lateral * 0.75f;
            rect.rotation = Quaternion.LookRotation(lateral, Vector3.up);

            AddBackground(panel);
            AddTitle(panel);

            CreateButton(panel, "Resting", new Vector2(-105f, -35f), controller.GoToResting);
            CreateButton(panel, "Wake Up", new Vector2(105f, -35f), controller.GoToAwake);

            log.Add($"Created \"{k_PanelName}\" beside the bed with Resting and Wake Up buttons.");
        }

        static void AddBackground(GameObject panel)
        {
            var image = panel.AddComponent<Image>();
            image.color = new Color(0.07f, 0.09f, 0.12f, 0.88f);
        }

        static void AddTitle(GameObject panel)
        {
            var title = new GameObject("Title", typeof(RectTransform));
            title.transform.SetParent(panel.transform, false);

            var rect = title.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(0f, 55f);
            rect.sizeDelta = new Vector2(400f, 50f);

            var text = title.AddComponent<TextMeshProUGUI>();
            text.text = "Patient State";
            text.fontSize = 34f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
        }

        static void CreateButton(GameObject panel, string label, Vector2 position, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(label, typeof(RectTransform));
            go.transform.SetParent(panel.transform, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(180f, 70f);

            var image = go.AddComponent<Image>();
            image.color = new Color(0.16f, 0.42f, 0.72f, 1f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            // A persistent listener survives the domain reload and is visible in the Inspector,
            // unlike a runtime AddListener call.
            UnityEventTools.AddVoidPersistentListener(button.onClick, action);

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);

            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            textRect.anchoredPosition = Vector2.zero;

            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 28f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
        }

        /// <summary>One two-bone chain, paired with the rig objects that drive it.</summary>
        struct LimbChain
        {
            public string target;
            public string hint;
            public string root;
            public string mid;
            public string tip;

            /// <summary>True for legs, whose knee folds toward world up on a supine patient.</summary>
            public bool kneeStyle;
        }

        static readonly LimbChain[] k_Limbs =
        {
            new LimbChain { target = "Left Arm IK_target",  hint = "Left Arm IK_hint",  root = "mixamorig8:LeftArm",    mid = "mixamorig8:LeftForeArm",  tip = "mixamorig8:LeftHand"  },
            new LimbChain { target = "Right Arm IK_target", hint = "Right Arm IK_hint", root = "mixamorig8:RightArm",   mid = "mixamorig8:RightForeArm", tip = "mixamorig8:RightHand" },
            new LimbChain { target = "Left Leg IK_target",  hint = "Left Leg IK_hint",  root = "mixamorig8:LeftUpLeg",  mid = "mixamorig8:LeftLeg",      tip = "mixamorig8:LeftFoot",  kneeStyle = true },
            new LimbChain { target = "Right Leg IK_target", hint = "Right Leg IK_hint", root = "mixamorig8:RightUpLeg", mid = "mixamorig8:RightLeg",     tip = "mixamorig8:RightFoot", kneeStyle = true },
        };

        /// <summary>
        /// Moves every IK target onto the bone it drives, so the constraints resolve to the pose the
        /// avatar is already in rather than dragging the limbs somewhere else.
        /// </summary>
        /// <remarks>
        /// The targets shipped at their original standing coordinates — hands at chest height, feet
        /// near the floor — while the body itself was hand-posed lying down. With the constraint
        /// weights at 1 that mismatch is what folds the legs and splays the arms. Snapping each
        /// target onto its own bone makes the solve a no-op, which is what a resting pose should be.
        /// </remarks>
        [MenuItem("DaVinci/Patient/Snap IK Targets To Current Pose")]
        public static void SnapTargetsToPose()
        {
            var patient = FindPatient();
            if (patient == null)
            {
                EditorUtility.DisplayDialog("Patient Setup",
                    $"Could not find a GameObject called \"{k_PatientName}\" in the open scene.", "OK");
                return;
            }

            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Patient Setup",
                    "Leave play mode first. In play mode the rig is actively solving, so the bones " +
                    "read back in the contorted pose rather than the authored one.", "OK");
                return;
            }

            var log = new List<string>();

            // RigBuilder is [ExecuteInEditMode], so by the time this runs the constraints have
            // already dragged the bones toward the wrong targets. Stop it solving, then put the
            // bones back to what the scene actually has authored, or we would read the contorted
            // pose straight back out and bake the fault in.
            var builder = patient.GetComponentInChildren<RigBuilder>(true);
            var builderWasEnabled = builder != null && builder.enabled;
            if (builder != null)
                builder.enabled = false;

            var restored = RestoreAuthoredPose(patient);
            log.Add($"Restored {restored} bone transforms to their authored values before reading.");

            var hips = Find(patient, "mixamorig8:Hips");
            var head = Find(patient, "mixamorig8:Head");
            var lateral = Vector3.right;
            if (hips != null && head != null)
            {
                var candidate = Vector3.Cross((head.position - hips.position).normalized, Vector3.up);
                if (candidate.sqrMagnitude > 1e-6f)
                    lateral = candidate.normalized;
            }

            foreach (var limb in k_Limbs)
            {
                var target = Find(patient, limb.target);
                var root = Find(patient, limb.root);
                var mid = Find(patient, limb.mid);
                var tip = Find(patient, limb.tip);

                if (target == null || tip == null)
                {
                    log.Add($"WARNING: missing \"{limb.target}\" or \"{limb.tip}\"; skipped.");
                    continue;
                }

                Undo.RecordObject(target, "Snap IK Targets");
                target.position = tip.position;
                target.rotation = tip.rotation;
                log.Add($"Moved \"{limb.target}\" onto {limb.tip}.");

                var hint = Find(patient, limb.hint);
                if (hint == null || root == null || mid == null)
                    continue;

                var fallback = limb.kneeStyle
                    ? Vector3.up
                    : lateral * (Vector3.Dot(tip.position - (hips != null ? hips.position : root.position), lateral) >= 0f ? 1f : -1f);

                Undo.RecordObject(hint, "Snap IK Targets");
                hint.position = ResolveHintPosition(root, mid, tip, fallback);
            }

            var headTarget = Find(patient, "Head Target");
            if (headTarget != null && head != null)
            {
                Undo.RecordObject(headTarget, "Snap IK Targets");
                headTarget.position = head.position;
                headTarget.rotation = head.rotation;
                log.Add("Moved \"Head Target\" onto mixamorig8:Head.");
            }

            if (builder != null)
                builder.enabled = builderWasEnabled;

            // The controller's stored rest pose was captured from the old target layout, so it has
            // to be retaken or the Resting button would blend straight back to the bad pose.
            var controller = patient.GetComponent<PatientPoseController>();
            if (controller != null)
            {
                controller.CaptureRestPose();
                EditorUtility.SetDirty(controller);
                log.Add("Re-captured the corrected pose as Resting.");
            }

            EditorUtility.SetDirty(patient);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(patient.scene);
            Debug.Log("[Patient Setup] Snapped IK targets.\n  " + string.Join("\n  ", log), patient);
        }

        const string k_GeneratedFolder = "Assets/DaVinci/Generated";

        /// <summary>
        /// Bakes the authored resting pose into an AnimationClip and gives the patient's Animator a
        /// controller that plays it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Without this the pose survives in edit mode but collapses on play. The rig's
        /// <c>SyncSceneToStreamLayer</c> only copies scene values for transforms a constraint
        /// actually references — targets, hints and sources. Every other bone (hips, spine,
        /// shoulders) starts each frame at the stream's default values, so the hand-authored pose is
        /// discarded the moment the playable graph runs and the IK then solves from a default body.
        /// </para>
        /// <para>
        /// Giving the Animator a clip puts the pose back at the bottom of the graph, where the
        /// constraints can layer on top of it instead of fighting a default pose. The avatar is
        /// cleared at the same time: the model imports as Humanoid, and a Humanoid Animator routes
        /// everything through muscle space, where plain transform curves like these are ignored.
        /// </para>
        /// </remarks>
        [MenuItem("DaVinci/Patient/Bake Resting Pose Clip")]
        public static void BakeRestingPoseClip()
        {
            var patient = FindPatient();
            if (patient == null)
            {
                EditorUtility.DisplayDialog("Patient Setup",
                    $"Could not find a GameObject called \"{k_PatientName}\" in the open scene.", "OK");
                return;
            }

            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Patient Setup", "Leave play mode first.", "OK");
                return;
            }

            var animator = patient.GetComponent<Animator>();
            if (animator == null)
            {
                EditorUtility.DisplayDialog("Patient Setup",
                    "The patient has no Animator, which Animation Rigging requires.", "OK");
                return;
            }

            var builder = patient.GetComponentInChildren<RigBuilder>(true);
            var builderWasEnabled = builder != null && builder.enabled;
            if (builder != null)
                builder.enabled = false;

            var restored = RestoreAuthoredPose(patient);

            if (!AssetDatabase.IsValidFolder(k_GeneratedFolder))
                AssetDatabase.CreateFolder("Assets/DaVinci", "Generated");

            var clip = BuildPoseClip(patient, out var boneCount);
            var clipPath = $"{k_GeneratedFolder}/PatientRestingPose.anim";
            AssetDatabase.DeleteAsset(clipPath);
            AssetDatabase.CreateAsset(clip, clipPath);

            var controllerPath = $"{k_GeneratedFolder}/PatientPose.controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var stateMachine = controller.layers[0].stateMachine;
            var state = stateMachine.AddState("Resting");
            state.motion = clip;
            stateMachine.defaultState = state;

            Undo.RecordObject(animator, "Bake Resting Pose");
            animator.runtimeAnimatorController = controller;
            animator.avatar = null;
            animator.applyRootMotion = false;
            EditorUtility.SetDirty(animator);

            if (builder != null)
                builder.enabled = builderWasEnabled;

            AssetDatabase.SaveAssets();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(patient.scene);

            Debug.Log($"[Patient Setup] Baked resting pose.\n  Restored {restored} bones.\n  " +
                      $"Wrote {boneCount} bones into {clipPath}.\n  Assigned {controllerPath} and cleared the Humanoid avatar.",
                patient);
        }

        /// <summary>
        /// Builds a one-pose clip from the bones' current local transforms.
        /// </summary>
        static AnimationClip BuildPoseClip(GameObject patient, out int boneCount)
        {
            var clip = new AnimationClip { name = "PatientRestingPose" };
            var root = patient.transform;
            boneCount = 0;

            foreach (var bone in patient.GetComponentsInChildren<Transform>(true))
            {
                if (bone == root)
                    continue;

                // Only bones belonging to the model. The rig and its IK targets must stay free for
                // the constraints and the pose controller to move.
                if (PrefabUtility.GetCorrespondingObjectFromSource(bone) == null)
                    continue;

                var path = AnimationUtility.CalculateTransformPath(bone, root);
                var position = bone.localPosition;
                var rotation = bone.localRotation;

                SetConstantCurve(clip, path, "m_LocalPosition.x", position.x);
                SetConstantCurve(clip, path, "m_LocalPosition.y", position.y);
                SetConstantCurve(clip, path, "m_LocalPosition.z", position.z);
                SetConstantCurve(clip, path, "m_LocalRotation.x", rotation.x);
                SetConstantCurve(clip, path, "m_LocalRotation.y", rotation.y);
                SetConstantCurve(clip, path, "m_LocalRotation.z", rotation.z);
                SetConstantCurve(clip, path, "m_LocalRotation.w", rotation.w);
                boneCount++;
            }

            clip.EnsureQuaternionContinuity();

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            return clip;
        }

        static void SetConstantCurve(AnimationClip clip, string path, string property, float value)
        {
            var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), property);
            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 1f / 60f, value));
        }

        /// <summary>
        /// Puts every bone back to the transform the scene actually has authored for it: the prefab
        /// instance's own override where there is one, and the source model's value where there is
        /// not. Live transforms cannot be trusted here because the rig has already written to them.
        /// </summary>
        static int RestoreAuthoredPose(GameObject patient)
        {
            var instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(patient);
            if (instanceRoot == null)
                return 0;

            var overrides = new Dictionary<UnityEngine.Object, Dictionary<string, float>>();
            var modifications = PrefabUtility.GetPropertyModifications(instanceRoot);

            if (modifications != null)
            {
                foreach (var modification in modifications)
                {
                    if (modification.target == null || !modification.propertyPath.StartsWith("m_Local"))
                        continue;

                    if (!float.TryParse(modification.value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                        continue;

                    if (!overrides.TryGetValue(modification.target, out var fields))
                        overrides[modification.target] = fields = new Dictionary<string, float>();

                    fields[modification.propertyPath] = value;
                }
            }

            var count = 0;
            foreach (var bone in patient.GetComponentsInChildren<Transform>(true))
            {
                // Objects added to the instance (the rig, the IK targets) have no source and must
                // be left alone.
                var source = PrefabUtility.GetCorrespondingObjectFromSource(bone) as Transform;
                if (source == null)
                    continue;

                overrides.TryGetValue(source, out var fields);

                Undo.RecordObject(bone, "Restore Authored Pose");
                bone.localPosition = ReadVector(fields, source.localPosition);
                bone.localRotation = ReadQuaternion(fields, source.localRotation);
                count++;
            }

            return count;
        }

        static Vector3 ReadVector(Dictionary<string, float> fields, Vector3 fallback)
        {
            if (fields == null)
                return fallback;

            return new Vector3(
                fields.TryGetValue("m_LocalPosition.x", out var x) ? x : fallback.x,
                fields.TryGetValue("m_LocalPosition.y", out var y) ? y : fallback.y,
                fields.TryGetValue("m_LocalPosition.z", out var z) ? z : fallback.z);
        }

        static Quaternion ReadQuaternion(Dictionary<string, float> fields, Quaternion fallback)
        {
            if (fields == null)
                return fallback;

            return new Quaternion(
                fields.TryGetValue("m_LocalRotation.x", out var x) ? x : fallback.x,
                fields.TryGetValue("m_LocalRotation.y", out var y) ? y : fallback.y,
                fields.TryGetValue("m_LocalRotation.z", out var z) ? z : fallback.z,
                fields.TryGetValue("m_LocalRotation.w", out var w) ? w : fallback.w);
        }

        /// <summary>
        /// Places a hint so the joint keeps bending the way the authored pose already bends it.
        /// </summary>
        static Vector3 ResolveHintPosition(Transform root, Transform mid, Transform tip, Vector3 fallbackDirection)
        {
            var direction = Vector3.zero;
            var chord = tip.position - root.position;

            if (chord.sqrMagnitude > 1e-8f)
            {
                // How far the middle joint sits off the straight line from root to tip; that offset
                // is the bend plane.
                var projected = root.position + Vector3.Project(mid.position - root.position, chord.normalized);
                direction = mid.position - projected;
            }

            // A limb lying straight has no meaningful bend plane, and a near-straight one only
            // yields noise, so below a centimetre of offset fall back to anatomy instead.
            if (direction.sqrMagnitude < 1e-4f)
                direction = fallbackDirection;

            return mid.position + direction.normalized * 0.35f;
        }

        static Transform Find(GameObject root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                    return child;
            }

            return null;
        }
    }
}
