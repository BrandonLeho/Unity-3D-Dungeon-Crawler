using UnityEngine;

public class DamageTextManager : MonoBehaviour
{
    public static DamageTextManager Instance { get; private set; }

    [Header("Wiring")]
    [SerializeField] Canvas overlayCanvas;                 // Screen Space – Overlay canvas
    [SerializeField] DamageText damageTextPrefab;  // TMP prefab
    [SerializeField] RectTransform normalRoot;
    [SerializeField] RectTransform critRoot;

    [Header("Defaults")]
    [SerializeField] float lifetime = 0.9f;
    [SerializeField] float growTime = 0.07f;
    [SerializeField] float holdTime = 0.05f;
    [SerializeField] float shrinkTime = 0.15f;
    [SerializeField] float startScale = 1.8f;
    [SerializeField] float endScale = 1.0f;
    [SerializeField] float critScaleMul = 1.35f;
    [SerializeField] Color normalColor = Color.white;
    [SerializeField] Color critColor = new Color(1f, 0.35f, 0.25f);
    [SerializeField] Vector2 screenJitter = new(20f, 14f);

    [Header("Spread vs Distance")]
    [SerializeField] AnimationCurve distanceToJitter = AnimationCurve.Linear(0f, 1f, 60f, 0.25f);

    Camera cachedCam;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (!overlayCanvas)
        {
            overlayCanvas = GameObject.Find("FloatingTextCanvas")?.GetComponent<Canvas>();
            if (!overlayCanvas) Debug.LogWarning("DamageTextManager: Assign an Overlay Canvas.");
        }
        if (!damageTextPrefab) Debug.LogWarning("DamageTextManager: Assign a DamageText prefab.");

        // Create roots if missing
        if (!normalRoot || !critRoot)
        {
            normalRoot = new GameObject("NormalRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            critRoot = new GameObject("CritRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            normalRoot.SetParent(overlayCanvas.transform, false);
            critRoot.SetParent(overlayCanvas.transform, false);
            normalRoot.SetSiblingIndex(0);
            critRoot.SetSiblingIndex(1);
        }
    }

    Camera GetActiveCamera()
    {
        if (cachedCam && cachedCam.isActiveAndEnabled) return cachedCam;
        cachedCam = Camera.main;
        if (!cachedCam && Camera.allCamerasCount > 0) cachedCam = Camera.allCameras[0];
        return cachedCam;
    }

    public void Spawn(Transform follow, Vector3 worldPos, int amount, bool isCrit)
    {
        if (!overlayCanvas || !damageTextPrefab) return;

        var parent = isCrit ? critRoot : normalRoot;
        var inst = Instantiate(damageTextPrefab, parent);

        var s = new DamageText.Settings
        {
            lifetime = lifetime,
            growTime = growTime,
            holdTime = holdTime,
            shrinkTime = shrinkTime,
            startScale = startScale,
            endScale = endScale,
            critScaleMul = critScaleMul,
            normalColor = normalColor,
            critColor = critColor,
            randomScreenJitter = screenJitter
        };

        inst.Init(GetActiveCamera(), follow, worldPos, amount, isCrit, s, d => distanceToJitter.Evaluate(d));
    }
}
