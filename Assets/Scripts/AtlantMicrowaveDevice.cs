using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class AtlantMicrowaveDevice : AtlantMqttDeviceBase
{
    [Header("Microwave State")]
    [SerializeField] private bool running;
    [SerializeField][Range(1, 10)] private int powerLevel = 7;
    [SerializeField][Range(5, 7200)] private int durationSec = 180;
    [SerializeField] private int remainingSec;
    [SerializeField] private string preset = "soup";
    [SerializeField] private bool doorClosed = true;
    [SerializeField] private string stage = "idle";
    [SerializeField] private string lastProductQr = "";
    [SerializeField] private string error = "";

    [Header("Simulation")]
    [SerializeField] private float telemetryEverySeconds = 1f;

    private int lastDurationSec = 180;
    private int lastPowerLevel = 7;
    private string lastPreset = "soup";
    private float nextTelemetryTime;

    private static readonly AtlantDeviceActionHint[] InteractionHints =
    {
        new AtlantDeviceActionHint(KeyCode.F, "Старт / стоп"),
        new AtlantDeviceActionHint(KeyCode.Alpha1, "Время", true),
        new AtlantDeviceActionHint(KeyCode.Alpha2, "Мощность", true),
        new AtlantDeviceActionHint(KeyCode.Alpha3, "Пресет", true)
    };

    protected override string DeviceTypeKey => "microwave";

    public override IReadOnlyList<AtlantDeviceActionHint> GetInteractionHints()
    {
        return InteractionHints;
    }

    public override void HandleInteractionKey(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.F:
                running = !running;
                if (running)
                {
                    stage = "cooking";
                    if (remainingSec <= 0) remainingSec = durationSec;
                    CacheLastProgram();
                }
                else
                {
                    stage = "idle";
                    remainingSec = 0;
                }
                break;
        }
        PublishState();
    }

    public override void BeginInteractionAdjustment(KeyCode key) { }

    public override void AdjustInteractionValue(KeyCode key, float direction)
    {
        int step = direction > 0 ? 1 : -1;

        if (key == KeyCode.Alpha1)
        {
            durationSec = Mathf.Clamp(durationSec + step * 30, 5, 7200);
            if (!running) remainingSec = durationSec;
            CacheLastProgram();
        }
        else if (key == KeyCode.Alpha2) powerLevel = Mathf.Clamp(powerLevel + step, 1, 10);
        else if (key == KeyCode.Alpha3)
        {
            CyclePreset(step);
            ApplyPreset();
        }
    }

    public override void CommitInteractionAdjustment(KeyCode key)
    {
        PublishState();
    }

    public override string GetPrimaryActionLabel(KeyCode key)
    {
        if (key == KeyCode.F)
        {
            return running ? "Стоп" : "Старт";
        }
        return string.Empty;
    }

    protected override void ApplyCommand(JObject command)
    {
        if (command == null) return;

        if (command["start"] != null)
        {
            running = command["start"].Value<bool>();
            stage = running ? "cooking" : "idle";
            if (running && remainingSec <= 0)
            {
                remainingSec = durationSec;
            }
            if (!running)
            {
                remainingSec = 0;
            }
        }

        if (command["preset"] != null)
        {
            preset = command["preset"].Value<string>();
            ApplyPreset();
        }

        if (command["duration_sec"] != null)
        {
            durationSec = Mathf.Clamp(command["duration_sec"].Value<int>(), 5, 7200);
            if (!running) remainingSec = durationSec;
            CacheLastProgram();
        }

        if (command["power_level"] != null)
        {
            powerLevel = Mathf.Clamp(command["power_level"].Value<int>(), 1, 10);
            CacheLastProgram();
        }

        if (command["repeat_last"] != null && command["repeat_last"].Value<bool>())
        {
            RestoreLastProgram();
            running = true;
            stage = "cooking";
            if (remainingSec <= 0) remainingSec = durationSec;
        }

        if (command["product_qr"] != null)
        {
            lastProductQr = command["product_qr"].Value<string>();
        }

        PublishState();
    }

    private void ApplyPreset()
    {
        switch (preset.ToLower())
        {
            case "pizza":
                powerLevel = 8;
                durationSec = 240;
                break;
            case "popcorn":
                powerLevel = 10;
                durationSec = 150;
                break;
            default:
                preset = "soup";
                powerLevel = 6;
                durationSec = 180;
                break;
        }
        if (!running) remainingSec = durationSec;
        CacheLastProgram();
    }

    private void CyclePreset(int direction = 1)
    {
        string[] presets = { "soup", "pizza", "popcorn" };
        int index = System.Array.IndexOf(presets, preset);
        if (index < 0) index = 0;

        index = (index + direction + presets.Length) % presets.Length;
        preset = presets[index];
    }

    private void CacheLastProgram()
    {
        lastDurationSec = durationSec;
        lastPowerLevel = powerLevel;
        lastPreset = preset;
    }

    private void RestoreLastProgram()
    {
        durationSec = lastDurationSec;
        powerLevel = lastPowerLevel;
        preset = lastPreset;
        if (!running)
        {
            remainingSec = durationSec;
        }
    }

    protected override void Update()
    {
        base.Update();

        if (!IsMqttConnected)
        {
            return;
        }

        if (Time.time < nextTelemetryTime)
        {
            return;
        }

        nextTelemetryTime = Time.time + telemetryEverySeconds;

        if (running && remainingSec > 0)
        {
            remainingSec = Mathf.Max(0, remainingSec - Mathf.Max(1, Mathf.RoundToInt(telemetryEverySeconds)));
            if (remainingSec == 0)
            {
                running = false;
                stage = "idle";
            }
            PublishState();
        }
    }

    protected override JObject BuildStatePayload()
    {
        return new JObject
        {
            ["running"] = running,
            ["power_level"] = powerLevel,
            ["duration_sec"] = durationSec,
            ["remaining_sec"] = remainingSec,
            ["preset"] = preset,
            ["door_closed"] = doorClosed,
            ["stage"] = stage,
            ["last_product_qr"] = lastProductQr,
            ["error"] = error
        };
    }

    protected override string BuildStateSummary()
    {
        return running ? $"работает: {stage}, осталось: {remainingSec}с" : "печь остановлена";
    }

    protected override string BuildDataSummary()
    {
        return $"Режим пресета: {preset}\n" +
               $"Уровень мощности: {powerLevel}/10\n" +
               $"Время таймера: {durationSec} сек.\n" +
               $"Осталось времени: {remainingSec} сек.\n" +
               $"Дверца закрыта: {(doorClosed ? "да" : "нет")}";
    }
}
