using UnityEngine;

[DefaultExecutionOrder(+20)]
public class ProceduralGripAnimator : MonoBehaviour
{
    [Header("References")]
    public EquipmentController equipmentController;
    public ProceduralIdleAnimator idleAnimator;

    [Header("Polling Failsafe")]
    public bool pollActiveEachFrame = true;

    [Header("Defaults (Idle Snapshot)")]
    public Vector3 idle_upperArmR, idle_lowerArmR, idle_handR;
    public Vector3 idle_upperArmL, idle_lowerArmL, idle_handL;

    [Range(0f, 1f)] public float idle_fingerCurl = 0f;
    [Range(0f, 1f)] public float idle_fingerSpread = 0f;

    // Finger parameters snapshot (so we can restore)
    float idle_idleCurlDeg, idle_idleHz, idle_maxCurlDeg, idle_falloff, idle_rotLerp, idle_microDeg;
    float idle_idleSpreadDeg, idle_maxSpreadDeg, idle_spreadRotLerp;
    ProceduralIdleAnimator.FingerAxis idle_spreadAxis;
    bool idle_mirrorRightHand;

    [Header("Blend Controls")]
    [Range(1f, 30f)] public float defaultBlendSpeed = 12f;

    [Header("Item Offsets")]
    public bool applyItemOffsets = true;
    public bool reapplyOffsetsContinuously = false;

    // targets
    Vector3 _target_uArmR, _target_lArmR, _target_handR;
    Vector3 _target_uArmL, _target_lArmL, _target_handL;
    float _target_fingerCurl, _target_fingerSpread;

    // finger param targets (override-aware)
    bool _useOverrides;
    float _t_idleCurlDeg, _t_idleHz, _t_maxCurlDeg, _t_falloff, _t_rotLerp, _t_microDeg;
    float _t_idleSpreadDeg, _t_maxSpreadDeg, _t_spreadRotLerp;
    ProceduralIdleAnimator.FingerAxis _t_spreadAxis;
    bool _t_mirrorRight;

    // active
    HandEquipment _lastItem;
    Transform _activeItemTr;
    HandEquipmentPose _activePose;
    float _equipBlendSpeed, _unequipBlendSpeed;

    void Reset()
    {
        if (!idleAnimator) idleAnimator = GetComponent<ProceduralIdleAnimator>();
        if (!equipmentController) equipmentController = GetComponentInParent<EquipmentController>();
    }

    void Awake()
    {
        if (!idleAnimator) idleAnimator = GetComponent<ProceduralIdleAnimator>();
        if (!equipmentController) equipmentController = GetComponentInParent<EquipmentController>();

        if (idleAnimator)
        {
            // Arms
            idle_upperArmR = idleAnimator.upperArmR_BaseEuler;
            idle_lowerArmR = idleAnimator.lowerArmR_BaseEuler;
            idle_handR = idleAnimator.handR_BaseEuler;
            idle_upperArmL = idleAnimator.upperArmL_BaseEuler;
            idle_lowerArmL = idleAnimator.lowerArmL_BaseEuler;
            idle_handL = idleAnimator.handL_BaseEuler;

            // Finger inputs
            idle_fingerCurl = idleAnimator.fingerCurlInput;
            idle_fingerSpread = idleAnimator.fingerSpreadInput;

            // Finger params
            idle_idleCurlDeg = idleAnimator.fingerIdleCurlDeg;
            idle_idleHz = idleAnimator.fingerIdleHz;
            idle_maxCurlDeg = idleAnimator.fingerMaxCurlDeg;
            idle_falloff = idleAnimator.fingerFalloff;
            idle_rotLerp = idleAnimator.fingerRotLerp;
            idle_microDeg = idleAnimator.fingerMicroDeg;

            idle_idleSpreadDeg = idleAnimator.fingerIdleSpreadDeg;
            idle_maxSpreadDeg = idleAnimator.fingerMaxSpreadDeg;
            idle_spreadRotLerp = idleAnimator.fingerSpreadRotLerp;
            idle_spreadAxis = idleAnimator.fingerSpreadAxis;
            idle_mirrorRightHand = idleAnimator.mirrorRightHand;
        }

        SetTargetsToIdle();
    }

    void OnEnable()
    {
        if (equipmentController != null)
        {
            equipmentController.OnItemEquipped += HandleEquipped;
            equipmentController.OnItemUnequipped += HandleUnequipped;
        }
        RefreshFromController();
    }

    void OnDisable()
    {
        if (equipmentController != null)
        {
            equipmentController.OnItemEquipped -= HandleEquipped;
            equipmentController.OnItemUnequipped -= HandleUnequipped;
        }
    }

    void HandleEquipped(int index, HandEquipment item) => ApplyFromItem(item);
    void HandleUnequipped(int index, HandEquipment item) { if (item == _lastItem) ClearPose(); }

    void RefreshFromController() => ApplyFromItem(equipmentController ? equipmentController.ActiveItem : null);

    void ApplyFromItem(HandEquipment item)
    {
        _lastItem = item;
        if (!item) { ClearPose(); return; }

        var comp = item as Component;
        var pose = comp ? comp.GetComponentInChildren<HandEquipmentPose>(true) : null;
        ApplyPose(pose, comp ? comp.transform : null);
    }

    void ApplyPose(HandEquipmentPose pose, Transform itemTr)
    {
        _activePose = pose;
        _activeItemTr = itemTr;

        if (!pose) { ClearPose(); return; }

        // Arms
        _target_uArmR = pose.upperArmR_BaseEuler; _target_lArmR = pose.lowerArmR_BaseEuler; _target_handR = pose.handR_BaseEuler;
        if (pose.useLeftHand)
        {
            _target_uArmL = pose.upperArmL_BaseEuler; _target_lArmL = pose.lowerArmL_BaseEuler; _target_handL = pose.handL_BaseEuler;
        }
        else
        {
            _target_uArmL = idle_upperArmL; _target_lArmL = idle_lowerArmL; _target_handL = idle_handL;
        }

        // Finger inputs
        _target_fingerCurl = Mathf.Clamp01(pose.fingerCurl);
        _target_fingerSpread = Mathf.Clamp01(pose.fingerSpread);

        // Overrides
        _useOverrides = pose.overrideFingerSettings;
        if (_useOverrides)
        {
            _t_idleCurlDeg = pose.fingerIdleCurlDeg;
            _t_idleHz = pose.fingerIdleHz;
            _t_maxCurlDeg = pose.fingerMaxCurlDeg;
            _t_falloff = pose.fingerFalloff;
            _t_rotLerp = pose.fingerRotLerp;
            _t_microDeg = pose.fingerMicroDeg;

            _t_idleSpreadDeg = pose.fingerIdleSpreadDeg;
            _t_maxSpreadDeg = pose.fingerMaxSpreadDeg;
            _t_spreadRotLerp = pose.fingerSpreadRotLerp;
            // map enum
            _t_spreadAxis = (ProceduralIdleAnimator.FingerAxis)System.Enum.Parse(
                typeof(ProceduralIdleAnimator.FingerAxis), pose.fingerSpreadAxis.ToString());
            _t_mirrorRight = pose.mirrorRightHand;
        }
        else
        {
            // targets fall back to idle snapshot
            _t_idleCurlDeg = idle_idleCurlDeg;
            _t_idleHz = idle_idleHz;
            _t_maxCurlDeg = idle_maxCurlDeg;
            _t_falloff = idle_falloff;
            _t_rotLerp = idle_rotLerp;
            _t_microDeg = idle_microDeg;

            _t_idleSpreadDeg = idle_idleSpreadDeg;
            _t_maxSpreadDeg = idle_maxSpreadDeg;
            _t_spreadRotLerp = idle_spreadRotLerp;
            _t_spreadAxis = idle_spreadAxis;
            _t_mirrorRight = idle_mirrorRightHand;
        }

        _equipBlendSpeed = Mathf.Max(1f, pose.equipBlendSpeed);
        _unequipBlendSpeed = Mathf.Max(1f, pose.unequipBlendSpeed);
    }

    void ClearPose()
    {
        _activePose = null; _activeItemTr = null;
        SetTargetsToIdle();
    }

    void SetTargetsToIdle()
    {
        _target_uArmR = idle_upperArmR; _target_lArmR = idle_lowerArmR; _target_handR = idle_handR;
        _target_uArmL = idle_upperArmL; _target_lArmL = idle_lowerArmL; _target_handL = idle_handL;
        _target_fingerCurl = idle_fingerCurl;
        _target_fingerSpread = idle_fingerSpread;

        _t_idleCurlDeg = idle_idleCurlDeg; _t_idleHz = idle_idleHz; _t_maxCurlDeg = idle_maxCurlDeg;
        _t_falloff = idle_falloff; _t_rotLerp = idle_rotLerp; _t_microDeg = idle_microDeg;

        _t_idleSpreadDeg = idle_idleSpreadDeg; _t_maxSpreadDeg = idle_maxSpreadDeg;
        _t_spreadRotLerp = idle_spreadRotLerp; _t_spreadAxis = idle_spreadAxis; _t_mirrorRight = idle_mirrorRightHand;

        _equipBlendSpeed = defaultBlendSpeed; _unequipBlendSpeed = defaultBlendSpeed;
        _useOverrides = false;
    }

    void LateUpdate()
    {
        // Failsafe poll
        if (pollActiveEachFrame && equipmentController)
        {
            var cur = equipmentController.ActiveItem;
            if (cur != _lastItem) ApplyFromItem(cur);
        }
        if (!idleAnimator) return;

        float dt = Time.deltaTime;
        float speed = (_activePose != null) ? _equipBlendSpeed : _unequipBlendSpeed;
        float k = 1f - Mathf.Exp(-speed * dt);

        // Arms
        idleAnimator.upperArmR_BaseEuler = Vector3.Lerp(idleAnimator.upperArmR_BaseEuler, _target_uArmR, k);
        idleAnimator.lowerArmR_BaseEuler = Vector3.Lerp(idleAnimator.lowerArmR_BaseEuler, _target_lArmR, k);
        idleAnimator.handR_BaseEuler = Vector3.Lerp(idleAnimator.handR_BaseEuler, _target_handR, k);

        idleAnimator.upperArmL_BaseEuler = Vector3.Lerp(idleAnimator.upperArmL_BaseEuler, _target_uArmL, k);
        idleAnimator.lowerArmL_BaseEuler = Vector3.Lerp(idleAnimator.lowerArmL_BaseEuler, _target_lArmL, k);
        idleAnimator.handL_BaseEuler = Vector3.Lerp(idleAnimator.handL_BaseEuler, _target_handL, k);

        // Finger inputs
        idleAnimator.fingerCurlInput = Mathf.Lerp(idleAnimator.fingerCurlInput, _target_fingerCurl, k);
        idleAnimator.fingerSpreadInput = Mathf.Lerp(idleAnimator.fingerSpreadInput, _target_fingerSpread, k);

        // Finger parameter overrides (blend numeric params for smooth handovers)
        idleAnimator.fingerIdleCurlDeg = Mathf.Lerp(idleAnimator.fingerIdleCurlDeg, _t_idleCurlDeg, k);
        idleAnimator.fingerIdleHz = Mathf.Lerp(idleAnimator.fingerIdleHz, _t_idleHz, k);
        idleAnimator.fingerMaxCurlDeg = Mathf.Lerp(idleAnimator.fingerMaxCurlDeg, _t_maxCurlDeg, k);
        idleAnimator.fingerFalloff = Mathf.Lerp(idleAnimator.fingerFalloff, _t_falloff, k);
        idleAnimator.fingerRotLerp = Mathf.Lerp(idleAnimator.fingerRotLerp, _t_rotLerp, k);
        idleAnimator.fingerMicroDeg = Mathf.Lerp(idleAnimator.fingerMicroDeg, _t_microDeg, k);

        idleAnimator.fingerIdleSpreadDeg = Mathf.Lerp(idleAnimator.fingerIdleSpreadDeg, _t_idleSpreadDeg, k);
        idleAnimator.fingerMaxSpreadDeg = Mathf.Lerp(idleAnimator.fingerMaxSpreadDeg, _t_maxSpreadDeg, k);
        idleAnimator.fingerSpreadRotLerp = Mathf.Lerp(idleAnimator.fingerSpreadRotLerp, _t_spreadRotLerp, k);
        idleAnimator.fingerSpreadAxis = _t_spreadAxis;          // enum: discrete (switch is fine)
        idleAnimator.mirrorRightHand = _t_mirrorRight;         // bool: discrete
    }

    public void ForceRefresh() => RefreshFromController();
}
