using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class AtlantCoffeeMachineDevice : AtlantMqttDeviceBase
{
    [Header("Coffee Machine State")]
    [SerializeField] private bool brewing;
    [SerializeField] private string profileName = "morning";
    [SerializeField] private string strength = "medium";
    [SerializeField][Range(30, 500)] private int volumeMl = 180;
    [SerializeField][Range(70, 100)] private int temperatureC = 92;
    [SerializeField] private bool scheduleEnabled;
    [SerializeField] private string scheduleTime = "07:00";
    [SerializeField] private string stage = "idle";
    [SerializeField] private int cupsBrewed;
    [SerializeField] private int cleaningCyclesUntilRequired = 12;
    [SerializeField] private bool waterTankEmpty;
    [SerializeField] private bool beansEmpty;
    [SerializeField] private string error = "";

    [Header("Simulation")]
    [SerializeField] private float brewDurationSeconds = 12f;
    private float brewElapsed;

    private static readonly AtlantDeviceActionHint[] InteractionHints =
    {
        new AtlantDeviceActionHint(KeyCode.F, "Приготовить / стоп"),
        new AtlantDeviceActionHint(KeyCode.Alpha1, "Крепость", true),
        new AtlantDeviceActionHint(KeyCode.Alpha2, "Объем", true),
        new AtlantDeviceActionHint(KeyCode.Alpha3, "Температура", true)
    };

    protected override string DeviceTypeKey => "coffee_machine";

    protected override void Update()
    {
        base.Update();

        if (brewing)
        {
            brewElapsed += Time.deltaTime;
            if (brewElapsed >= brewDurationSeconds)
            {
                brewing = false;
                brewElapsed = 0f;
                stage = "idle";
                cupsBrewed++;
                if (cleaningCyclesUntilRequired > 0) cleaningCyclesUntilRequired--;
                PublishState();
            }
        }
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
                if (brewing) StopBrew();
                else StartBrew();
                break;
        }
        PublishState();
    }

    public override void BeginInteractionAdjustment(KeyCode key) { }

    public override void AdjustInteractionValue(KeyCode key, float direction)
    {
        int step = direction > 0 ? 1 : -1;
        if (key == KeyCode.Alpha1) CycleStrength(step);
        else if (key == KeyCode.Alpha2) volumeMl = Mathf.Clamp(volumeMl + step * 20, 30, 500);
        else if (key == KeyCode.Alpha3) temperatureC = Mathf.Clamp(temperatureC + step * 2, 70, 100);
    }

    public override void CommitInteractionAdjustment(KeyCode key)
    {
        PublishState();
    }

    public override string GetPrimaryActionLabel(KeyCode key)
    {
        if (key == KeyCode.F)
        {
            return brewing ? "Стоп" : "Старт";
        }
        return string.Empty;
    }

    protected override void ApplyCommand(JObject command)
    {
        if (command == null) return;

        if (command["brew"] != null)
        {
            bool run = command["brew"].Value<bool>();
            if (run && !brewing) StartBrew();
            else if (!run && brewing) StopBrew();
        }

        if (command["strength"] != null) strength = command["strength"].Value<string>().ToLower();
        if (command["profile_name"] != null) profileName = command["profile_name"].Value<string>();

        if (command["volume_ml"] != null) volumeMl = Mathf.Clamp(command["volume_ml"].Value<int>(), 30, 500);
        if (command["temperature_c"] != null) temperatureC = Mathf.Clamp(command["temperature_c"].Value<int>(), 70, 100);

        if (command["schedule_enabled"] != null) scheduleEnabled = command["schedule_enabled"].Value<bool>();
        if (command["schedule_time"] != null) scheduleTime = command["schedule_time"].Value<string>();

        if (command["stats_request"] != null && command["stats_request"].Value<bool>())
        {
            PublishState();
            return;
        }

        if (command["reset_cleaning_reminder"] != null && command["reset_cleaning_reminder"].Value<bool>())
        {
            cleaningCyclesUntilRequired = 12;
        }

        PublishState();
    }

    private void StartBrew()
    {
        brewing = true;
        brewElapsed = 0f;
        stage = "brewing";
        error = "";
    }

    private void StopBrew()
    {
        brewing = false;
        brewElapsed = 0f;
        stage = "idle";
    }

    private void CycleStrength(int direction = 1)
    {
        string[] strengths = { "mild", "medium", "strong" };
        int index = System.Array.IndexOf(strengths, strength);
        if (index < 0) index = 1;

        index = (index + direction + strengths.Length) % strengths.Length;
        strength = strengths[index];
    }

    protected override JObject BuildStatePayload()
    {
        // Ключи идеально мэтчатся с ApplyCommand и валидатором бэкенда
        return new JObject
        {
            ["brewing"] = brewing,
            ["profile_name"] = profileName,
            ["strength"] = strength,
            ["volume_ml"] = volumeMl,
            ["temperature_c"] = temperatureC,
            ["schedule_enabled"] = scheduleEnabled,
            ["schedule_time"] = scheduleTime,
            ["stage"] = stage,
            ["cups_brewed"] = cupsBrewed,
            ["cleaning_cycles_left"] = cleaningCyclesUntilRequired,
            ["water_tank_empty"] = waterTankEmpty,
            ["beans_empty"] = beansEmpty,
            ["error"] = error
        };
    }

    protected override string BuildStateSummary() => brewing ? "приготовление кофе" : "готова к работе";

    protected override string BuildDataSummary()
    {
        string strengthRu = strength == "mild" ? "Мягкий" : strength == "medium" ? "Средний" : "Крепкий";
        return $"Крепость: {strengthRu}\n" +
               $"Объем порции: {volumeMl} мл\n" +
               $"Температура: {temperatureC}°C\n" +
               $"Текущий статус: {stage}\n" +
               $"Очистка через: {cleaningCyclesUntilRequired} чашек";
    }
}
