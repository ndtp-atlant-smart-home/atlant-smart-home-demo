using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public class AtlantFreezerDevice : AtlantMqttDeviceBase
{
    [Header("Freezer State")]
    [SerializeField] private bool power = true;
    [SerializeField][Range(-30f, -15f)] private float targetTemp = -20f;
    [SerializeField] private bool superFreeze;
    [SerializeField] private bool doorOpen;
    [SerializeField][Range(-30f, 5f)] private float currentTemp = -18f;

    [Header("Simulation")]
    [SerializeField] private float temperatureUpdateInterval = 1f;
    [SerializeField] private float doorOpenWarmRate = 0.8f;
    [SerializeField] private float doorClosedCoolRate = 0.2f;
    [SerializeField] private float superFreezeBonus = 0.4f;
    [SerializeField] private float publishStateAfterRightClickDelay = 0.05f;

    private float nextTemperatureUpdateTime;
    private float nextDoorPublishTime;
    private bool pendingDoorStatePublish;
    private static readonly AtlantDeviceActionHint[] InteractionHints =
    {
        new AtlantDeviceActionHint(KeyCode.F, "Вкл / выкл"),
        new AtlantDeviceActionHint(KeyCode.Alpha1, "Целевая температура", true),
        new AtlantDeviceActionHint(KeyCode.Alpha2, "Быстрая заморозка", true)
    };

    protected override string DeviceTypeKey => "freezer";

    public override IReadOnlyList<AtlantDeviceActionHint> GetInteractionHints()
    {
        return InteractionHints;
    }

    protected override void Update()
    {
        base.Update();

        if (!IsMqttConnected)
        {
            return;
        }

        if (Time.time >= nextTemperatureUpdateTime)
        {
            nextTemperatureUpdateTime = Time.time + temperatureUpdateInterval;
            SimulateTemperature();
        }

        if (pendingDoorStatePublish && Time.time >= nextDoorPublishTime)
        {
            pendingDoorStatePublish = false;
            PublishState();
        }
    }

    private void OnMouseOver()
    {
        if (!IsMqttConnected)
        {
            return;
        }

        if (Input.GetMouseButtonDown(1))
        {
            ToggleDoor();
        }
    }

    public override void HandleInteractionKey(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.F:
                power = !power;
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
            targetTemp = Mathf.Clamp(targetTemp + step, -30f, -15f);
        }
        else if (key == KeyCode.Alpha2)
        {
            superFreeze = !superFreeze;
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
            return power ? "Выключить" : "Включить";
        }

        return string.Empty;
    }

    protected override void ApplyCommand(JObject command)
    {
        if (command == null)
        {
            return;
        }

        if (command["power"] != null)
        {
            power = command["power"].Value<bool>();
        }

        if (command["target_temp"] != null)
        {
            targetTemp = Mathf.Clamp(command["target_temp"].Value<int>(), -30, -15);
        }

        if (command["super_freeze"] != null)
        {
            superFreeze = command["super_freeze"].Value<bool>();
        }

        if (command["door_open"] != null)
        {
            doorOpen = command["door_open"].Value<bool>();
        }

        PublishState();
    }

    protected override JObject BuildStatePayload()
    {
        return new JObject
        {
            ["power"] = power,
            ["current_temp"] = currentTemp,
            ["target_temp"] = Mathf.RoundToInt(targetTemp),
            ["super_freeze"] = superFreeze,
            ["door_open"] = doorOpen
        };
    }

    protected override string BuildStateSummary()
    {
        return doorOpen ? $"дверь открыта, {currentTemp:0.0}°C" : $"{currentTemp:0.0}°C, цель {targetTemp:0}°C";
    }

    protected override string BuildDataSummary()
    {
        return $"Питание: {(power ? "ВКЛ" : "ВЫКЛ")}\n" +
               $"Текущая температура: {currentTemp:0.0}°C\n" +
               $"Целевая температура: {targetTemp:0}°C\n" +
               $"Быстрая заморозка: {(superFreeze ? "да" : "нет")}\n" +
               $"Дверца: {(doorOpen ? "открыта" : "закрыта")}";
    }

    private void ToggleDoor()
    {
        doorOpen = !doorOpen;
        pendingDoorStatePublish = true;
        nextDoorPublishTime = Time.time + publishStateAfterRightClickDelay;
    }

    private void SimulateTemperature()
    {
        float desiredTemp = doorOpen ? Mathf.Min(5f, targetTemp + 6f) : targetTemp;
        float rate = doorOpen ? doorOpenWarmRate : doorClosedCoolRate;
        if (superFreeze && !doorOpen)
        {
            desiredTemp -= superFreezeBonus;
        }

        currentTemp = Mathf.MoveTowards(currentTemp, desiredTemp, rate);
        currentTemp = Mathf.Clamp(currentTemp, -30f, 5f);
        PublishState();
    }
}
