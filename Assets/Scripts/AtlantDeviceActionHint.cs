using UnityEngine;

[System.Serializable]
public readonly struct AtlantDeviceActionHint
{
    public AtlantDeviceActionHint(KeyCode key, string action, bool adjustable = false)
    {
        Key = key;
        Action = action;
        Adjustable = adjustable;
    }

    public KeyCode Key { get; }
    public string Action { get; }
    public bool Adjustable { get; }
}
