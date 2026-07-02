using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class AtlantWashingMachineDevice : AtlantMqttDeviceBase
{
    [Header("Washing Machine State")]
    [SerializeField] private bool power;
    [SerializeField] private string preset = "eco"; // Строго латиница по tasks.md
    [SerializeField][Range(20, 95)] private int waterTemp = 40;
    [SerializeField][Range(400, 1400)] private int spinRpm = 800;
    [SerializeField] private string stage = "idle";
    [SerializeField] private int remainingMinutes = 60;
    [SerializeField] private string error = "";

    [Header("Task State (Backend Sync)")]
    [SerializeField] private string currentTaskId = "";
    [SerializeField] private string currentTaskStatus = "";

    [Header("Simulation")]
    [SerializeField] private float secondsPerSimulatedMinute = 1.5f;
    [SerializeField] private float minuteAccumulator;

    private static readonly AtlantDeviceActionHint[] InteractionHints =
    {
        new AtlantDeviceActionHint(KeyCode.F, "Старт / стоп"),
        new AtlantDeviceActionHint(KeyCode.Alpha1, "Программа", true),
        new AtlantDeviceActionHint(KeyCode.Alpha2, "Температура", true),
        new AtlantDeviceActionHint(KeyCode.Alpha3, "Отжим", true)
    };

    protected override string DeviceTypeKey => "washing_machine";

    protected override void Update()
    {
        // [ИСПРАВЛЕНО] Базовый метод Update() должен вызываться ВСЕГДА, 
        // иначе у девайса зависнет mainThreadQueue и сломается обработка MQTT-команд
        base.Update();

        if (power && remainingMinutes > 0)
        {
            minuteAccumulator += Time.deltaTime;
            if (minuteAccumulator >= secondsPerSimulatedMinute)
            {
                remainingMinutes--;
                minuteAccumulator = 0f;

                if (remainingMinutes <= 0)
                {
                    power = false;
                    stage = "ready";
                    currentTaskStatus = "completed";

                    // [ИСПРАВЛЕНО] Сначала отправляем стейт со статусом 'completed' и валидным task_id,
                    // чтобы бэкенд успел зафиксировать успешное завершение конкретной задачи
                    PublishState();

                    // Только после публикации финального пакета очищаем ID таски для будущих сессий
                    currentTaskId = "";
                    currentTaskStatus = "";
                }
                else
                {
                    // Обычное обновление прогресса во время стирки
                    PublishState();
                }
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
                power = !power;
                if (power)
                {
                    stage = "wash";
                    currentTaskStatus = "running";
                    ApplyPresetParameters();
                }
                else
                {
                    stage = "idle";
                    if (!string.IsNullOrEmpty(currentTaskId))
                    {
                        currentTaskStatus = "stopped";
                    }
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
            CyclePreset(step);
            ApplyPresetParameters();
        }
        else if (key == KeyCode.Alpha2)
        {
            waterTemp = Mathf.Clamp(waterTemp + step * 5, 20, 95);
        }
        else if (key == KeyCode.Alpha3)
        {
            spinRpm = Mathf.Clamp(spinRpm + step * 100, 400, 1400);
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
            return power ? "Стоп" : "Старт";
        }
        return string.Empty;
    }

    protected override void ApplyCommand(JObject command)
    {
        if (command == null) return;

        // ====== 1. ОБРАБОТКА ЗАПУСКА ЗАДАЧИ (washing_cycle) ======
        if (command["action"] != null && command["action"].Value<string>() == "start_task")
        {
            power = true;
            stage = "wash";
            currentTaskStatus = "running";

            if (command["task_id"] != null)
                currentTaskId = command["task_id"].Value<string>();

            // Сначала парсим и выставляем пресет, чтобы подтянулись базовые тайминги (remainingMinutes)
            if (command["payload"] != null && command["payload"]["preset"] != null)
            {
                preset = command["payload"]["preset"].Value<string>().ToLower();
            }
            ApplyPresetParameters();

            // Накатываем точечные кастомные параметры поверх дефолтных, если они пришли в таске
            if (command["payload"] != null)
            {
                JObject payload = (JObject)command["payload"];
                if (payload["water_temp"] != null) waterTemp = Mathf.Clamp(payload["water_temp"].Value<int>(), 20, 95);
                if (payload["spin_rpm"] != null) spinRpm = Mathf.Clamp(payload["spin_rpm"].Value<int>(), 400, 1400);
            }

            PublishState();
            return;
        }

        if (command["status_request"] != null && command["status_request"].Value<bool>())
        {
            PublishState();
            return;
        }

        // ====== 2. ОБРАБОТКА ОТМЕНЫ ЗАДАЧИ С БЭКЕНДА (cancel_task) ======
        if (command["action"] != null && command["action"].Value<string>() == "cancel_task")
        {
            power = false;
            stage = "idle";
            currentTaskStatus = "cancelled";
            PublishState();

            // Сбрасываем ID таска после отправки финального статуса отмены
            currentTaskId = "";
            currentTaskStatus = "";
            return;
        }
    }

    private void ApplyPresetParameters()
    {
        switch (preset.ToLower())
        {
            case "quick":
                waterTemp = 30;
                spinRpm = 800;
                remainingMinutes = 15;
                break;
            case "delicate":
                waterTemp = 30;
                spinRpm = 600;
                remainingMinutes = 40;
                break;
            case "cotton":
                waterTemp = 60;
                spinRpm = 1200;
                remainingMinutes = 90;
                break;
            case "rinse_spin":
                waterTemp = 20;
                spinRpm = 1000;
                remainingMinutes = 20;
                break;
            default:
                preset = "eco";
                waterTemp = 40;
                spinRpm = 1000;
                remainingMinutes = 60;
                break;
        }
    }

    private void CyclePreset(int direction)
    {
        string[] presets = { "eco", "quick", "delicate", "cotton", "rinse_spin" };
        int index = System.Array.IndexOf(presets, preset);
        if (index < 0) index = 0;

        index = (index + direction + presets.Length) % presets.Length;
        preset = presets[index];
    }

    protected override JObject BuildStatePayload()
    {
        // Плоская и чистая структура стейта для обновления состояния девайса на бэкенде
        JObject state = new JObject
        {
            ["power"] = power,
            ["preset"] = preset,
            ["water_temp"] = waterTemp,
            ["spin_rpm"] = spinRpm,
            ["stage"] = stage,
            ["remaining_min"] = remainingMinutes,
            ["error"] = error,
            // [ИСПРАВЛЕНО] Переводим значение в нижний регистр ("online"/"offline"), 
            // чтобы бэкенд и Pydantic-валидатор не отбрасывали пакет
            ["value"] = power ? "online" : "offline"
        };

        // Подмешиваем параметры активного таска на верхний уровень для синхронизации
        if (!string.IsNullOrWhiteSpace(currentTaskId))
        {
            state["task_id"] = currentTaskId;
            state["task_status"] = currentTaskStatus; // Считывается в методе sync_from_device_state бэкенда
        }

        return state;
    }

    protected override string BuildStateSummary() => power ? $"стирка: {stage}" : "остановлена";

    protected override string BuildDataSummary()
    {
        string presetRu = preset == "eco" ? "Эко" : preset == "quick" ? "Быстрая" : preset == "delicate" ? "Деликатная" : preset == "cotton" ? "Хлопок" : "Полоскание";
        return $"Программа: {presetRu}\n" +
               $"Температура воды: {waterTemp}°C\n" +
               $"Скорость отжима: {spinRpm} об/мин\n" +
               $"Этап: {stage}\n" +
               $"Осталось: {remainingMinutes} мин.";
    }
}