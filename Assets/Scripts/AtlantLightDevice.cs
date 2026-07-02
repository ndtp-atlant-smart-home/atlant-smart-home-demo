using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class AtlantLightDevice : AtlantMqttDeviceBase
{
    [Header("Light State")]
    [SerializeField] private bool power;
    [SerializeField][Range(0, 100)] private int brightness = 70;
    [SerializeField][Range(1500, 9000)] private int colorTemp = 3000;
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private Light targetLight;

    private Material runtimeMaterial;

    private static readonly AtlantDeviceActionHint[] InteractionHints =
    {
        new AtlantDeviceActionHint(KeyCode.F, "Вкл / выкл"),
        new AtlantDeviceActionHint(KeyCode.Alpha1, "Яркость", true),
        new AtlantDeviceActionHint(KeyCode.Alpha2, "Температура света", true)
    };

    protected override string DeviceTypeKey => "light";

    protected override void Start()
    {
        if (targetRenderer == null) targetRenderer = GetComponentInChildren<Renderer>();
        if (targetLight == null) targetLight = GetComponentInChildren<Light>();
        if (targetRenderer != null) runtimeMaterial = targetRenderer.material;

        ApplyVisuals();
        base.Start();
    }

    public override IReadOnlyList<AtlantDeviceActionHint> GetInteractionHints()
    {
        return InteractionHints;
    }

    public override void HandleInteractionKey(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.F:
                power = !power;
                ApplyVisuals();
                break;
        }
        PublishState();
    }

    public override void BeginInteractionAdjustment(KeyCode key) { }

    public override void AdjustInteractionValue(KeyCode key, float direction)
    {
        int step = direction > 0 ? 1 : -1;

        if (key == KeyCode.Alpha1) brightness = Mathf.Clamp(brightness + step * 5, 0, 100);
        else if (key == KeyCode.Alpha2) colorTemp = Mathf.Clamp(colorTemp + step * 250, 1500, 9000);
        ApplyVisuals();
    }

    public override void CommitInteractionAdjustment(KeyCode key)
    {
        PublishState();
    }

    public override string GetPrimaryActionLabel(KeyCode key)
    {
        if (key == KeyCode.F)
        {
            return power ? "Выключить" : "Включить";
        }
        return string.Empty;
    }

    protected override void ApplyCommand(JObject command)
    {
        if (command == null) return;

        if (command["power"] != null) power = command["power"].Value<bool>();
        if (command["running"] != null) power = command["running"].Value<bool>();
        if (command["brightness"] != null) brightness = Mathf.Clamp(command["brightness"].Value<int>(), 0, 100);
        if (command["color_temp"] != null) colorTemp = Mathf.Clamp(command["color_temp"].Value<int>(), 1500, 9000);

        ApplyVisuals();
        PublishState();
    }

    protected override void OnDestroy()
    {
        if (runtimeMaterial != null)
        {
            Destroy(runtimeMaterial);
            runtimeMaterial = null;
        }

        base.OnDestroy();
    }

    private void ApplyVisuals()
    {
        Color color = ColorFromTemperature(colorTemp);
        float intensity = power ? brightness / 100f : 0f;

        if (targetLight != null)
        {
            targetLight.enabled = power && brightness > 0;
            targetLight.color = color;
            targetLight.intensity = Mathf.Lerp(0.2f, 2.5f, intensity);
        }

        if (runtimeMaterial != null)
        {
            Color materialColor = Color.Lerp(Color.black, color, intensity);
            runtimeMaterial.color = materialColor;

            if (power && brightness > 0)
            {
                runtimeMaterial.EnableKeyword("_EMISSION");
                runtimeMaterial.SetColor("_EmissionColor", materialColor * 1.5f);
            }
            else runtimeMaterial.DisableKeyword("_EMISSION");
        }
    }

    private static Color ColorFromTemperature(int kelvin)
    {
        float warm = Mathf.InverseLerp(1500f, 9000f, kelvin);
        return Color.Lerp(new Color(1f, 0.4f, 0f), new Color(0.6f, 0.8f, 1f), warm);
    }

    protected override JObject BuildStatePayload()
    {
        return new JObject
        {
            ["power"] = power,
            ["brightness"] = brightness,
            ["color_temp"] = colorTemp
        };
    }

    protected override string BuildStateSummary()
    {
        return power ? $"включен, яркость: {brightness}%" : "выключен";
    }

    protected override string BuildDataSummary()
    {
        return $"Состояние: {(power ? "ВКЛ" : "ВЫКЛ")}\n" +
               $"Яркость: {brightness}%\n" +
               $"Цветовая температура: {colorTemp}K";
    }
}
