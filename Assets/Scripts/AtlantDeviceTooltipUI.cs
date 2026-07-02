using System.Collections.Generic;
using System.Text;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public class AtlantDeviceTooltipUI : MonoBehaviour
{
    [Header("UI Ссылки")]
    [SerializeField] private Canvas canvas;
    [SerializeField] private RectTransform panel;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private RectTransform actionsContainer;

    private AtlantMqttDeviceBase targetDevice;
    private CanvasGroup canvasGroup;
    private Coroutine animationCoroutine;
    private List<GameObject> generatedUIElements = new List<GameObject>();
    private List<MonoBehaviour> disabledCameraScripts = new List<MonoBehaviour>();

    public bool IsOpen => targetDevice != null;

    public void Initialize(Canvas ownerCanvas, RectTransform tooltipPanel, TextMeshProUGUI title, TextMeshProUGUI status, RectTransform container)
    {
        canvas = ownerCanvas;
        panel = tooltipPanel;
        titleText = title;
        statusText = status;
        actionsContainer = container;

        if (TryGetComponent<Button>(out var bgBtn)) Destroy(bgBtn);
        gameObject.AddComponent<Button>().onClick.AddListener(Hide);

        EnsureReferences();
        HideImmediately();
    }

    private void Awake()
    {
        EnsureReferences();
        HideImmediately();
    }

    private void Update()
    {
        if (targetDevice == null) return;

        RefreshStatusOnly(targetDevice);

        if (Input.GetKeyDown(KeyCode.E))
        {
            Hide();
        }
    }

    public void Show(AtlantMqttDeviceBase device)
    {
        if (statusText != null) statusText.text = "";
        if (titleText != null) titleText.text = "";
        ClearGeneratedUI();

        targetDevice = device;
        EnsureReferences();
        gameObject.SetActive(true);

        // [ИСПРАВЛЕНО] Больше НЕ сбрасываем состояние принудительно! 
        // Если прибор уже получил свой код — пускай хранит его и показывает при повторном открытии.
        if (device.IsMqttConnected && !device.IsAlreadyPaired)
        {
            device.TriggerAutoPairingRequest();
        }

        RefreshStatusOnly(device);
        GenerateDeviceControlUI(device);

        FreezePlayerCamera(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Input.ResetInputAxes();
        EventSystem.current?.SetSelectedGameObject(null);

        if (animationCoroutine != null) StopCoroutine(animationCoroutine);
        animationCoroutine = StartCoroutine(AnimateTooltip(1f, Vector3.one, 0.2f, false));
    }

    public void Hide()
    {
        if (targetDevice == null) return;

        targetDevice = null;
        FreezePlayerCamera(false);
        Input.ResetInputAxes();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (animationCoroutine != null) StopCoroutine(animationCoroutine);
        animationCoroutine = StartCoroutine(AnimateTooltip(0f, Vector3.one * 0.95f, 0.15f, true));
    }

    private void HideImmediately()
    {
        if (animationCoroutine != null)
        {
            StopCoroutine(animationCoroutine);
            animationCoroutine = null;
        }

        targetDevice = null;
        FreezePlayerCamera(false);
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (panel != null) panel.localScale = Vector3.one * 0.95f;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        Input.ResetInputAxes();

        if (statusText != null) statusText.text = "";
        gameObject.SetActive(false);
        ClearGeneratedUI();
    }

    private void RefreshStatusOnly(AtlantMqttDeviceBase device)
    {
        if (device == null || targetDevice != device) return;
        if (titleText != null) titleText.text = TranslateDeviceName(device.DisplayName);
        if (statusText != null) statusText.text = BuildStatusText(device);
    }

    private void GenerateDeviceControlUI(AtlantMqttDeviceBase device)
    {
        ClearGeneratedUI();
        var hints = device.GetInteractionHints();

        foreach (var hint in hints)
        {
            if (hint.Key == KeyCode.Alpha0) continue;

            string russianActionLabel = TranslateActionLabel(hint.Action);

            if (hint.Adjustable)
            {
                GameObject row = new GameObject("AdjustableRow");
                row.transform.SetParent(actionsContainer, false);
                generatedUIElements.Add(row);

                HorizontalLayoutGroup hLayout = row.AddComponent<HorizontalLayoutGroup>();
                hLayout.spacing = 8;
                hLayout.childControlWidth = true;
                hLayout.childForceExpandWidth = true;

                CreateUIButton(row.transform, russianActionLabel, null, true);

                KeyCode currentKey = hint.Key;

                CreateUIButton(row.transform, "  <  ", () => {
                    device.BeginInteractionAdjustment(currentKey);
                    device.AdjustInteractionValue(currentKey, -1f);
                    device.CommitInteractionAdjustment(currentKey);
                    RefreshStatusOnly(device);
                }, false);

                CreateUIButton(row.transform, "  >  ", () => {
                    device.BeginInteractionAdjustment(currentKey);
                    device.AdjustInteractionValue(currentKey, 1f);
                    device.CommitInteractionAdjustment(currentKey);
                    RefreshStatusOnly(device);
                }, false);
            }
            else
            {
                KeyCode key = hint.Key;
                string buttonLabel = russianActionLabel;
                string primaryActionLabel = device.GetPrimaryActionLabel(key);
                if (!string.IsNullOrWhiteSpace(primaryActionLabel))
                {
                    buttonLabel = primaryActionLabel;
                }

                CreateUIButton(actionsContainer, buttonLabel, () => {
                    device.HandleInteractionKey(key);
                }, false);
            }
        }
    }

    private void CreateUIButton(Transform parent, string label, System.Action onClickAction, bool isLabelOnly)
    {
        GameObject btnObj = new GameObject(isLabelOnly ? "UI_Label" : "UI_Button");
        btnObj.transform.SetParent(parent, false);
        generatedUIElements.Add(btnObj);

        Image img = btnObj.AddComponent<Image>();
        img.color = isLabelOnly ? new Color(0.06f, 0.11f, 0.18f, 0.6f) : new Color(0.0f, 0.45f, 0.85f, 1f);

        if (onClickAction != null && !isLabelOnly)
        {
            Button btn = btnObj.AddComponent<Button>();
            btn.onClick.AddListener(() => onClickAction.Invoke());

            ColorBlock cb = btn.colors;
            cb.normalColor = new Color(0.0f, 0.45f, 0.85f, 1f);
            cb.highlightedColor = new Color(0.0f, 0.55f, 1f, 1f);
            cb.pressedColor = new Color(0.0f, 0.3f, 0.6f, 1f);
            btn.colors = cb;
        }

        GameObject textObj = new GameObject("BtnText");
        textObj.transform.SetParent(btnObj.transform, false);
        TextMeshProUGUI txt = textObj.AddComponent<TextMeshProUGUI>();
        txt.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        txt.fontSize = 16;
        txt.text = label;
        txt.alignment = TextAlignmentOptions.Center;
        txt.color = Color.white;
        txt.raycastTarget = false;

        RectTransform txtRect = textObj.GetComponent<RectTransform>();
        txtRect.anchorMin = Vector2.zero;
        txtRect.anchorMax = Vector2.one;
        txtRect.sizeDelta = Vector2.zero;

        LayoutElement le = btnObj.AddComponent<LayoutElement>();
        le.minHeight = 42;
    }

    private string TranslateDeviceName(string originalName)
    {
        string nameLower = originalName.ToLower();
        if (nameLower.Contains("washing")) return "Стиральная машина ATLANT";
        if (nameLower.Contains("kettle")) return "Умный чайник ATLANT";
        if (nameLower.Contains("microwave")) return "Микроволновая печь ATLANT";
        if (nameLower.Contains("coffee")) return "Кофемашина ATLANT";
        if (nameLower.Contains("light")) return "Умное освещение";
        return originalName;
    }

    private string TranslateActionLabel(string originalAction)
    {
        switch (originalAction.ToLower())
        {
            case "программа": return "Программа стирки";
            case "режим": return "Режим работы";
            case "пресет": return "Выбор пресета";
            case "крепость": return "Крепость кофе";
            case "температура": return "Температура";
            case "температура света": return "Цвет. температура";
            case "отжим": return "Скорость отжима";
            case "время": return "Время таймера";
            case "мощность": return "Уровень мощности";
            case "объем": return "Объем порции";
            case "поддержание тепла": return "Поддержание тепла";
            case "вкл / выкл": return "Включить питание";
            case "вкл / стоп": return "Старт / Стоп";
            case "старт / стоп": return "Запуск процесса";
            case "приготовить / стоп": return "Приготовление / Отмена";
            default: return originalAction;
        }
    }

    private void FreezePlayerCamera(bool freeze)
    {
        if (freeze)
        {
            disabledCameraScripts.Clear();
            MonoBehaviour[] allScripts = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var script in allScripts)
            {
                if (script == null || !script.enabled) continue;
                string typeName = script.GetType().Name.ToLower();

                if (typeName.Contains("look") || typeName.Contains("camera") || typeName.Contains("controller") || typeName.Contains("movement"))
                {
                    if (script is AtlantDeviceTooltipUI || script.gameObject.transform.root == transform.root) continue;

                    script.enabled = false;
                    disabledCameraScripts.Add(script);
                }
            }

            Time.timeScale = 0f;
        }
        else
        {
            foreach (var script in disabledCameraScripts)
            {
                if (script != null) script.enabled = true;
            }
            disabledCameraScripts.Clear();
            Time.timeScale = 1f;
        }
    }

    private void ClearGeneratedUI()
    {
        foreach (var obj in generatedUIElements)
        {
            if (obj != null) Destroy(obj);
        }
        generatedUIElements.Clear();
    }

    private void OnDisable()
    {
        FreezePlayerCamera(false);
    }

    private void OnDestroy()
    {
        if (animationCoroutine != null)
        {
            StopCoroutine(animationCoroutine);
            animationCoroutine = null;
        }

        FreezePlayerCamera(false);
        ClearGeneratedUI();
    }

    private IEnumerator AnimateTooltip(float targetAlpha, Vector3 targetScale, float duration, bool deactivateOnEnd)
    {
        float startAlpha = canvasGroup.alpha;
        Vector3 startScale = panel.localScale;
        float time = 0f;

        while (time < duration)
        {
            time += Time.unscaledDeltaTime;
            float t = time / duration;
            float ease = t * t * (3f - 2f * t);

            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, ease);
            panel.localScale = Vector3.Lerp(startScale, targetScale, ease);
            yield return null;
        }

        canvasGroup.alpha = targetAlpha;
        panel.localScale = targetScale;

        if (deactivateOnEnd)
        {
            gameObject.SetActive(false);
            ClearGeneratedUI();
        }
    }

    private static string BuildStatusText(AtlantMqttDeviceBase device)
    {
        var builder = new StringBuilder();
        string mqttStatus = device.IsMqttConnected ? "<color=#4CAF50>В СЕТИ</color>" : "<color=#F44336>ОФФЛАЙН</color>";

        builder.AppendLine($"<size=85%><color=#A0AAB5>MQTT Сеть:</color> {mqttStatus}</size>");

        if (!string.IsNullOrEmpty(device.CurrentPairingCode))
        {
            builder.AppendLine($"<size=125%><color=#FFEB3B>[!] Код сопряжения: {device.CurrentPairingCode}</color></size>");
        }
        else
        {
            string stateRu = NormalizeStatusText(device.CurrentStatusLine);
            builder.AppendLine($"<size=110%><color=#2196F3>●</color> {stateRu}</size>");
        }

        builder.AppendLine("<color=#3A4D63>---------------------------------------</color>");

        string summaryRu = device.CurrentDataSummary
            .Replace("Preset:", "Режим:")
            .Replace("stage:", "Этап:")
            .Replace("idle", "ожидание")
            .Replace("heating", "нагрев")
            .Replace("keep_warm", "поддержание тепла")
            .Replace("ready", "готово")
            .Replace("running", "выполняется")
            .Replace("Текущая темп.:", "Текущая температура:")
            .Replace("Целевая темп.:", "Установлено до:")
            .Replace("Температура воды:", "Температура:")
            .Replace("Дверца заблокирована:", "Замок двери:")
            .Replace("Осталось:", "Осталось времени:")
            .Replace("мин", "мин.")
            .Replace("yes", "закрыта").Replace("no", "открыта");

        builder.Append($"<color=#E0E0E0>{summaryRu}</color>");
        return builder.ToString();
    }

    private void EnsureReferences()
    {
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        if (panel == null) panel = transform.Find("Device Control Panel") as RectTransform;

        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

    private static string NormalizeStatusText(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "Готов к работе";
        }

        string normalized = status.Trim();

        if (normalized.Contains("Already paired") || normalized.Contains("Уже сопряжено") || normalized.StartsWith("Al"))
        {
            normalized = "Уже сопряжено";
        }
        else if (normalized == "Connected" || normalized == "Подключено")
        {
            normalized = "Подключено";
        }
        else if (normalized == "Disconnected" || normalized == "Отключено")
        {
            normalized = "Отключено";
        }
        else if (normalized == "Connecting..." || normalized == "Подключение...")
        {
            normalized = "Подключение...";
        }
        else if (normalized == "Connection error" || normalized == "Ошибка подключения")
        {
            normalized = "Ошибка подключения";
        }
        else if (normalized == "Pairing error" || normalized == "Ошибка сопряжения")
        {
            normalized = "Ошибка сопряжения";
        }

        return normalized
            .Replace("running", "Выполняется")
            .Replace("idle", "Ожидание")
            .Replace("ready", "Готов к работе")
            .Replace("heating", "Нагрев")
            .Replace("keep_warm", "Поддержание тепла")
            .Replace("brewing", "Приготовление")
            .Replace("Инициализация", "Запуск прибора");
    }
}
