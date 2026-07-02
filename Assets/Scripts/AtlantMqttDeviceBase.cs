using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using UnityEngine;

public abstract class AtlantMqttDeviceBase : MonoBehaviour
{
    private const float PairingRequestTimeoutSeconds = 8f;

    [Header("MQTT settings")]
    [SerializeField] private string brokerAddress = "127.0.0.1";
    [SerializeField] private int brokerPort = 1883;
    [SerializeField] private string mqttUsername = "";
    [SerializeField] private string mqttPassword = "";
    [SerializeField] private string mqttTopic = "";
    [SerializeField] private bool useSerializedMqttTopic = true;
    [SerializeField] private bool persistPairingState = true;
    [SerializeField] private string suggestedName = "Smart Device";
    [SerializeField] private bool connectOnStart = true;
    [SerializeField] private bool enableAutoStateHeartbeat = false;
    [SerializeField] private bool enablePresenceHeartbeat = false;
    [SerializeField] private float logStateEverySeconds = 5f;
    [SerializeField] private float autoPublishStateEverySeconds = 4f;
    [SerializeField] private float presenceHeartbeatEverySeconds = 30f;

    private readonly Queue<Action> mainThreadQueue = new Queue<Action>();
    private MqttClient mqttClient;
    private string myDeviceToken;
    private string statusLine = "Подключение...";
    private string pairingCode = string.Empty;
    private string resolvedStableIdentity = string.Empty;
    private float nextStateLogTime;
    private float nextAutoPublishTime;
    private float nextPresenceHeartbeatTime;
    private float pairingRequestSentAt;
    private string interactionLine = string.Empty;
    private bool isPairingRequestSent;
    private bool isSessionReady;
    private bool isPaired;

    public bool IsMqttConnected => mqttClient != null && mqttClient.IsConnected && isSessionReady;
    public string DisplayName => string.IsNullOrWhiteSpace(suggestedName) ? gameObject.name : suggestedName;
    public string CurrentStatusLine => statusLine;
    public string CurrentPairingCode => pairingCode;
    public string CurrentInteractionLine => interactionLine;
    public string CurrentStateSummary => BuildStateSummary();
    public string CurrentDataSummary => BuildDataSummary();
    public bool IsAlreadyPaired => isPaired;

    protected string BaseTopic => mqttTopic;
    protected abstract string DeviceTypeKey { get; }

    protected virtual void Awake()
    {
        resolvedStableIdentity = GetStableIdentityKey();
        mqttTopic = ResolveMqttTopic(resolvedStableIdentity);

        string tokenPrefsKey = BuildPrefsKey("AtlantToken_v5_", mqttTopic);
        if (PlayerPrefs.HasKey(tokenPrefsKey))
        {
            myDeviceToken = PlayerPrefs.GetString(tokenPrefsKey);
        }
        else
        {
            myDeviceToken = "tok_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            PlayerPrefs.SetString(tokenPrefsKey, myDeviceToken);
            PlayerPrefs.Save();
        }

        isPaired = LoadPairingState();
    }

    protected virtual void Start()
    {
        if (connectOnStart)
        {
            ConnectToBroker();
        }
    }

    protected virtual void Update()
    {
        lock (mainThreadQueue)
        {
            while (mainThreadQueue.Count > 0)
            {
                mainThreadQueue.Dequeue().Invoke();
            }
        }

        if (!IsMqttConnected)
        {
            return;
        }

        if (ShouldPublishStateHeartbeat() && autoPublishStateEverySeconds > 0f && Time.time >= nextAutoPublishTime)
        {
            nextAutoPublishTime = Time.time + autoPublishStateEverySeconds;
            PublishState();
        }

        if (
            ShouldPublishPresenceHeartbeat()
            && presenceHeartbeatEverySeconds > 0f
            && Time.time >= nextPresenceHeartbeatTime
        )
        {
            nextPresenceHeartbeatTime = Time.time + presenceHeartbeatEverySeconds;
            PublishAvailability(true);
        }

        if (Time.time >= nextStateLogTime)
        {
            nextStateLogTime = Time.time + logStateEverySeconds;
            LogFullState();
        }

        if (
            isPairingRequestSent
            && string.IsNullOrEmpty(pairingCode)
            && Time.time - pairingRequestSentAt >= PairingRequestTimeoutSeconds
        )
        {
            isPairingRequestSent = false;
            TriggerAutoPairingRequest();
        }
    }

    protected virtual void OnDestroy()
    {
        DisconnectFromBroker();
    }

    protected virtual void OnApplicationQuit()
    {
        DisconnectFromBroker();
    }

    public void ConnectToBroker()
    {
        if (mqttClient != null && mqttClient.IsConnected)
        {
            return;
        }

        try
        {
            isSessionReady = false;
            statusLine = "Подключение...";
            mqttClient = new MqttClient(brokerAddress, brokerPort, false, null, null, MqttSslProtocols.None);

            string clientId = $"Unity_{DeviceTypeKey}_{BuildClientIdSuffix()}";
            mqttClient.MqttMsgPublishReceived -= OnMessageReceivedInternal;
            mqttClient.MqttMsgPublishReceived += OnMessageReceivedInternal;

            if (!string.IsNullOrEmpty(mqttUsername))
            {
                mqttClient.Connect(clientId, mqttUsername, mqttPassword);
            }
            else
            {
                mqttClient.Connect(clientId);
            }

            if (mqttClient.IsConnected)
            {
                StartCoroutine(SafeInitializeSessionRoutine());
            }
        }
        catch (Exception ex)
        {
            statusLine = "Ошибка подключения";
            Debug.LogError($"[{DisplayName}] MQTT Connection error: {ex.Message}");
        }
    }

    private IEnumerator SafeInitializeSessionRoutine()
    {
        yield return new WaitForSeconds(0.4f);

        if (mqttClient != null && mqttClient.IsConnected)
        {
            SubscribeToTopics();
            isSessionReady = true;
            PublishAvailability(true);
            nextPresenceHeartbeatTime = Time.time + presenceHeartbeatEverySeconds;
            nextAutoPublishTime = Time.time + autoPublishStateEverySeconds;
            statusLine = "Подключено";
            PublishState();
            Debug.Log($"[{DisplayName}] MQTT session ready. Listening on: {BaseTopic}/set");
        }
    }

    public void DisconnectFromBroker()
    {
        if (mqttClient == null)
        {
            return;
        }

        try
        {
            mqttClient.MqttMsgPublishReceived -= OnMessageReceivedInternal;

            if (mqttClient.IsConnected)
            {
                PublishAvailability(false);
                mqttClient.Disconnect();
            }
        }
        catch
        {
        }
        finally
        {
            mqttClient = null;
            isSessionReady = false;
            statusLine = "Отключено";
        }
    }

    private string GetStableIdentityKey()
    {
        if (IsValidMqttTopic(mqttTopic))
        {
            return mqttTopic;
        }

        var builder = new StringBuilder();
        builder.Append(gameObject.scene.path);
        builder.Append('|');
        builder.Append(DeviceTypeKey);
        builder.Append('|');
        builder.Append(GetHierarchyPath(transform));
        builder.Append('|');
        builder.Append(GetTransformFingerprint());
        return builder.ToString();
    }

    private void SavePairingState(bool paired)
    {
        if (!persistPairingState || string.IsNullOrEmpty(resolvedStableIdentity))
        {
            return;
        }

        string pairingPrefsKey = BuildPrefsKey("AtlantPaired_v2_", resolvedStableIdentity);
        PlayerPrefs.SetInt(pairingPrefsKey, paired ? 1 : 0);
        PlayerPrefs.Save();
    }

    private bool LoadPairingState()
    {
        if (!persistPairingState || string.IsNullOrEmpty(resolvedStableIdentity))
        {
            return false;
        }

        string pairingPrefsKey = BuildPrefsKey("AtlantPaired_v2_", resolvedStableIdentity);
        return PlayerPrefs.GetInt(pairingPrefsKey, 0) == 1;
    }

    public void TriggerAutoPairingRequest()
    {
        if (isPairingRequestSent || isPaired || !string.IsNullOrEmpty(pairingCode))
        {
            return;
        }

        isPairingRequestSent = true;
        pairingRequestSentAt = Time.time;
        if (string.IsNullOrWhiteSpace(statusLine) || statusLine == "Подключено")
        {
            statusLine = "Подключено";
        }

        string uniqueRequestId = BuildRequestId();
        var payload = new JObject
        {
            ["action"] = "pair",
            ["request_id"] = uniqueRequestId,
            ["device_token"] = myDeviceToken,
            ["type"] = DeviceTypeKey,
            ["name"] = DisplayName,
        };

        PublishJson($"{BaseTopic}/request", payload);
    }

    public void ForceClearPairingCode()
    {
        pairingCode = string.Empty;
        isPairingRequestSent = false;
        isPaired = false;
        SavePairingState(false);
    }

    public abstract IReadOnlyList<AtlantDeviceActionHint> GetInteractionHints();
    public abstract void HandleInteractionKey(KeyCode key);
    public abstract void BeginInteractionAdjustment(KeyCode key);
    public abstract void AdjustInteractionValue(KeyCode key, float direction);
    public abstract void CommitInteractionAdjustment(KeyCode key);
    public virtual string GetPrimaryActionLabel(KeyCode key) => string.Empty;

    protected abstract void ApplyCommand(JObject command);
    protected abstract JObject BuildStatePayload();
    protected abstract string BuildStateSummary();
    protected abstract string BuildDataSummary();

    public void PublishState()
    {
        if (!IsMqttConnected)
        {
            return;
        }

        try
        {
            JObject payload = BuildStatePayload();
            PublishJson($"{BaseTopic}/state", payload);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{DisplayName}] State publish error: {ex.Message}");
        }
    }

    protected void SetInteractionLine(string text)
    {
        interactionLine = text;
    }

    private bool ShouldPublishStateHeartbeat()
    {
        return enableAutoStateHeartbeat || isPaired || !string.IsNullOrEmpty(pairingCode);
    }

    private bool ShouldPublishPresenceHeartbeat()
    {
        return enablePresenceHeartbeat || isPaired || !string.IsNullOrEmpty(pairingCode);
    }

    private void SubscribeToTopics()
    {
        if (mqttClient == null || !mqttClient.IsConnected)
        {
            return;
        }

        mqttClient.Subscribe(
            new[] { $"{BaseTopic}/set", $"{BaseTopic}/response" },
            new[] { MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE }
        );
    }

    private void OnMessageReceivedInternal(object sender, MqttMsgPublishEventArgs e)
    {
        string topic = e.Topic;
        string payloadStr = Encoding.UTF8.GetString(e.Message);

        if (topic != $"{BaseTopic}/response" && topic != $"{BaseTopic}/set")
        {
            return;
        }

        EnqueueOnMainThread(() =>
        {
            try
            {
                JObject json = JObject.Parse(payloadStr);

                if (topic == $"{BaseTopic}/response")
                {
                    HandlePairingResponse(json);
                }
                else
                {
                    JObject targetCommand = json;
                    if (
                        json["action"] == null
                        && json["payload"] != null
                        && json["payload"].Type == JTokenType.Object
                    )
                    {
                        targetCommand = (JObject)json["payload"];
                    }

                    ApplyCommand(targetCommand);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[{DisplayName}] Invalid MQTT JSON payload: {ex.Message}\nPayload: {payloadStr}"
                );
            }
        });
    }

    private void HandlePairingResponse(JObject response)
    {
        if (response == null)
        {
            return;
        }

        if (response["ok"] != null && response["ok"].Value<bool>() == false)
        {
            isPairingRequestSent = false;
            pairingRequestSentAt = 0f;
            string error = response["error"]?.Value<string>() ?? "Ошибка сопряжения";

            if (string.Equals(error, "Device already paired", StringComparison.OrdinalIgnoreCase))
            {
                pairingCode = string.Empty;
                isPaired = true;
                statusLine = "Уже сопряжено";
                SavePairingState(true);
                PublishAvailability(true);
                PublishState();
                return;
            }

            if (string.Equals(error, "Invalid device_token", StringComparison.OrdinalIgnoreCase))
            {
                error = "Некорректный токен устройства";
            }
            else if (string.Equals(error, "Invalid device type", StringComparison.OrdinalIgnoreCase))
            {
                error = "Некорректный тип устройства";
            }

            statusLine = error;
            pairingCode = string.Empty;
            isPaired = false;
            SavePairingState(false);
            return;
        }

        if (response["pairing_code"] != null)
        {
            pairingCode = response["pairing_code"].Value<string>();
            isPairingRequestSent = false;
            pairingRequestSentAt = 0f;
            isPaired = true;
            statusLine = "Код сопряжения получен";
            SavePairingState(true);
            PublishAvailability(true);
            PublishState();
        }
    }

    private void PublishAvailability(bool online)
    {
        if (mqttClient == null || !mqttClient.IsConnected)
        {
            return;
        }

        string payload = online ? "online" : "offline";
        mqttClient.Publish(
            $"{BaseTopic}/availability",
            Encoding.UTF8.GetBytes(payload),
            MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE,
            false
        );
    }

    private void PublishJson(string topic, JObject payload)
    {
        if (mqttClient == null || !mqttClient.IsConnected)
        {
            return;
        }

        string json = payload.ToString(Formatting.None);
        mqttClient.Publish(
            topic,
            Encoding.UTF8.GetBytes(json),
            MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE,
            false
        );
    }

    private string ResolveMqttTopic(string stableIdentity)
    {
        if (useSerializedMqttTopic && IsValidMqttTopic(mqttTopic))
        {
            return mqttTopic;
        }

        string topicPrefsKey = BuildPrefsKey("AtlantTopic_v6_", stableIdentity);
        if (PlayerPrefs.HasKey(topicPrefsKey))
        {
            string storedTopic = PlayerPrefs.GetString(topicPrefsKey);
            if (IsValidMqttTopic(storedTopic))
            {
                return storedTopic;
            }
        }

        string generatedTopic = BuildStableTopic(stableIdentity);
        PlayerPrefs.SetString(topicPrefsKey, generatedTopic);
        PlayerPrefs.Save();
        return generatedTopic;
    }

    private string BuildStableTopic(string stableIdentity)
    {
        using SHA256 hash = SHA256.Create();
        byte[] digest = hash.ComputeHash(Encoding.UTF8.GetBytes(stableIdentity));
        string suffix = BitConverter.ToString(digest, 0, 6).Replace("-", string.Empty).ToLowerInvariant();
        return $"devices/{DeviceTypeKey}-{suffix}";
    }

    private static bool IsValidMqttTopic(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic) || !topic.StartsWith("devices/", StringComparison.Ordinal))
        {
            return false;
        }

        return topic.IndexOf("/", "devices/".Length, StringComparison.Ordinal) < 0;
    }

    private static string GetHierarchyPath(Transform root)
    {
        var path = new Stack<string>();
        Transform current = root;
        while (current != null)
        {
            path.Push(CleanPathSegment(current.name) + "#" + current.GetSiblingIndex());
            current = current.parent;
        }

        return string.Join("/", path.ToArray());
    }

    private string GetTransformFingerprint()
    {
        Vector3 position = transform.position;
        Vector3 scale = transform.lossyScale;
        return string.Format(
            CultureInfo.InvariantCulture,
            "pos:{0:F3},{1:F3},{2:F3}|scale:{3:F3},{4:F3},{5:F3}",
            position.x,
            position.y,
            position.z,
            scale.x,
            scale.y,
            scale.z
        );
    }

    private static string CleanPathSegment(string value)
    {
        return value.Replace(" ", string.Empty).Replace("(Clone)", string.Empty);
    }

    private string BuildClientIdSuffix()
    {
        using SHA256 hash = SHA256.Create();
        byte[] digest = hash.ComputeHash(Encoding.UTF8.GetBytes(resolvedStableIdentity ?? GetStableIdentityKey()));
        return BitConverter.ToString(digest, 0, 6).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string BuildPrefsKey(string prefix, string identity)
    {
        using SHA256 hash = SHA256.Create();
        byte[] digest = hash.ComputeHash(Encoding.UTF8.GetBytes(identity));
        string suffix = BitConverter.ToString(digest, 0, 12).Replace("-", string.Empty).ToLowerInvariant();
        return prefix + suffix;
    }

    protected string BuildRequestId()
    {
        return "req-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    }

    private void EnqueueOnMainThread(Action action)
    {
        lock (mainThreadQueue)
        {
            mainThreadQueue.Enqueue(action);
        }
    }

    private void LogFullState()
    {
        Debug.Log($"[{DisplayName}] MQTT state | topic: {BaseTopic} | connected: {IsMqttConnected}");
    }
}
