using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(+70)]
public class FingerCurlAnimator : MonoBehaviour
{
    [Header("References")]
    public EquipmentController equipmentController;
    [Tooltip("Searches on enable if null.")] public Transform leftHandRoot;
    [Tooltip("Searches on enable if null.")] public Transform rightHandRoot;

    [Header("Finger Bones (Left)")]
    public List<Transform> fingerBonesL = new List<Transform>();

    [Header("Finger Bones (Right)")]
    public List<Transform> fingerBonesR = new List<Transform>();

    [Header("Idle Curl")]
    [Range(0f, 6f)] public float idleCurlDeg = 3.0f;
    [Range(0f, 3f)] public float idleHz = 0.25f;
    [Range(0f, 2f)] public float perlinJitterDeg = 0.6f;
    [Range(0f, 1f)] public float distalFalloff = 0.65f;

    [Header("Grip Curl")]
    [Range(0f, 90f)] public float maxGripCurlDeg = 55f;
    [Range(1f, 40f)] public float gripLerp = 14f;

    [Header("Smoothing")]
    [Range(1f, 40f)] public float rotLerp = 18f;

    List<Quaternion> _baseL = new List<Quaternion>();
    List<Quaternion> _baseR = new List<Quaternion>();

    float _seedL, _seedR;
    float _targetGrip;
    float _smGrip;

    void Reset()
    {
        if (!equipmentController) equipmentController = GetComponentInParent<EquipmentController>();
    }

    void OnEnable()
    {
        CacheBaselines();
        _seedL = Random.Range(0f, 1000f);
        _seedR = Random.Range(0f, 1000f);
        HookEquipment();
        RefreshFromActiveItem();
    }

    void OnDisable() => UnhookEquipment();

    void HookEquipment()
    {
        if (!equipmentController) return;
        equipmentController.OnItemEquipped += OnEquipped;
        equipmentController.OnItemUnequipped += OnUnequipped;
    }

    void UnhookEquipment()
    {
        if (!equipmentController) return;
        equipmentController.OnItemEquipped -= OnEquipped;
        equipmentController.OnItemUnequipped -= OnUnequipped;
    }

    void OnEquipped(int index, HandEquipment item) => _targetGrip = ReadGripFromItem(item);

    void OnUnequipped(int index, HandEquipment item) => _targetGrip = 0f;

    void RefreshFromActiveItem()
    {
        if (!equipmentController) { _targetGrip = 0f; return; }
        _targetGrip = ReadGripFromItem(equipmentController.ActiveItem);
    }

    float ReadGripFromItem(HandEquipment item)
    {
        if (item == null) return 0f;
        var c = (item as Component);
        if (!c) return 0f;
        var pose = c.GetComponentInChildren<HandEquipmentPose>(true);
        return pose ? Mathf.Clamp01(pose.fingerCurl) : 0f;
    }

    void CacheBaselines()
    {
        _baseL.Clear();
        for (int i = 0; i < fingerBonesL.Count; i++)
            _baseL.Add(fingerBonesL[i] ? fingerBonesL[i].localRotation : Quaternion.identity);

        _baseR.Clear();
        for (int i = 0; i < fingerBonesR.Count; i++)
            _baseR.Add(fingerBonesR[i] ? fingerBonesR[i].localRotation : Quaternion.identity);
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        _smGrip = Mathf.Lerp(_smGrip, _targetGrip, 1f - Mathf.Exp(-gripLerp * dt));

        float phase = Mathf.Sin(Time.time * idleHz * Mathf.PI * 2f);
        float idle = phase * idleCurlDeg;
        float grip = _smGrip * maxGripCurlDeg;

        AnimateSet(fingerBonesL, _baseL, idle, grip, _seedL);
        AnimateSet(fingerBonesR, _baseR, idle, grip, _seedR);
    }

    void AnimateSet(List<Transform> bones, List<Quaternion> bases, float idleDeg, float gripDeg, float seed)
    {
        if (bones == null || bases == null) return;
        float dt = Time.deltaTime;

        for (int i = 0; i < bones.Count && i < bases.Count; i++)
        {
            var t = bones[i];
            if (!t) continue;

            float fall = Mathf.Pow(distalFalloff, i);
            float jit = (Mathf.PerlinNoise(Time.time * 0.9f, seed + i * 0.31f) - 0.5f) * perlinJitterDeg;
            float curl = (idleDeg + gripDeg) * fall + jit;

            Quaternion target = bases[i] * Quaternion.Euler(curl, 0f, 0f);
            t.localRotation = Quaternion.Slerp(t.localRotation, target, 1f - Mathf.Exp(-rotLerp * dt));
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Capture Baselines (use current pose)")]
    void EditorCapture()
    {
        CacheBaselines();
        Debug.Log("[FingerCurlAnimator] Captured current finger baselines.");
    }
#endif
}
