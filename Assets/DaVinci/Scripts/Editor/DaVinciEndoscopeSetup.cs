using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace XRMultiplayer.DaVinci.Editor
{
    /// <summary>
    /// Mounts a camera on the instrument tip and builds the console monitor that shows its feed,
    /// mirroring the endoscopic view a surgeon works from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The camera is parented to the tip so it inherits the arm's IK motion directly, and the
    /// monitor is a world-space canvas placed in front of the surgeon console. Both ends share one
    /// <see cref="RenderTexture"/> asset, so the link survives play mode, domain reloads and scene
    /// saves without anything having to reconnect them at runtime.
    /// </para>
    /// <para>
    /// The monitor is put on the UI layer and that layer is removed from the camera's culling mask.
    /// Without that the endoscope sees the screen showing its own feed and the image recurses into
    /// a tunnel.
    /// </para>
    /// </remarks>
    public static class DaVinciEndoscopeSetup
    {
        const string k_Title = "Da Vinci Endoscope Setup";
        const string k_Container = "Da Vinci Endoscope";
        const string k_ConsoleName = "Robotic Surgery Controller";
        const string k_GeneratedFolder = "Assets/DaVinci/Generated";
        const string k_FeedPath = k_GeneratedFolder + "/EndoscopeFeed.renderTexture";

        /// <summary>Canvas units per metre, matching <c>DaVinciPanelBuilder</c>.</summary>
        const float k_MetresPerPixel = 0.001f;

        const float k_PanelWidth = 660f;
        const float k_PanelHeight = 430f;
        const float k_ScreenWidth = 608f;
        const float k_ScreenHeight = 342f;

        /// <summary>
        /// Instrument tips in preference order. The chain rooted at HAND_BEGIN ends at the forceps,
        /// which is where an endoscope would actually sit.
        /// </summary>
        static readonly string[] k_TipCandidates = { "Tip 2", "HAND_4", "Cylinder.004" };

        static readonly Color k_Bezel = new Color(0.05f, 0.06f, 0.08f, 0.97f);
        static readonly Color k_Heading = new Color(0.45f, 0.78f, 1f);

        [MenuItem("Tools/Da Vinci/Set Up Endoscope View", false, 11)]
        [MenuItem("GameObject/Da Vinci/Set Up Endoscope View", false, 14)]
        public static void Setup()
        {
            var notes = new List<string>();

            var tip = ResolveTip();
            if (tip == null)
            {
                EditorUtility.DisplayDialog(k_Title,
                    "Could not find an instrument tip. Looked for: " + string.Join(", ", k_TipCandidates) +
                    ".\n\nSelect the tip transform and run this again.", "OK");
                return;
            }

            var feed = LoadOrCreateFeed();
            var container = GetOrCreateContainer(tip.gameObject.scene);
            var uiLayer = LayerMask.NameToLayer("UI");

            var camera = BuildCamera(tip, feed, uiLayer, notes);
            BuildMonitor(container, feed, uiLayer, notes);

            EditorSceneManager.MarkSceneDirty(tip.gameObject.scene);
            AssetDatabase.SaveAssets();

            Debug.Log($"[{k_Title}] Done.\n  " + string.Join("\n  ", notes), camera);
        }

        static Transform ResolveTip()
        {
            // An explicit selection wins, so a different arm can be chosen without editing code.
            if (Selection.activeTransform != null)
                return Selection.activeTransform;

            foreach (var name in k_TipCandidates)
            {
                var found = FindInScene(name);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>
        /// Finds by name including inactive objects, which <see cref="GameObject.Find"/> skips.
        /// </summary>
        static Transform FindInScene(string name)
        {
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
                {
                    if (candidate.name == name)
                        return candidate;
                }
            }

            return null;
        }

        static RenderTexture LoadOrCreateFeed()
        {
            var existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(k_FeedPath);
            if (existing != null)
                return existing;

            if (!AssetDatabase.IsValidFolder(k_GeneratedFolder))
                AssetDatabase.CreateFolder("Assets/DaVinci", "Generated");

            // 16:9 at a modest size. This is a third full render on top of both eyes, so the
            // resolution is deliberately below the monitor's pixel size rather than above it.
            var feed = new RenderTexture(1024, 576, 24, RenderTextureFormat.Default)
            {
                name = "EndoscopeFeed",
                antiAliasing = 1,
                useMipMap = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            AssetDatabase.CreateAsset(feed, k_FeedPath);
            return feed;
        }

        static Camera BuildCamera(Transform tip, RenderTexture feed, int uiLayer, List<string> notes)
        {
            var existing = tip.Find("Endoscope Camera");
            GameObject go;

            if (existing != null)
            {
                go = existing.gameObject;
                notes.Add("Reused the existing Endoscope Camera.");
            }
            else
            {
                go = new GameObject("Endoscope Camera", typeof(Camera), typeof(EndoscopeCamera));
                Undo.RegisterCreatedObjectUndo(go, "Set Up Endoscope");
                Undo.SetTransformParent(go.transform, tip, "Set Up Endoscope");
                notes.Add($"Mounted an Endoscope Camera on \"{tip.name}\".");
            }

            // Aim down the instrument shaft rather than along the tip's own axes, whose orientation
            // is whatever the model was exported with. The shaft direction is a property of the
            // geometry, so it points where the instrument actually looks.
            var shaft = tip.parent != null ? tip.position - tip.parent.position : tip.forward;
            if (shaft.sqrMagnitude > 1e-8f)
            {
                shaft = shaft.normalized;
                go.transform.SetPositionAndRotation(
                    tip.position - shaft * 0.02f,
                    Quaternion.LookRotation(shaft, Vector3.up));
            }
            else
            {
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
            }

            // The tip sits inside a model imported at a fraction of a unit; a camera does not care
            // about scale for projection, but leaving it uneven makes gizmos misleading.
            go.transform.localScale = Vector3.one;

            var camera = go.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.depth = -10f;

            var endoscope = go.GetComponent<EndoscopeCamera>();
            if (endoscope == null)
                endoscope = Undo.AddComponent<EndoscopeCamera>(go);

            endoscope.SetFeed(feed);

            var visible = ~0;
            if (uiLayer >= 0)
            {
                visible &= ~(1 << uiLayer);
                notes.Add("Excluded the UI layer from the endoscope's culling mask, so the monitor cannot appear in its own feed.");
            }

            var mirrorLayer = LayerMask.NameToLayer("Mirror");
            if (mirrorLayer >= 0)
                visible &= ~(1 << mirrorLayer);

            var serialized = new SerializedObject(endoscope);
            serialized.FindProperty("m_VisibleLayers").intValue = visible;
            serialized.ApplyModifiedProperties();
            endoscope.Apply();

            EditorUtility.SetDirty(go);
            return camera;
        }

        static void BuildMonitor(Transform container, RenderTexture feed, int uiLayer, List<string> notes)
        {
            var existing = container.Find("Endoscope Monitor");
            if (existing != null)
            {
                var raw = existing.GetComponentInChildren<RawImage>(true);
                if (raw != null)
                {
                    Undo.RecordObject(raw, "Set Up Endoscope");
                    raw.texture = feed;
                }

                notes.Add("Monitor already present; refreshed its feed and left its placement alone.");
                return;
            }

            var console = FindInScene(k_ConsoleName);
            var pose = console != null
                ? InFrontOf(console)
                : new Pose(container.position + Vector3.up * 1.3f, Quaternion.identity);

            if (console == null)
                notes.Add($"WARNING: no \"{k_ConsoleName}\" in the scene, so the monitor was parked at the container instead.");

            var root = new GameObject("Endoscope Monitor", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Undo.RegisterCreatedObjectUndo(root, "Set Up Endoscope");
            Undo.SetTransformParent(root.transform, container, "Set Up Endoscope");

            root.transform.SetPositionAndRotation(pose.position, pose.rotation);
            root.transform.localScale = Vector3.one * k_MetresPerPixel;

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            root.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;

            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(k_PanelWidth, k_PanelHeight);

            var bezel = root.AddComponent<Image>();
            bezel.color = k_Bezel;

            AddLabel(rect, "Title", "ENDOSCOPE — ARM A", new Vector2(0f, k_PanelHeight * 0.5f - 32f),
                new Vector2(k_PanelWidth - 40f, 40f), 26f, k_Heading);

            var screen = new GameObject("Screen", typeof(RectTransform));
            screen.transform.SetParent(rect, false);

            var screenRect = screen.GetComponent<RectTransform>();
            screenRect.sizeDelta = new Vector2(k_ScreenWidth, k_ScreenHeight);
            screenRect.anchoredPosition = new Vector2(0f, -18f);

            var image = screen.AddComponent<RawImage>();
            image.texture = feed;

            if (uiLayer >= 0)
                SetLayerRecursively(root, uiLayer);

            notes.Add(console != null
                ? $"Built the monitor in front of \"{k_ConsoleName}\"."
                : "Built the monitor.");
        }

        static void AddLabel(RectTransform parent, string name, string text, Vector2 position, Vector2 size, float fontSize, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = colour;
        }

        /// <summary>
        /// A pose standing off the front face of <paramref name="model"/> at about seated eye
        /// height, matching how the console panel is placed.
        /// </summary>
        static Pose InFrontOf(Transform model)
        {
            var bounds = new Bounds(model.position, Vector3.zero);
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(false))
                bounds.Encapsulate(renderer.bounds);

            var forward = Flatten(model.forward);
            var position = bounds.center + forward * (bounds.extents.magnitude * 0.5f + 0.25f);
            position.y = bounds.min.y + Mathf.Min(1.35f, bounds.size.y * 0.9f);

            return new Pose(position, Quaternion.LookRotation(forward, Vector3.up));
        }

        static Vector3 Flatten(Vector3 direction)
        {
            var flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            return flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.forward;
        }

        static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        static Transform GetOrCreateContainer(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == k_Container)
                    return root.transform;
            }

            var created = new GameObject(k_Container);
            Undo.RegisterCreatedObjectUndo(created, "Set Up Endoscope");
            return created.transform;
        }
    }
}
