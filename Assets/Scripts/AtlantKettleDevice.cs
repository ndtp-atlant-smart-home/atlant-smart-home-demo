using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class AtlantKettleDevice : AtlantMqttDeviceBase
{
    [Header("Kettle State")]
    [SerializeField] private bool power;
    [SerializeField][Range(20, 100)] private int currentTemp = 22;
    [SerializeField][Range(40, 100)] private int targetTemp = 95;
    [SerializeField] private bool keepWarm;
    [SerializeField] private string preset = "black_tea"; // black_tea, green_tea, coffee, baby_formula, custom
    [SerializeField] private string stage = "idle";       // idle, heating, keep_warm
    [SerializeField] private bool waterPresent = true;

    [Header("Task State (Backend Sync)")]
    [SerializeField] private string currentTaskId = "";
    [SerializeField] private string currentTaskStatus = "";

    [Header("Simulation")]
    [SerializeField] private float heatDegreesPerSecond = 8f;
    [SerializeField] private float coolDegreesPerSecond = 0.3f;

    private float temperatureAccumulator;

    private static readonly AtlantDeviceActionHint[] InteractionHints =
    {
        new AtlantDeviceActionHint(KeyCode.F, "Вкл / стоп"),
        new AtlantDeviceActionHint(KeyCode.Alpha1, "Температура", true),
        new AtlantDeviceActionHint(KeyCode.Alpha2, "Режим", true),
        new AtlantDeviceActionHint(KeyCode.Alpha3, "Поддержание тепла")
    };

    protected override string DeviceTypeKey => "kettle";

    protected override void Update()
    {
        base.Update();
        SimulateTemperature();
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
                stage = power ? "heating" : "idle";

                // Если пользователь вручную остановил кипячение, прерываем текущую задачу бэкенда
                if (!power && !string.IsNullOrEmpty(currentTaskId))
                {
                    currentTaskStatus = "stopped";
                }
                break;

            case KeyCode.Alpha3:
                keepWarm = !keepWarm;
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
            targetTemp = Mathf.Clamp(targetTemp + step * 5, 40, 100);
            preset = "custom"; // Ручной выбор температуры сбрасывает пресет
        }
        else if (key == KeyCode.Alpha2)
        {
            CyclePreset(step);
            ApplyPresetTemperature();
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
        if (key == KeyCode.Alpha3)
        {
            return keepWarm ? "Подогрев: вкл" : "Подогрев: выкл";
        }
        return string.Empty;
    }

    protected override void ApplyCommand(JObject command)
    {
        if (command == null) return;

        // ====== 1. ОБРАБОТКА ЗАПУСКА ЗАДАЧИ (boiling_cycle) ======
        if (command["action"] != null && command["action"].Value<string>() == "start_task")
        {
            power = true;
            stage = "heating";
            currentTaskStatus = "running";

            if (command["task_id"] != null)
                currentTaskId = command["task_id"].Value<string>();

            JObject payload = command["payload"] as JObject;
            if (payload != null)
            {
                if (payload["preset"] != null)
                {
                    preset = payload["preset"].Value<string>().ToLower();
                    ApplyPresetTemperature();
                }

                // Накатываем точечную целевую температуру, если она передана в payload задачи
                if (payload["target_temp"] != null)
                {
                    targetTemp = Mathf.Clamp(payload["target_temp"].Value<int>(), 40, 100);
                    if (payload["preset"] == null) preset = "custom";
                }

                if (payload["keep_warm"] != null)
                {
                    keepWarm = payload["keep_warm"].Value<bool>();
                }
            }

            PublishState();
            return;
        }

        if (command["action"] != null && command["action"].Value<string>() == "start")
        {
            power = command["power"] == null || command["power"].Value<bool>();
            stage = power ? "heating" : "idle";
            if (command["target_temp"] != null)
                targetTemp = Mathf.Clamp(command["target_temp"].Value<int>(), 40, 100);
            if (command["preset"] != null)
            {
                preset = command["preset"].Value<string>().ToLower();
                ApplyPresetTemperature();
            }
            if (command["keep_warm"] != null)
                keepWarm = command["keep_warm"].Value<bool>();
            PublishState();
            return;
        }

        // ====== 2. ОБРАБОТКА ОТМЕНЫ ЗАДАЧИ (cancel_task) ======
        if (command["action"] != null && command["action"].Value<string>() == "cancel_task")
        {
            power = false;
            stage = "idle";
            currentTaskStatus = "cancelled";
            PublishState();

            // Очищаем ID таска после публикации финального статуса отмены
            currentTaskId = "";
            currentTaskStatus = "";
            return;
        }

        // ====== 3. ОБРАБОТКА СТАНДАРТНОЙ ПЛОСКОЙ КОМАНДЫ ======
        bool hasChanges = false;

        if (command["power"] != null)
        {
            power = command["power"].Value<bool>();
            stage = power ? "heating" : "idle";
            if (!power && !string.IsNullOrEmpty(currentTaskId)) currentTaskStatus = "stopped";
            hasChanges = true;
        }

        if (command["keep_warm"] != null)
        {
            keepWarm = command["keep_warm"].Value<bool>();
            hasChanges = true;
        }

        if (command["preset"] != null)
        {
            preset = command["preset"].Value<string>().ToLower();
            ApplyPresetTemperature();
            hasChanges = true;
        }

        if (command["target_temp"] != null)
        {
            targetTemp = Mathf.Clamp(command["target_temp"].Value<int>(), 40, 100);
            if (command["preset"] == null) preset = "custom";
            hasChanges = true;
        }

        if (command["status_request"] != null && command["status_request"].Value<bool>())
        {
            hasChanges = true;
        }

        if (hasChanges)
        {
            PublishState();
        }
    }

    private void SimulateTemperature()
    {
        float delta = 0f;

        // Логика нагрева
        if (power && stage == "heating")
        {
            if (currentTemp < targetTemp)
            {
                delta = heatDegreesPerSecond * Time.deltaTime;
            }
            else
            {
                currentTemp = targetTemp;

                // Фиксируем успешное завершение активной задачи бэкенда
                if (!string.IsNullOrEmpty(currentTaskId))
                {
                    currentTaskStatus = "completed";
                }

                if (keepWarm)
                {
                    stage = "keep_warm";
                }
                else
                {
                    power = false;
                    stage = "idle";
                }

                PublishState();

                // Если автоподогрев не нужен, сразу высвобождаем ID таска
                if (!keepWarm)
                {
                    currentTaskId = "";
                    currentTaskStatus = "";
                }
            }
        }
        // Логика автоподогрева
        else if (power && stage == "keep_warm")
        {
            if (!keepWarm)
            {
                power = false;
                stage = "idle";
                currentTaskId = "";
                PublishState();
            }
            else if (currentTemp < targetTemp - 2)
            {
                stage = "heating"; // Включаем ТЭН, если остыл на 2 градуса
                PublishState();
            }
            else if (currentTemp > targetTemp)
            {
                delta = -coolDegreesPerSecond * Time.deltaTime;
            }
        }
        // Логика естественного остывания прибора
        else if (currentTemp > 22)
        {
            delta = -coolDegreesPerSecond * Time.deltaTime;
        }

        temperatureAccumulator += delta;

        // Публикация стейта строго при изменении физического градуса
        if (Mathf.Abs(temperatureAccumulator) >= 1f)
        {
            int tempDiff = Mathf.RoundToInt(temperatureAccumulator);
            currentTemp = Mathf.Clamp(currentTemp + tempDiff, 20, 100);
            temperatureAccumulator = 0f;
            PublishState();
        }
    }

    private void ApplyPresetTemperature()
    {
        switch (preset.ToLower())
        {
            case "green_tea": targetTemp = 80; break;
            case "coffee": targetTemp = 92; break;
            case "baby_formula": targetTemp = 40; break;
            case "custom": break;
            default:
                preset = "black_tea";
                targetTemp = 95;
                break;
        }
    }

    private void CyclePreset(int direction = 1)
    {
        string[] presets = { "black_tea", "green_tea", "coffee", "baby_formula", "custom" };
        int index = System.Array.IndexOf(presets, preset);
        if (index < 0) index = 0;

        index = (index + direction + presets.Length) % presets.Length;
        preset = presets[index];
    }

    protected override JObject BuildStatePayload()
    {
        // Плоская и чистая структура JSON под требования валидатора pydantic на бэке
        JObject state = new JObject
        {
            ["power"] = power,
            ["current_temp"] = currentTemp,
            ["target_temp"] = targetTemp,
            ["keep_warm"] = keepWarm,
            ["preset"] = preset,
            ["stage"] = stage,
            ["water_present"] = waterPresent,
            ["value"] = power ? "online" : "offline"
        };

        // Если выполняется фоновое задание, обязательно подмешиваем верхним слоем его поля
        if (!string.IsNullOrWhiteSpace(currentTaskId))
        {
            state["task_id"] = currentTaskId;
            state["task_status"] = currentTaskStatus; // Обрабатывается в service.py (метод _handle_state)
        }

        return state;
    }

    protected override string BuildStateSummary() => $"статус: {stage}, темп: {currentTemp}°C";

    protected override string BuildDataSummary()
    {
        string presetRu = preset == "black_tea" ? "Черный чай" :
                          preset == "green_tea" ? "Зеленый чай" :
                          preset == "coffee" ? "Кофе" :
                          preset == "baby_formula" ? "Детская смесь" : "Ручной режим";

        return $"Режим: {presetRu}\n" +
               $"Текущая темп.: {currentTemp}°C\n" +
               $"Целевая темп.: {targetTemp}°C\n" +
               $"Автоподогрев: {(keepWarm ? "Вкл" : "Выкл")}\n" +
               $"Этап симуляции: {stage}";
    }
}
