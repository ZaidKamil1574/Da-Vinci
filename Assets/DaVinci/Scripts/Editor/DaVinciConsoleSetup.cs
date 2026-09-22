using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.UI;
using UnityEngine.XR.Content.Interaction;

namespace XRMultiplayer.DaVinci.Editor
{
    /// <summary>
    /// Wires the angle overlays, the arm readouts, the ball resets and the lever-driven joint into
    /// the open scene in one pass, and builds the console panel that reports them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Doing this by hand means dragging something like forty references across a dozen objects
    /// buried eight levels deep in an imported hierarchy, and getting one wrong produces a panel
    /// that looks wired but reads nothing. The names are stable — they come from the FBX — so the
    /// whole arrangement can be rebuilt from them, and re-running after a model re-import is a
    /// menu click rather than an afternoon.
    /// </para>
    /// <para>
    /// Nothing here touches the networked lever's own components. The rotation is driven by a
    /// <see cref="LeverDrivenRotator"/> placed on a separate object that merely references the
    /// lever, so the prefab instance keeps its <c>NetworkObject</c>, its <c>NetworkXRLever</c> and
    /// every setting on them exactly as authored.
    /// </para>
    /// </remarks>
    public static class DaVinciConsoleSetup
    {
        const string k_Title = "Da Vinci Console Setup";

        /// <summary>Name of the imported model's root object.</summary>
        const string k_ModelRoot = "Da Vinci";

        /// <summary>Scene-root container holding everything this command adds.</summary>
        const string k_Container = "Da Vinci Console";

        /// <summary>The joint the lever turns.</summary>
        const string k_LeverJoint = "ROTARY MECHANISM_1.002";

        /// <summary>Joints outside any IK chain that still get an angle overlay and a readout.</summary>
        static readonly string[] k_StandaloneJoints = { "HAND_1.003", "HAND_2", "ROTARY MECHANISM_1.002" };

        /// <summary>
        /// Joints given a wedge inside each IK chain.
        /// </summary>
        /// <remarks>
        /// The chain rooted at HAND_BEGIN runs twelve transforms down to the instrument tip, and a
        /// wedge and a label on every one of them buries the arm in overlay. These are the joints
        /// that carry the rotation — the ones a reader means by "where the IK is bending it".
        /// Clearing the filter on the component shows every joint in the chain.
        /// </remarks>
        static readonly string[] k_ChainJointFilter = { "HAND_BEGIN", "ROTARY MECHANISM" };

        /// <summary>
        /// The two balls, in panel order, each given as the names it might go by.
        /// </summary>
        /// <remarks>
        /// A list rather than a single name because these objects get renamed. They arrive from the
        /// prefab as "Toy_Ball_Networked" and are commonly renamed to "Ball 1" and "Ball 2" once
        /// they are the arms' IK targets, and a setup that silently wires nothing when it cannot
        /// find an exact match is worse than one that tries the obvious alternatives.
        /// </remarks>
        static readonly string[][] k_BallNames =
        {
            new[] { "Ball 1", "Toy_Ball_Networked", "Toy_Ball" },
            new[] { "Ball 2", "Toy_Ball_Networked (1)", "Toy_Ball (1)" },
        };

        /// <summary>
        /// The joints plotted on the telemetry graphs, in legend order.
        /// </summary>
        /// <remarks>
        /// These four links carry the instrument, so their speed is what a reader means by "how
        /// fast is the arm moving". Their colours come from <see cref="DaVinciJointPalette"/> and
        /// therefore match the wedges drawn on the same joints out in the world.
        /// </remarks>
        /// <summary>
        /// Joints plotted on the telemetry graphs, as (object name, legend label, trace colour).
        /// </summary>
        /// <remarks>
        /// A colour left clear defers to <see cref="DaVinciJointPalette"/>, which is what keeps a
        /// trace the same colour as the wedge drawn on its joint out in the world. The second and
        /// third arms cannot do that: the palette matches on prefix, so HAND_3.003 and HAND_3.004
        /// would both come back the same green as HAND_3 and the three arms would be
        /// indistinguishable on the chart. They are given their own colours instead.
        /// </remarks>
        static readonly (string name, string label, Color colour)[] k_GraphJoints =
        {
            ("HAND_1", "HAND 1", Color.clear),
            ("HAND_2", "HAND 1 · mid", Color.clear),
            ("HAND_3", "HAND 1 · fore", Color.clear),
            ("HAND_4", "HAND 1 · tip", Color.clear),
            ("HAND_3.004", "HAND 2", new Color(1.00f, 0.40f, 0.80f)),
            ("HAND_3.003", "HAND 3", new Color(0.55f, 0.75f, 1.00f)),
        };

        const string k_LeverPrefabPath = "Assets/VRMPAssets/Prefabs/NetworkedPrefabs/NetworkedLever.prefab";

        // Listed in both menus on purpose. The GameObject menu is where the project's other Da
        // Vinci commands live, but those all act on the current selection and this one does not —
        // it finds the model by name — so Tools is where someone looking for a project-wide setup
        // command would reasonably look first.
        [MenuItem("Tools/Da Vinci/Set Up Console And Angle Readouts", false, 10)]
        [MenuItem("GameObject/Da Vinci/Set Up Console And Angle Readouts", false, 13)]
        static void SetUp()
        {
            var model = FindModelRoot();
            if (model == null)
            {
                Report($"Could not find the '{k_ModelRoot}' model in the open scene. Select it and run this again.");
                return;
            }

            var notes = new List<string>();
            var container = GetOrCreateContainer(model);
            var pose = PanelPose(model);

            var overlays = new List<JointAngleVisual>();
            var chains = BuildChainVisualizers(model, container, overlays, notes);
            overlays.AddRange(BuildStandaloneOverlays(model, notes));
            var balls = BuildBallResets(model, notes);
            var rotator = BuildLeverRotator(model, container, pose, notes);

            var forces = BuildForceArrows(chains, notes);
            var pause = BuildPauseControl(container, balls, rotator);

            RemoveStrayPanels(model, notes);
            BuildPanel(model, container, pose, chains, overlays, balls, rotator, notes);
            BuildGraphPanel(model, container, pose, notes);

            EditorSceneManager.MarkSceneDirty(model.gameObject.scene);
            Selection.activeGameObject = container.gameObject;

            var summary = new StringBuilder();
            summary.AppendLine($"Set up under '{container.name}'.").AppendLine();
            summary.AppendLine($"  IK chains watched : {chains.Count}");
            summary.AppendLine($"  Joint readouts    : {overlays.Count}");
            summary.AppendLine($"  Ball resets       : {balls.Count}");
            summary.AppendLine($"  Force arrows      : {forces}");
            summary.AppendLine($"  Pause control     : {(pause != null ? "A button" : "not wired")}");
            summary.AppendLine($"  Lever-driven joint: {(rotator != null ? k_LeverJoint : "not wired")}");

            if (notes.Count > 0)
            {
                summary.AppendLine().AppendLine("Still to do:");
                for (var i = 0; i < notes.Count; i++)
                    summary.AppendLine($"  {i + 1}. {notes[i]}");
            }

            Debug.Log($"[Da Vinci] {summary}", container);
            Report(summary.ToString());
        }

        /// <summary>
        /// Adds a <see cref="DaVinciIkChainVisualizer"/> for every chain constraint in the model.
        /// </summary>
        /// <remarks>
        /// Every constraint is picked up rather than only the one named in the request, because the
        /// four arms are built the same way and a reader comparing them learns more than one
        /// watching a single arm in isolation. A constraint with no target is still watched: its
        /// joints report their angles, and the reach line says it has nothing to reach for, which
        /// is the fastest way to notice an unassigned target.
        /// </remarks>
        static List<DaVinciIkChainVisualizer> BuildChainVisualizers(
            Transform model, Transform container, List<JointAngleVisual> overlays, List<string> notes)
        {
            var results = new List<DaVinciIkChainVisualizer>();
            var constraints = model.GetComponentsInChildren<ChainIKConstraint>(true);

            if (constraints.Length == 0)
                notes.Add("No ChainIKConstraint found under the model, so no IK chain is being watched.");

            foreach (var constraint in constraints)
            {
                var data = constraint.data;
                if (data.root == null || data.tip == null)
                {
                    notes.Add($"Chain IK on '{constraint.name}' has no Root or Tip set, so it was skipped.");
                    continue;
                }

                var host = GetOrCreateChild(container, $"Chain — {data.root.name} to {data.tip.name}");
                var visualizer = GetOrAddComponent<DaVinciIkChainVisualizer>(host.gameObject);

                var so = new SerializedObject(visualizer);
                so.FindProperty("m_Constraint").objectReferenceValue = constraint;
                so.FindProperty("m_ShowJointSpeed").boolValue = true;

                var filter = so.FindProperty("m_JointNameFilter");
                filter.arraySize = k_ChainJointFilter.Length;
                for (var i = 0; i < k_ChainJointFilter.Length; i++)
                    filter.GetArrayElementAtIndex(i).stringValue = k_ChainJointFilter[i];

                so.ApplyModifiedProperties();

                if (data.target == null)
                    notes.Add($"Chain IK '{data.root.name} → {data.tip.name}' has no Target, so that arm is not solving.");

                // Real, saved components on the chain's own joints. The visualizer can add these
                // itself at runtime, but only at runtime — adding them here is what makes the
                // wedges show up in the scene view before anyone presses Play, and what lets their
                // colour and size be tuned in the inspector.
                AddChainOverlays(data.root, data.tip, overlays);

                results.Add(visualizer);
            }

            return results;
        }

        /// <summary>
        /// Puts an angle readout on every joint between <paramref name="root"/> and
        /// <paramref name="tip"/> that matches <see cref="k_ChainJointFilter"/>.
        /// </summary>
        /// <remarks>
        /// Walks upward from the tip, not down from the root: a joint has exactly one parent, while
        /// the Da Vinci bones carry decorative children — fasteners, knurled knobs, indicator
        /// lights — so a downward search would have to guess which child continues the chain.
        /// </remarks>
        static void AddChainOverlays(Transform root, Transform tip, List<JointAngleVisual> overlays)
        {
            for (var joint = tip; joint != null; joint = joint.parent)
            {
                if (MatchesFilter(joint.name))
                    overlays.Add(AddOverlay(joint));

                if (joint == root)
                    return;
            }
        }

        static bool MatchesFilter(string jointName)
        {
            for (var i = 0; i < k_ChainJointFilter.Length; i++)
            {
                if (jointName.Contains(k_ChainJointFilter[i], System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>Gives one joint a measurement and a wedge, and turns its speed readout on.</summary>
        static JointAngleVisual AddOverlay(Transform joint)
        {
            GetOrAddComponent<JointMotionTracker>(joint.gameObject);

            var visual = GetOrAddComponent<JointAngleVisual>(joint.gameObject);

            var so = new SerializedObject(visual);
            so.FindProperty("m_ShowSpeed").boolValue = true;
            so.FindProperty("m_Colour").colorValue = DaVinciJointPalette.For(joint.name);
            so.ApplyModifiedProperties();

            return visual;
        }

        /// <summary>
        /// Puts an overlay on joints that no chain constraint drives.
        /// </summary>
        /// <remarks>
        /// HAND_1.003 and HAND_2 sit above their arms' chain roots, so the solver never touches
        /// them. They still move — they carry everything below them — and an angle on each is what
        /// shows how the arm is set up as opposed to how it is aiming.
        /// </remarks>
        static List<JointAngleVisual> BuildStandaloneOverlays(Transform model, List<string> notes)
        {
            var results = new List<JointAngleVisual>();

            foreach (var jointName in k_StandaloneJoints)
            {
                var joint = FindDescendant(model, jointName);
                if (joint == null)
                {
                    notes.Add($"No joint named '{jointName}' under the model, so it has no angle overlay.");
                    continue;
                }

                results.Add(AddOverlay(joint));
            }

            return results;
        }

        /// <summary>
        /// Gives each ball a reset component and hands back the ball transforms for the panel.
        /// </summary>
        /// <remarks>
        /// Transforms, not the components: the panel's slots take the ball itself so anything in
        /// the scene can be dropped on them, and it resolves the component at startup.
        /// </remarks>
        static List<Transform> BuildBallResets(Transform model, List<string> notes)
        {
            var results = new List<Transform>();

            for (var i = 0; i < k_BallNames.Length; i++)
            {
                var ball = FindInScene(model.gameObject.scene, k_BallNames[i]);
                if (ball == null)
                {
                    notes.Add(
                        $"Could not find ball {i + 1}. Looked for: {string.Join(", ", k_BallNames[i])}. " +
                        "Drag the ball onto the panel's Ball slot by hand, or rename it to one of those.");
                    results.Add(null);
                    continue;
                }

                GetOrAddComponent<NetworkedPoseReset>(ball.gameObject);
                results.Add(ball);
            }

            return results;
        }

        /// <summary>
        /// Finds the first object in <paramref name="scene"/> going by any of
        /// <paramref name="names"/>, inactive objects included.
        /// </summary>
        /// <remarks>
        /// <see cref="GameObject.Find"/> is not used here: it skips inactive objects without
        /// saying so, which is how a ball that happens to be switched off ends up silently unwired
        /// and a reset button that does nothing.
        /// </remarks>
        static Transform FindInScene(Scene scene, string[] names)
        {
            var roots = scene.GetRootGameObjects();

            for (var n = 0; n < names.Length; n++)
            {
                for (var r = 0; r < roots.Length; r++)
                {
                    var all = roots[r].GetComponentsInChildren<Transform>(true);
                    for (var i = 0; i < all.Length; i++)
                    {
                        if (all[i].name == names[n])
                            return all[i];
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Wires a lever to <see cref="k_LeverJoint"/>, adding the networked lever prefab to the
        /// scene if there is not one already.
        /// </summary>
        static LeverDrivenRotator BuildLeverRotator(Transform model, Transform container, Pose panelPose, List<string> notes)
        {
            var joint = FindDescendant(model, k_LeverJoint);
            if (joint == null)
            {
                notes.Add($"No joint named '{k_LeverJoint}' under the model, so the lever was not wired.");
                return null;
            }

            var lever = Object.FindAnyObjectByType<XRLever>(FindObjectsInactive.Include);
            if (lever == null)
            {
                lever = InstantiateLeverPrefab(container, panelPose, notes);
                if (lever == null)
                    return null;
            }

            // On its own object, not on the lever and not on the joint: the lever is a networked
            // prefab instance whose components must stay exactly as authored, and the joint belongs
            // to the imported model.
            var host = GetOrCreateChild(container, $"Lever Drive — {k_LeverJoint}");
            var rotator = GetOrAddComponent<LeverDrivenRotator>(host.gameObject);

            var axis = ChoosePitchAxis(joint);

            var so = new SerializedObject(rotator);
            so.FindProperty("m_Lever").objectReferenceValue = lever;
            so.FindProperty("m_Joint").objectReferenceValue = joint;
            so.FindProperty("m_RotationAxis").vector3Value = axis;
            so.ApplyModifiedProperties();

            notes.Add($"'{k_LeverJoint}' will pitch about its local {AxisName(axis)}. If it still " +
                      "turns the wrong way, negate that axis on the Lever Drive component.");

            return rotator;
        }

        static XRLever InstantiateLeverPrefab(Transform container, Pose panelPose, List<string> notes)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_LeverPrefabPath);
            if (prefab == null)
            {
                notes.Add($"No XRLever in the scene and the prefab at '{k_LeverPrefabPath}' is missing, so nothing drives {k_LeverJoint}.");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, container.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(instance, "Add Networked Lever");
            Undo.SetTransformParent(instance.transform, container, "Add Networked Lever");

            // Placed beside the panel and left there. The transform is the only override, so the
            // prefab's NetworkObject and NetworkXRLever arrive exactly as authored.
            instance.transform.SetPositionAndRotation(
                panelPose.position + panelPose.rotation * new Vector3(0.34f, -0.22f, 0f),
                panelPose.rotation);

            notes.Add("Added the NetworkedLever prefab beside the panel — move it somewhere the surgeon can reach.");

            return instance.GetComponentInChildren<XRLever>(true);
        }

        /// <summary>
        /// Puts a force arrow on each chain's instrument tip.
        /// </summary>
        /// <remarks>
        /// The tip is taken from the constraint rather than named directly: it is by definition the
        /// transform the solver drives to the target, which is the one place on the arm where a
        /// force reading means anything.
        /// </remarks>
        static int BuildForceArrows(List<DaVinciIkChainVisualizer> chains, List<string> notes)
        {
            var count = 0;
            var seen = new HashSet<Transform>();

            for (var i = 0; i < chains.Count; i++)
            {
                var tip = chains[i] != null ? chains[i].tip : null;

                // Two constraints in this scene drive the same tip, so without this the arrow would
                // be added twice and the second would sit invisibly inside the first.
                if (tip == null || !seen.Add(tip))
                    continue;

                GetOrAddComponent<TipForceVisual>(tip.gameObject);
                count++;
            }

            if (count == 0)
                notes.Add("No chain tips found, so no force arrows were added.");

            return count;
        }

        /// <summary>
        /// Adds the pause control and tells it what to hold still.
        /// </summary>
        static DaVinciPauseControl BuildPauseControl(
            Transform container, List<Transform> balls, LeverDrivenRotator rotator)
        {
            var host = GetOrCreateChild(container, "Pause Control");
            var pause = GetOrAddComponent<DaVinciPauseControl>(host.gameObject);

            var so = new SerializedObject(pause);

            var targets = so.FindProperty("m_Targets");
            targets.arraySize = 0;
            for (var i = 0; i < balls.Count; i++)
            {
                if (balls[i] == null)
                    continue;

                targets.arraySize++;
                targets.GetArrayElementAtIndex(targets.arraySize - 1).objectReferenceValue = balls[i];
            }

            var rotators = so.FindProperty("m_Rotators");
            rotators.arraySize = rotator != null ? 1 : 0;
            if (rotator != null)
                rotators.GetArrayElementAtIndex(0).objectReferenceValue = rotator;

            so.ApplyModifiedProperties();

            return pause;
        }

        /// <summary>
        /// Builds the second panel: a speed chart and a distance chart, sharing one legend.
        /// </summary>
        /// <remarks>
        /// A panel of its own, beside the controls rather than below them. The control panel is
        /// reached for and pressed; the charts are watched. Stacking them would push the buttons out
        /// of comfortable reach to make room for something nobody touches.
        /// </remarks>
        static void BuildGraphPanel(Transform model, Transform container, Pose consolePose, List<string> notes)
        {
            var existing = container.Find("Da Vinci Telemetry");
            if (existing != null)
                Undo.DestroyObjectImmediate(existing.gameObject);

            var pose = new Pose(
                consolePose.position + consolePose.rotation * new Vector3(0.52f, 0.06f, 0f),
                consolePose.rotation);

            var root = DaVinciPanelBuilder.CreateCanvas(container, pose, out var content);
            root.name = "Da Vinci Telemetry";
            root.AddComponent<FaceViewer>();

            DaVinciPanelBuilder.AddTitle(content, "ARM TELEMETRY");

            var joints = new List<(Transform joint, string label, Color colour)>();
            var legend = new List<(string, Color)>();
            for (var i = 0; i < k_GraphJoints.Length; i++)
            {
                var entry = k_GraphJoints[i];
                var joint = FindDescendant(model, entry.name);
                if (joint == null)
                {
                    notes.Add($"No joint named '{entry.name}', so '{entry.label}' has no trace on the graphs.");
                    continue;
                }

                joints.Add((joint, entry.label, entry.colour));
                legend.Add((entry.label, entry.colour == Color.clear ? DaVinciJointPalette.For(entry.name) : entry.colour));
            }

            DaVinciPanelBuilder.AddLegend(content, legend.ToArray());

            DaVinciPanelBuilder.AddHeading(content, "Speed vs time");
            var speedScale = DaVinciPanelBuilder.AddReadout(content, "Speed scale", "—");
            var speedChart = DaVinciPanelBuilder.AddChart(content, "Speed", 150f);
            WireGraph(root, DaVinciTelemetryGraph.Channel.Speed, joints, speedChart, speedScale);

            DaVinciPanelBuilder.AddHeading(content, "Distance vs time");
            var distanceScale = DaVinciPanelBuilder.AddReadout(content, "Distance scale", "—");
            var distanceChart = DaVinciPanelBuilder.AddChart(content, "Distance", 150f);
            WireGraph(root, DaVinciTelemetryGraph.Channel.Distance, joints, distanceChart, distanceScale);

            DaVinciPanelBuilder.AddSpacer(content, 6f);
            DaVinciPanelBuilder.AddReadout(content, "Pause hint", "<alpha=#AA>A button freezes the arms and the graphs");
        }

        static void WireGraph(
            GameObject host,
            DaVinciTelemetryGraph.Channel channel,
            List<(Transform joint, string label, Color colour)> joints,
            RawImage image,
            TMP_Text scaleLabel)
        {
            var graph = host.AddComponent<DaVinciTelemetryGraph>();

            var so = new SerializedObject(graph);
            so.FindProperty("m_Channel").enumValueIndex = (int)channel;
            so.FindProperty("m_Image").objectReferenceValue = image;
            so.FindProperty("m_ScaleLabel").objectReferenceValue = scaleLabel;
            so.FindProperty("m_BaseRange").floatValue = channel == DaVinciTelemetryGraph.Channel.Speed ? 0.25f : 1f;

            var series = so.FindProperty("m_Series");
            series.arraySize = joints.Count;
            for (var i = 0; i < joints.Count; i++)
            {
                var row = series.GetArrayElementAtIndex(i);
                row.FindPropertyRelative("label").stringValue = joints[i].label;
                row.FindPropertyRelative("joint").objectReferenceValue = joints[i].joint;

                // Clear defers to the shared palette, which is what keeps a trace and its joint's
                // wedge the same colour. The extra arms carry their own, since the palette cannot
                // tell them apart by prefix.
                row.FindPropertyRelative("colour").colorValue = joints[i].colour;
            }

            so.ApplyModifiedProperties();
        }

        /// <summary>
        /// Deletes control panels left anywhere other than on a panel object.
        /// </summary>
        /// <remarks>
        /// A second panel component is not harmless: its ball slots are empty, so whichever copy a
        /// button happens to be wired to may be the one that does nothing. They accumulate from
        /// earlier runs and from dropping the component on an object by hand while hunting for the
        /// reset buttons.
        /// </remarks>
        static void RemoveStrayPanels(Transform model, List<string> notes)
        {
            var panels = Object.FindObjectsByType<DaVinciControlPanel>(FindObjectsInactive.Include);

            for (var i = 0; i < panels.Length; i++)
            {
                if (panels[i] == null || panels[i].GetComponent<Canvas>() != null)
                    continue;

                notes.Add($"Removed a stray control panel from '{panels[i].name}' — it had no canvas and no wiring.");
                Undo.DestroyObjectImmediate(panels[i]);
            }
        }

        /// <summary>
        /// Builds the console panel: controls only.
        /// </summary>
        /// <remarks>
        /// Angles and speeds are deliberately <b>not</b> on the panel. They belong beside the joint
        /// they describe — a number reading "31.4°" on a wall tells you nothing about which of the
        /// twelve joints bent, whereas a wedge drawn on the joint itself needs no label to be
        /// understood. What is left here is the handful of things that have nowhere in the world to
        /// live: the buttons, and the switch for the overlays.
        /// </remarks>
        static void BuildPanel(
            Transform model,
            Transform container,
            Pose pose,
            List<DaVinciIkChainVisualizer> chains,
            List<JointAngleVisual> standalone,
            List<Transform> balls,
            LeverDrivenRotator rotator,
            List<string> notes)
        {
            var existing = container.Find("Da Vinci Control Panel");
            if (existing != null)
                Undo.DestroyObjectImmediate(existing.gameObject);

            var panelRoot = DaVinciPanelBuilder.CreateCanvas(container, pose, out var content);

            // The placement below is a guess at where the reader stands, and a guess that lands
            // backwards leaves every label mirrored. Turning to face the camera settles it.
            panelRoot.AddComponent<FaceViewer>();

            var panel = panelRoot.AddComponent<DaVinciControlPanel>();

            DaVinciPanelBuilder.AddTitle(content, "DA VINCI ROBOT");

            DaVinciPanelBuilder.AddHeading(content, "Instrument balls");
            var buttons = DaVinciPanelBuilder.AddButtonRow(content, "Reset Ball 1", "Reset Ball 2", "Reset Both");
            UnityEventTools.AddVoidPersistentListener(buttons[0].onClick, panel.ResetBall1);
            UnityEventTools.AddVoidPersistentListener(buttons[1].onClick, panel.ResetBall2);
            UnityEventTools.AddVoidPersistentListener(buttons[2].onClick, panel.ResetBothBalls);

            DaVinciPanelBuilder.AddHeading(content, "Angle overlay");
            var toggle = DaVinciPanelBuilder.AddToggle(content, "Show angles on the arms", true);
            UnityEventTools.AddBoolPersistentListener(toggle.onValueChanged, panel.SetAngleOverlayVisible, true);

            var zero = DaVinciPanelBuilder.AddButton(content, "Zero angles here");
            UnityEventTools.AddVoidPersistentListener(zero.onClick, panel.ZeroAngles);

            WirePanel(panel, balls, chains, standalone, rotator);
        }

        static void WirePanel(
            DaVinciControlPanel panel,
            List<Transform> balls,
            List<DaVinciIkChainVisualizer> chains,
            List<JointAngleVisual> overlays,
            LeverDrivenRotator rotator)
        {
            var so = new SerializedObject(panel);

            so.FindProperty("m_Ball1").objectReferenceValue = balls.Count > 0 ? balls[0] : null;
            so.FindProperty("m_Ball2").objectReferenceValue = balls.Count > 1 ? balls[1] : null;
            so.FindProperty("m_LeverRotator").objectReferenceValue = rotator;

            // Left empty on purpose: the readouts live in the world, not on the panel. The rows
            // still exist on the component for anyone who wants a number on the wall as well.
            so.FindProperty("m_Angles").arraySize = 0;
            so.FindProperty("m_Speeds").arraySize = 0;

            // Populated so the toggle can reach every overlay, even though nothing is printed here.
            FillObjectList(so.FindProperty("m_ChainOverlays"), chains);
            FillObjectList(so.FindProperty("m_Overlays"), overlays);

            so.ApplyModifiedProperties();
        }

        static void FillObjectList<T>(SerializedProperty list, List<T> values) where T : Object
        {
            list.arraySize = values.Count;
            for (var i = 0; i < values.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        /// <summary>
        /// Picks the local axis that pitches <paramref name="joint"/>'s arm up and down.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The FBX carries no joint metadata, so the hinge has to be inferred from geometry. Two
        /// things disqualify an axis. One that points up or down <b>yaws</b> the arm, sweeping it
        /// sideways across the table. One that lies along the arm's own length <b>rolls</b> it,
        /// spinning the instrument about itself and barely moving the hand at all. What is left —
        /// horizontal, and across the arm rather than along it — is the axis that raises and lowers
        /// the hand, so each candidate is scored on both and the best product wins.
        /// </para>
        /// <para>
        /// The joint's <i>rotation</i> is used rather than <c>TransformDirection</c>: the imported
        /// bones carry a negative scale, which mirrors a transformed direction and would flip the
        /// answer.
        /// </para>
        /// </remarks>
        static Vector3 ChoosePitchAxis(Transform joint)
        {
            var arm = ArmDirection(joint);
            var candidates = new[] { Vector3.right, Vector3.up, Vector3.forward };

            var best = Vector3.right;
            var bestScore = float.NegativeInfinity;

            for (var i = 0; i < candidates.Length; i++)
            {
                var world = (joint.rotation * candidates[i]).normalized;

                var horizontal = 1f - Mathf.Abs(Vector3.Dot(world, Vector3.up));
                var across = 1f - Mathf.Abs(Vector3.Dot(world, arm));

                var score = horizontal * across;
                if (score <= bestScore)
                    continue;

                bestScore = score;
                best = candidates[i];
            }

            return best;
        }

        /// <summary>
        /// The direction the arm below <paramref name="joint"/> runs in, taken from the joint to
        /// the deepest thing hanging off it.
        /// </summary>
        /// <remarks>
        /// The deepest descendant rather than the immediate child: the first child is often a short
        /// housing whose offset says nothing about which way the arm actually points.
        /// </remarks>
        static Vector3 ArmDirection(Transform joint)
        {
            var all = joint.GetComponentsInChildren<Transform>(true);
            var deepest = joint;
            var deepestDepth = 0;

            for (var i = 0; i < all.Length; i++)
            {
                var depth = 0;
                for (var walk = all[i]; walk != null && walk != joint; walk = walk.parent)
                    depth++;

                if (depth > deepestDepth)
                {
                    deepestDepth = depth;
                    deepest = all[i];
                }
            }

            var direction = deepest.position - joint.position;
            return direction.sqrMagnitude > 1e-8f ? direction.normalized : joint.forward;
        }

        static string AxisName(Vector3 axis)
        {
            if (axis == Vector3.right) return "+X";
            if (axis == Vector3.up) return "+Y";
            return "+Z";
        }

        /// <summary>
        /// Picks somewhere to put the panel: clear of the machine, at about chest height, facing
        /// away from it so a surgeon standing at the cart reads it face on.
        /// </summary>
        /// <remarks>
        /// A guess, and meant to be dragged. It is derived from the model's own bounds rather than
        /// hard-coded so it lands somewhere sensible whatever scale the model is imported at.
        /// </remarks>
        static Pose PanelPose(Transform model)
        {
            var bounds = new Bounds(model.position, Vector3.zero);
            var renderers = model.GetComponentsInChildren<Renderer>(false);
            for (var i = 0; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            var forward = Flatten(model.forward);
            var position = bounds.center + forward * (bounds.extents.magnitude * 0.5f + 0.3f);
            position.y = bounds.min.y + Mathf.Min(1.3f, bounds.size.y * 0.75f);

            return new Pose(position, Quaternion.LookRotation(forward, Vector3.up));
        }

        static Vector3 Flatten(Vector3 direction)
        {
            var flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            return flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.forward;
        }

        static Transform FindModelRoot()
        {
            var selected = Selection.activeTransform;
            if (selected != null)
            {
                var animator = selected.GetComponentInParent<Animator>();
                if (animator != null)
                    return animator.transform;
            }

            var byName = GameObject.Find(k_ModelRoot);
            return byName != null ? byName.transform : null;
        }

        /// <summary>
        /// Finds or creates the container everything this command adds lives under.
        /// </summary>
        /// <remarks>
        /// Kept at the scene root rather than under the model, because the model is imported at a
        /// uniform scale of about 0.25. A panel parented under it would inherit that, so a canvas
        /// authored at 420 units wide would come out a quarter of the intended size, and the
        /// overlay line widths with it.
        /// </remarks>
        static Transform GetOrCreateContainer(Transform model)
        {
            var roots = model.gameObject.scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == k_Container)
                    return roots[i].transform;
            }

            var host = new GameObject(k_Container);
            Undo.RegisterCreatedObjectUndo(host, "Create Da Vinci Console");
            host.transform.SetPositionAndRotation(model.position, Quaternion.identity);
            host.transform.localScale = Vector3.one;

            return host.transform;
        }

        static Transform GetOrCreateChild(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null)
                return existing;

            var host = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(host, "Create Da Vinci Object");
            Undo.SetTransformParent(host.transform, parent, "Create Da Vinci Object");
            host.transform.localPosition = Vector3.zero;
            host.transform.localRotation = Quaternion.identity;
            host.transform.localScale = Vector3.one;

            return host.transform;
        }

        static T GetOrAddComponent<T>(GameObject host) where T : Component
        {
            return host.TryGetComponent<T>(out var existing) ? existing : Undo.AddComponent<T>(host);
        }

        /// <summary>
        /// Finds a descendant by exact name. The imported names carry suffixes like ".003" that
        /// distinguish one arm from another, so a loose match would silently pick the wrong arm.
        /// </summary>
        static Transform FindDescendant(Transform root, string name)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i].name == name)
                    return all[i];
            }

            return null;
        }

        static void Report(string message) => EditorUtility.DisplayDialog(k_Title, message, "OK");
    }
}
