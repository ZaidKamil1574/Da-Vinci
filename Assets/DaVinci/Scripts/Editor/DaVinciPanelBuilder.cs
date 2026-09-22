using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace XRMultiplayer.DaVinci.Editor
{
    /// <summary>
    /// Builds the world-space console panel for the Da Vinci robot out of plain uGUI parts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built in the editor rather than at runtime so the result is an ordinary set of objects the
    /// user can select, restyle and re-lay-out by hand. A panel assembled in <c>Awake</c> would be
    /// invisible until play mode and impossible to nudge.
    /// </para>
    /// <para>
    /// The canvas carries a <see cref="TrackedDeviceGraphicRaycaster"/> rather than the stock
    /// <see cref="GraphicRaycaster"/>. The stock one raycasts from a screen-space pointer, which no
    /// one in a headset has; the tracked-device version is what lets a ray or a poking fingertip
    /// hit a button.
    /// </para>
    /// <para>
    /// Everything is sized in pixels and then scaled down by <see cref="k_MetresPerPixel"/>. Laying
    /// a world-space canvas out in metres directly means font sizes below one unit, where TMP's
    /// auto-sizing and layout rounding start to misbehave.
    /// </para>
    /// </remarks>
    public static class DaVinciPanelBuilder
    {
        /// <summary>Canvas units per metre. 1000 px across a 0.4 m panel is a comfortable density.</summary>
        const float k_MetresPerPixel = 0.001f;

        /// <summary>Panel width in canvas pixels.</summary>
        const float k_Width = 420f;

        static readonly Color k_Background = new Color(0.06f, 0.08f, 0.11f, 0.94f);
        static readonly Color k_Heading = new Color(0.45f, 0.78f, 1f);
        static readonly Color k_Body = new Color(0.88f, 0.92f, 0.96f);
        static readonly Color k_ButtonFace = new Color(0.16f, 0.36f, 0.52f);

        /// <summary>
        /// Creates the panel canvas, laid out as a single vertical column.
        /// </summary>
        /// <param name="parent">Object to parent the panel to, or null for the scene root.</param>
        /// <param name="pose">Where to put the panel, and which way it faces.</param>
        /// <param name="content">The column new rows should be added to.</param>
        /// <returns>The panel's root object.</returns>
        public static GameObject CreateCanvas(Transform parent, Pose pose, out RectTransform content)
        {
            var root = new GameObject("Da Vinci Control Panel", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(root, "Create Da Vinci Panel");

            if (parent != null)
                Undo.SetTransformParent(root.transform, parent, "Create Da Vinci Panel");

            root.transform.SetPositionAndRotation(pose.position, pose.rotation);
            root.transform.localScale = Vector3.one * k_MetresPerPixel;

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3f;

            root.AddComponent<TrackedDeviceGraphicRaycaster>();

            var rect = (RectTransform)root.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(k_Width, 480f);

            // Rows are added straight to the canvas rather than to a nested column. A column child
            // would need its own size driven from the parent while the parent's size is driven from
            // the column, which is the circular dependency the layout system warns about.
            var layout = root.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 16, 16);
            layout.spacing = 6f;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;

            // The panel grows to fit whatever rows are added, so the caller never has to work out a
            // height in advance.
            var fitter = root.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var background = new GameObject("Background", typeof(RectTransform)).GetComponent<RectTransform>();
            background.SetParent(rect, false);
            Stretch(background);

            var image = background.gameObject.AddComponent<Image>();
            image.sprite = BuiltinSprite("UI/Skin/Background.psd");
            image.type = Image.Type.Sliced;
            image.color = k_Background;

            // Kept out of the column: the backing plate has to span the panel, not take a turn in it.
            background.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            content = rect;
            return root;
        }

        /// <summary>Adds the panel's main title.</summary>
        public static TMP_Text AddTitle(RectTransform content, string text)
        {
            var label = AddText(content, "Title", text, 26f, FontStyles.Bold, Color.white);
            label.alignment = TextAlignmentOptions.Center;
            return label;
        }

        /// <summary>Adds a section heading with a rule under it.</summary>
        public static TMP_Text AddHeading(RectTransform content, string text)
        {
            AddSpacer(content, 6f);
            var label = AddText(content, $"Heading ({text})", text.ToUpperInvariant(), 15f, FontStyles.Bold, k_Heading);
            label.characterSpacing = 6f;
            return label;
        }

        /// <summary>Adds a readout line, returned so the panel can write into it.</summary>
        public static TMP_Text AddReadout(RectTransform content, string name, string placeholder)
        {
            return AddText(content, $"Readout ({name})", placeholder, 17f, FontStyles.Normal, k_Body);
        }

        /// <summary>Adds a row of equally sized buttons.</summary>
        public static Button[] AddButtonRow(RectTransform content, params string[] labels)
        {
            var row = new GameObject("Button Row", typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(content, false);

            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childForceExpandWidth = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            SetPreferredHeight(row, 52f);

            var buttons = new Button[labels.Length];
            for (var i = 0; i < labels.Length; i++)
                buttons[i] = CreateButton(row, labels[i]);

            return buttons;
        }

        /// <summary>Adds a single full-width button.</summary>
        public static Button AddButton(RectTransform content, string label)
        {
            var button = CreateButton(content, label);
            SetPreferredHeight((RectTransform)button.transform, 48f);
            return button;
        }

        /// <summary>Adds a labelled checkbox.</summary>
        public static Toggle AddToggle(RectTransform content, string label, bool value)
        {
            var row = new GameObject($"Toggle ({label})", typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(content, false);
            SetPreferredHeight(row, 40f);

            var toggle = row.gameObject.AddComponent<Toggle>();

            var box = new GameObject("Box", typeof(RectTransform)).GetComponent<RectTransform>();
            box.SetParent(row, false);
            box.anchorMin = new Vector2(0f, 0.5f);
            box.anchorMax = new Vector2(0f, 0.5f);
            box.pivot = new Vector2(0f, 0.5f);
            box.anchoredPosition = Vector2.zero;
            box.sizeDelta = new Vector2(28f, 28f);

            var boxImage = box.gameObject.AddComponent<Image>();
            boxImage.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
            boxImage.type = Image.Type.Sliced;
            boxImage.color = new Color(0.2f, 0.24f, 0.3f);

            var tick = new GameObject("Tick", typeof(RectTransform)).GetComponent<RectTransform>();
            tick.SetParent(box, false);
            Stretch(tick, 5f);
            var tickImage = tick.gameObject.AddComponent<Image>();
            tickImage.sprite = BuiltinSprite("UI/Skin/Checkmark.psd");
            tickImage.color = k_Heading;

            var text = AddText(row, "Label", label, 17f, FontStyles.Normal, k_Body);
            var textRect = text.rectTransform;
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(40f, 0f);
            textRect.offsetMax = Vector2.zero;
            text.alignment = TextAlignmentOptions.Left;

            toggle.targetGraphic = boxImage;
            toggle.graphic = tickImage;
            toggle.isOn = value;

            return toggle;
        }

        /// <summary>Adds a fixed vertical gap.</summary>
        public static void AddSpacer(RectTransform content, float height)
        {
            var spacer = new GameObject("Spacer", typeof(RectTransform)).GetComponent<RectTransform>();
            spacer.SetParent(content, false);
            SetPreferredHeight(spacer, height);
        }

        /// <summary>
        /// Adds a chart area and returns the image the graph rasterises into.
        /// </summary>
        public static RawImage AddChart(RectTransform content, string name, float height)
        {
            var host = new GameObject($"Chart ({name})", typeof(RectTransform)).GetComponent<RectTransform>();
            host.SetParent(content, false);
            SetPreferredHeight(host, height);

            var image = host.gameObject.AddComponent<RawImage>();

            // The chart texture carries its own background, so no sprite is needed underneath and
            // the raw image can draw it at one pixel per texel.
            image.color = Color.white;

            return image;
        }

        /// <summary>
        /// Adds a row of colour swatches naming what each trace on a chart belongs to.
        /// </summary>
        /// <remarks>
        /// Swatches rather than coloured text: a thin glyph in a saturated hue is hard to match
        /// against a one-pixel trace, while a solid block of the same colour is unmistakable.
        /// </remarks>
        public static void AddLegend(RectTransform content, (string label, Color colour)[] entries)
        {
            var row = new GameObject("Legend", typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(content, false);
            SetPreferredHeight(row, 26f);

            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 14f;
            layout.childForceExpandWidth = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = new GameObject($"Key ({entries[i].label})", typeof(RectTransform)).GetComponent<RectTransform>();
                entry.SetParent(row, false);

                var entryLayout = entry.gameObject.AddComponent<HorizontalLayoutGroup>();
                entryLayout.spacing = 5f;
                entryLayout.childForceExpandWidth = false;
                entryLayout.childControlWidth = true;
                entryLayout.childControlHeight = true;
                entryLayout.childAlignment = TextAnchor.MiddleLeft;

                var swatch = new GameObject("Swatch", typeof(RectTransform)).GetComponent<RectTransform>();
                swatch.SetParent(entry, false);
                var swatchImage = swatch.gameObject.AddComponent<Image>();
                swatchImage.color = entries[i].colour;

                var swatchElement = swatch.gameObject.AddComponent<LayoutElement>();
                swatchElement.preferredWidth = 16f;
                swatchElement.minWidth = 16f;
                swatchElement.preferredHeight = 16f;

                var text = AddText(entry, "Label", entries[i].label, 14f, FontStyles.Normal, k_Body);
                text.gameObject.GetComponent<LayoutElement>().preferredWidth = entries[i].label.Length * 8f;
            }
        }

        static Button CreateButton(RectTransform parent, string label)
        {
            var host = new GameObject($"Button ({label})", typeof(RectTransform)).GetComponent<RectTransform>();
            host.SetParent(parent, false);

            var image = host.gameObject.AddComponent<Image>();
            image.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = k_ButtonFace;

            var button = host.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            var text = AddText(host, "Label", label, 17f, FontStyles.Bold, Color.white);
            Stretch(text.rectTransform, 4f);
            text.alignment = TextAlignmentOptions.Center;

            return button;
        }

        static TMP_Text AddText(RectTransform parent, string name, string content, float size, FontStyles style, Color colour)
        {
            var host = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            host.SetParent(parent, false);

            var text = host.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = colour;
            text.alignment = TextAlignmentOptions.Left;

            if (TMP_Settings.defaultFontAsset != null)
                text.font = TMP_Settings.defaultFontAsset;

            SetPreferredHeight(host, size * 1.5f);

            return text;
        }

        static void SetPreferredHeight(RectTransform rect, float height)
        {
            var element = rect.gameObject.GetComponent<LayoutElement>();
            if (element == null)
                element = rect.gameObject.AddComponent<LayoutElement>();

            element.preferredHeight = height;
            element.minHeight = height;
        }

        static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        /// <summary>
        /// Fetches one of the editor's built-in UI sprites.
        /// </summary>
        /// <remarks>
        /// These ship with the editor rather than with the project, so nothing has to be imported
        /// to make the panel look like a panel. They are editor-only resources, which is why this
        /// whole builder lives under an Editor folder — a runtime equivalent would need real assets.
        /// </remarks>
        static Sprite BuiltinSprite(string path) => AssetDatabase.GetBuiltinExtraResource<Sprite>(path);
    }
}
