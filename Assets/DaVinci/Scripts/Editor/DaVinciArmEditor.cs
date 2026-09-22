using UnityEditor;
using UnityEngine;

namespace XRMultiplayer.DaVinci.Editor
{
    /// <summary>
    /// Inspector for <see cref="DaVinciArm"/> with a live calibration section.
    /// </summary>
    /// <remarks>
    /// The imported model carries no joint metadata, so every axis has to be established by eye.
    /// Dragging a joint's slider rotates it in the scene immediately, which turns "which way does
    /// this hinge swing" into a two-second check per joint instead of a guess.
    /// </remarks>
    [CustomEditor(typeof(DaVinciArm))]
    public class DaVinciArmEditor : UnityEditor.Editor
    {
        static readonly GUIContent k_AxisX = new GUIContent("X", "Rotate about the joint's local X axis.");
        static readonly GUIContent k_AxisY = new GUIContent("Y", "Rotate about the joint's local Y axis.");
        static readonly GUIContent k_AxisZ = new GUIContent("Z", "Rotate about the joint's local Z axis.");

        bool m_ShowCalibration = true;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var arm = (DaVinciArm)target;

            EditorGUILayout.Space();
            m_ShowCalibration = EditorGUILayout.BeginFoldoutHeaderGroup(m_ShowCalibration, "Joint Calibration");

            if (m_ShowCalibration)
            {
                EditorGUILayout.HelpBox(
                    "Drag a slider to swing that joint in the scene. If it twists the wrong way, " +
                    "switch its axis with X / Y / Z until the motion matches the real hinge, then " +
                    "set the limits to the range the mechanism actually allows.",
                    MessageType.Info);

                DrawPoseButtons(arm);
                EditorGUILayout.Space();
                DrawJointSliders(arm);
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void DrawPoseButtons(DaVinciArm arm)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent(
                        "Capture Rest Pose",
                        "Record the current pose as every joint's zero angle. Use this only when the arm is at its mechanical zero.")))
                {
                    RecordJointTransforms(arm, "Capture Da Vinci Rest Pose");
                    arm.CaptureRestPose();
                    EditorUtility.SetDirty(arm);
                }

                if (GUILayout.Button(new GUIContent("Reset To Rest", "Return every joint to its captured zero angle.")))
                {
                    RecordJointTransforms(arm, "Reset Da Vinci Arm");
                    arm.ResetToRestPose();
                }
            }
        }

        void DrawJointSliders(DaVinciArm arm)
        {
            var jointsProperty = serializedObject.FindProperty("m_Joints");
            var remoteCentreIndex = serializedObject.FindProperty("m_RemoteCentreJointIndex").intValue;

            for (var i = 0; i < arm.joints.Count; i++)
            {
                var joint = arm.joints[i];
                if (joint.joint == null)
                    continue;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var section = i < remoteCentreIndex ? "setup" : "instrument";
                    EditorGUILayout.LabelField($"{i}  {joint.joint.name}", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(
                        joint.restCaptured ? section : $"{section}  —  no rest pose captured",
                        EditorStyles.miniLabel);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUI.BeginChangeCheck();
                        var angle = EditorGUILayout.Slider(joint.angle, joint.minAngle, joint.maxAngle);
                        if (EditorGUI.EndChangeCheck() && joint.restCaptured)
                        {
                            Undo.RecordObject(joint.joint, "Pose Da Vinci Joint");
                            joint.angle = angle;
                        }

                        DrawAxisButtons(jointsProperty, i);
                    }
                }
            }
        }

        void DrawAxisButtons(SerializedProperty jointsProperty, int index)
        {
            if (index >= jointsProperty.arraySize)
                return;

            var axisProperty = jointsProperty
                .GetArrayElementAtIndex(index)
                .FindPropertyRelative("m_LocalAxis");

            var current = axisProperty.vector3Value;

            DrawAxisButton(axisProperty, current, Vector3.right, k_AxisX);
            DrawAxisButton(axisProperty, current, Vector3.up, k_AxisY);
            DrawAxisButton(axisProperty, current, Vector3.forward, k_AxisZ);
        }

        void DrawAxisButton(SerializedProperty axisProperty, Vector3 current, Vector3 axis, GUIContent label)
        {
            var wasSelected = Vector3.Dot(current.normalized, axis) > 0.99f;
            var isSelected = GUILayout.Toggle(wasSelected, label, EditorStyles.miniButton, GUILayout.Width(24f));

            if (isSelected == wasSelected)
                return;

            axisProperty.vector3Value = isSelected ? axis : current;
            serializedObject.ApplyModifiedProperties();
        }

        static void RecordJointTransforms(DaVinciArm arm, string undoName)
        {
            for (var i = 0; i < arm.joints.Count; i++)
            {
                if (arm.joints[i].joint != null)
                    Undo.RecordObject(arm.joints[i].joint, undoName);
            }
        }
    }
}
