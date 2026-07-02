using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class AtlantDeviceHoverInteractor : MonoBehaviour
{
    [SerializeField] private AtlantMqttDeviceBase device;
    [SerializeField] private AtlantDeviceTooltipUI tooltip;
    [SerializeField] private bool createTooltipIfMissing = true;

    [Header("Визуальное выделение при наведении")]
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private Color hoverColorMultiplier = new Color(1.2f, 1.2f, 1.2f, 1.0f);

    private Color originalColor = Color.white;
    private MaterialPropertyBlock propertyBlock;
    private bool isHovered;
    private GameObject worldHintInstance;

    private void Awake()
    {
        if (device == null) device = GetComponent<AtlantMqttDeviceBase>();

        if (tooltip == null && createTooltipIfMissing)
        {
            tooltip = AtlantDeviceTooltipBootstrap.GetOrCreate();
        }

        if (targetRenderer == null) targetRenderer = GetComponentInChildren<Renderer>();

        propertyBlock = new MaterialPropertyBlock();

        if (targetRenderer != null && targetRenderer.sharedMaterial != null && targetRenderer.sharedMaterial.HasProperty("_Color"))
        {
            originalColor = targetRenderer.sharedMaterial.color;
        }

        if (GetComponent<Collider>() == null)
        {
            Debug.LogWarning($"[{gameObject.name}] Добавьте Collider, иначе объект невозможно будет обнаружить.");
        }
    }

    private void Update()
    {
        if (!isHovered || device == null || tooltip == null) return;

        if (tooltip.IsOpen)
        {
            DestroyWorldHint();
        }

        if (Input.GetKeyDown(KeyCode.E) && !tooltip.IsOpen)
        {
            tooltip.Show(device);
            DestroyWorldHint();
        }

        if (worldHintInstance != null && Camera.main != null)
        {
            worldHintInstance.transform.LookAt(worldHintInstance.transform.position + Camera.main.transform.rotation * Vector3.forward, Camera.main.transform.rotation * Vector3.up);
        }
    }

    private void OnMouseEnter()
    {
        if (tooltip != null && tooltip.IsOpen) return;

        isHovered = true;

        if (targetRenderer != null && targetRenderer.sharedMaterial != null && targetRenderer.sharedMaterial.HasProperty("_Color"))
        {
            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor("_Color", originalColor * hoverColorMultiplier);
            targetRenderer.SetPropertyBlock(propertyBlock);
        }

        CreateWorldHint();
    }

    private void OnMouseExit()
    {
        isHovered = false;

        if (targetRenderer != null && targetRenderer.sharedMaterial != null && targetRenderer.sharedMaterial.HasProperty("_Color"))
        {
            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor("_Color", originalColor);
            targetRenderer.SetPropertyBlock(propertyBlock);
        }

        DestroyWorldHint();
    }

    private void CreateWorldHint()
    {
        if (worldHintInstance != null) return;

        worldHintInstance = new GameObject("World_E_InteractionHint");

        Vector3 spawnPos = transform.position;
        if (TryGetComponent<Collider>(out var col))
        {
            spawnPos = col.bounds.center + Vector3.up * (col.bounds.extents.y + 0.25f);
        }
        else
        {
            spawnPos += Vector3.up * 1.0f;
        }

        worldHintInstance.transform.position = spawnPos;
        worldHintInstance.transform.localScale = Vector3.one * 0.4f;

        TextMeshPro textMesh = worldHintInstance.AddComponent<TextMeshPro>();
        textMesh.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        textMesh.text = "<color=#00E5FF>[E]</color> Нажмите, чтобы открыть";
        textMesh.fontSize = 5;
        textMesh.alignment = TextAlignmentOptions.Center;
        textMesh.color = Color.white;

        Material textMaterial = textMesh.fontMaterial;
        textMaterial.EnableKeyword("GOW_ON");

        if (textMaterial.HasProperty("_GlowColor"))
            textMaterial.SetColor("_GlowColor", new Color(0f, 0.45f, 0.85f, 0.6f));

        if (textMaterial.HasProperty("_GlowOffset"))
            textMaterial.SetFloat("_GlowOffset", 0.15f);

        if (textMaterial.HasProperty("_GlowPower"))
            textMaterial.SetFloat("_GlowPower", 0.3f);
    }

    private void DestroyWorldHint()
    {
        if (worldHintInstance != null)
        {
            Destroy(worldHintInstance);
            worldHintInstance = null;
        }
    }

    private void OnDestroy()
    {
        if (targetRenderer != null)
        {
            targetRenderer.SetPropertyBlock(null);
        }
        DestroyWorldHint();
    }
}
