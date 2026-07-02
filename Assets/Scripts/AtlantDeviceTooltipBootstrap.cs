using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class AtlantDeviceTooltipBootstrap
{
    public static AtlantDeviceTooltipUI GetOrCreate()
    {
        AtlantDeviceTooltipUI existing = Object.FindFirstObjectByType<AtlantDeviceTooltipUI>();
        if (existing != null) return existing;

        Canvas canvas = CreateCanvas();
        RectTransform fullScreenBackground = CreateFullScreenBackground(canvas.transform);
        RectTransform panel = CreateCenterPanel(fullScreenBackground);
        RectTransform contentContainer = CreateContentContainer(panel);

        TextMeshProUGUI title = CreateText(contentContainer, "Title", 22, FontStyles.Bold, new Color(0.95f, 0.98f, 1f));
        TextMeshProUGUI status = CreateText(contentContainer, "Status", 16, FontStyles.Normal, new Color(0.75f, 0.82f, 0.9f));

        CreateSeparatorLine(contentContainer);

        GameObject actionsContainerGO = new GameObject("Actions UI Container");
        actionsContainerGO.transform.SetParent(contentContainer, false);
        RectTransform actionsContainer = actionsContainerGO.AddComponent<RectTransform>();
        VerticalLayoutGroup actionsLayout = actionsContainerGO.AddComponent<VerticalLayoutGroup>();
        actionsLayout.spacing = 10;
        actionsLayout.childControlHeight = true;
        actionsLayout.childControlWidth = true;
        actionsLayout.childForceExpandHeight = false;
        actionsLayout.childForceExpandWidth = true;

        CreateSeparatorLine(contentContainer);
        TextMeshProUGUI closeHint = CreateText(contentContainer, "CloseHint", 13, FontStyles.Italic, new Color(0.5f, 0.55f, 0.6f));
        closeHint.text = "Нажмите [E] или кликните по фону, чтобы вернуться назад";
        closeHint.alignment = TextAlignmentOptions.Center;

        AtlantDeviceTooltipUI tooltip = fullScreenBackground.gameObject.AddComponent<AtlantDeviceTooltipUI>();
        tooltip.Initialize(canvas, panel, title, status, actionsContainer);

        return tooltip;
    }

    private static Canvas CreateCanvas()
    {
        GameObject canvasObject = new GameObject("Atlant Device FullScreen UI Canvas");
        Object.DontDestroyOnLoad(canvasObject);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        canvas.pixelPerfect = true;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObject.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private static RectTransform CreateFullScreenBackground(Transform parent)
    {
        GameObject bgObj = new GameObject("FullScreen Background Dimmer");
        bgObj.transform.SetParent(parent, false);

        RectTransform rect = bgObj.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;

        Image img = bgObj.AddComponent<Image>();
        img.color = new Color(0.01f, 0.02f, 0.04f, 0.75f);
        img.raycastTarget = true;

        return rect;
    }

    private static RectTransform CreateCenterPanel(Transform parent)
    {
        GameObject panelObject = new GameObject("Device Control Panel");
        panelObject.transform.SetParent(parent, false);

        RectTransform rect = panelObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(480f, 550f);

        Image image = panelObject.AddComponent<Image>();
        image.color = new Color(0.04f, 0.08f, 0.14f, 0.95f);
        image.raycastTarget = true;

        ContentSizeFitter fitter = panelObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        VerticalLayoutGroup layout = panelObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 24, 24);
        layout.spacing = 16;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        return rect;
    }

    private static RectTransform CreateContentContainer(RectTransform parent)
    {
        GameObject container = new GameObject("Content Container");
        container.transform.SetParent(parent, false);

        RectTransform rect = container.AddComponent<RectTransform>();
        VerticalLayoutGroup layout = container.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 14;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        return rect;
    }

    private static TextMeshProUGUI CreateText(RectTransform parent, string name, int fontSize, FontStyles style, Color color)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        textObject.AddComponent<RectTransform>();

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.alignment = TextAlignmentOptions.Left;
        text.raycastTarget = false;

        return text;
    }

    private static void CreateSeparatorLine(RectTransform parent)
    {
        GameObject lineObject = new GameObject("Separator Line");
        lineObject.transform.SetParent(parent, false);

        RectTransform rect = lineObject.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(0f, 1f);

        Image image = lineObject.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.08f);
        image.raycastTarget = false;
    }
}