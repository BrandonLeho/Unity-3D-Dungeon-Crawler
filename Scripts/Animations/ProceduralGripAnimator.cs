using UnityEngine;

[DefaultExecutionOrder(+20)]
public class ProceduralGripAnimator : MonoBehaviour
{
    [Header("References")]
    public EquipmentController equipmentController;
    public ProceduralIdleAnimator idleAnimator;
    public ProceduralFingerAnimator fingerAnimator;

    [Header("Polling Failsafe")]
    public bool pollActiveEachFrame = true;

    [Header("Defaults (Idle Snapshot)")]
    public Vector3 idle_upperArmR, idle_lowerArmR, idle_handR;
    public Vector3 idle_upperArmL, idle_lowerArmL, idle_handL;

    [Header("Finger Defaults (Idle Snapshot)")]
    [Range(0f, 1f)] public float idle_leftCurl = 0f, idle_leftSpread = 0f;
    [Range(0f, 1f)] public float idle_rightCurl = 0f, idle_rightSpread = 0f;

    // finger param snapshots
    float idle_idleCurlDeg, idle_idleHz, idle_maxCurlDeg, idle_falloff, idle_curlLerp, idle_microDeg;
    float idle_thumbFalloff;
    float idle_idleSpreadDeg, idle_maxSpreadDeg, idle_spreadLerp;
    ProceduralFingerAnimator.Axis idle_spreadAxis;
    bool idle_mirrorRightSpread;
    Vector3 idle_thumbMaxCurlL, idle_thumbMaxCurlR;

    [Header("Blend Controls")]
    [Range(1f, 30f)] public float defaultBlendSpeed = 12f;

    [Header("Item Offsets")]
    public bool applyItemOffsets = true;
    public bool reapplyOffsetsContinuously = false;

    // targets
    Vector3 _target_uArmR, _target_lArmR, _target_handR;
    Vector3 _target_uArmL, _target_lArmL, _target_handL;

    float _t_leftCurl, _t_leftSpread, _t_rightCurl, _t_rightSpread;

    // param targets
    bool _useOverrides;
    float _t_idleCurlDeg, _t_idleHz, _t_maxCurlDeg, _t_falloff, _t_curlLerp, _t_microDeg;
    float _t_thumbFalloff;
    float _t_idleSpreadDeg, _t_maxSpreadDeg, _t_spreadLerp;
    ProceduralFingerAnimator.Axis _t_spreadAxis;
    bool _t_mirrorRightSpread;
    Vector3 _t_thumbMaxCurlL, _t_thumbMaxCurlR;

    // active item
    HandEquipment _lastItem;
    Transform _activeItemTr;
    HandEquipmentPose _activePose;
    float _equipBlendSpeed, _unequipBlendSpeed;

    void Reset()
    {
        if (!idleAnimator) idleAnimator = GetComponent<ProceduralIdleAnimator>();
        if (!fingerAnimator) fingerAnimator = GetComponent<ProceduralFingerAnimator>();
        if (!equipmentController) equipmentController = GetComponentInParent<EquipmentController>();
    }

    void Awake()
    {
        if (!idleAnimator) idleAnimator = GetComponent<ProceduralIdleAnimator>();
        if (!fingerAnimator) fingerAnimator = GetComponent<ProceduralFingerAnimator>();
        if (!equipmentController) equipmentController = GetComponentInParent<EquipmentController>();

        if (idleAnimator)
        {
            idle_upperArmR = idleAnimator.upperArmR_BaseEuler;
            idle_lowerArmR = idleAnimator.lowerArmR_BaseEuler;
            idle_handR = idleAnimator.handR_BaseEuler;
            idle_upperArmL = idleAnimator.upperArmL_BaseEuler;
            idle_lowerArmL = idleAnimator.lowerArmL_BaseEuler;
            idle_handL = idleAnimator.handL_BaseEuler;
        }

        if (fingerAnimator)
        {
            idle_leftCurl = fingerAnimator.leftCurl;
            idle_leftSpread = fingerAnimator.leftSpread;
            idle_rightCurl = fingerAnimator.rightCurl;
            idle_rightSpread = fingerAnimator.rightSpread;

            idle_idleCurlDeg = fingerAnimator.idleCurlDeg;
            idle_idleHz = fingerAnimator.idleCurlHz;
            idle_maxCurlDeg = fingerAnimator.maxCurlDeg;
            idle_falloff = fingerAnimator.distalFalloff;
            idle_curlLerp = fingerAnimator.curlLerp;
            idle_microDeg = fingerAnimator.microCurlDeg;

            idle_thumbFalloff = fingerAnimator.thumbDistalFalloff;
            idle_thumbMaxCurlL = fingerAnimator.leftThumbMaxCurlDegXYZ;
            idle_thumbMaxCurlR = fingerAnimator.rightThumbMaxCurlDegXYZ;

            idle_idleSpreadDeg = fingerAnimator.idleSpreadDeg;
            idle_maxSpreadDeg = fingerAnimator.maxSpreadDeg;
            idle_spreadLerp = fingerAnimator.spreadLerp;
            idle_spreadAxis = fingerAnimator.spreadAxis;
            idle_mirrorRightSpread = fingerAnimator.mirrorRightSpread;
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

        // Inputs
        if (pose.usePerHandFingerInputs)
        {
            _t_leftCurl = Mathf.Clamp01(pose.leftFingerCurl);
            _t_leftSpread = Mathf.Clamp01(pose.leftFingerSpread);
            _t_rightCurl = Mathf.Clamp01(pose.rightFingerCurl);
            _t_rightSpread = Mathf.Clamp01(pose.rightFingerSpread);
        }
        else
        {
            float c = Mathf.Clamp01(pose.fingerCurl);
            float s = Mathf.Clamp01(pose.fingerSpread);
            _t_leftCurl = _t_rightCurl = c;
            _t_leftSpread = _t_rightSpread = s;
        }

        // Overrides
        _useOverrides = pose.overrideFingerSettings;
        if (_useOverrides)
        {
            _t_idleCurlDeg = pose.fingerIdleCurlDeg;
            _t_idleHz = pose.fingerIdleHz;
            _t_maxCurlDeg = pose.fingerMaxCurlDeg;
            _t_falloff = pose.fingerFalloff;
            _t_curlLerp = pose.fingerRotLerp;
            _t_microDeg = pose.fingerMicroDeg;

            _t_thumbFalloff = pose.thumbFalloff;
            _t_thumbMaxCurlL = pose.leftThumbMaxCurlDegXYZ;
            _t_thumbMaxCurlR = pose.rightThumbMaxCurlDegXYZ;

            _t_idleSpreadDeg = pose.fingerIdleSpreadDeg;
            _t_maxSpreadDeg = pose.fingerMaxSpreadDeg;
            _t_spreadLerp = pose.fingerSpreadRotLerp;
            _t_spreadAxis = (ProceduralFingerAnimator.Axis)System.Enum.Parse(
                typeof(ProceduralFingerAnimator.Axis), pose.fingerSpreadAxis.ToString());
            _t_mirrorRightSpread = pose.mirrorRightHand;

            _equipBlendSpeed = Mathf.Max(1f, pose.equipBlendSpeed);
            _unequipBlendSpeed = Mathf.Max(1f, pose.unequipBlendSpeed);
        }
        else
        {
            _t_idleCurlDeg = idle_idleCurlDeg; _t_idleHz = idle_idleHz; _t_maxCurlDeg = idle_maxCurlDeg;
            _t_falloff = idle_falloff; _t_curlLerp = idle_curlLerp; _t_microDeg = idle_microDeg;

            _t_thumbFalloff = idle_thumbFalloff;
            _t_thumbMaxCurlL = idle_thumbMaxCurlL;
            _t_thumbMaxCurlR = idle_thumbMaxCurlR;

            _t_idleSpreadDeg = idle_idleSpreadDeg; _t_maxSpreadDeg = idle_maxSpreadDeg;
            _t_spreadLerp = idle_spreadLerp; _t_spreadAxis = idle_spreadAxis;
            _t_mirrorRightSpread = idle_mirrorRightSpread;

            _equipBlendSpeed = _unequipBlendSpeed = defaultBlendSpeed;
        }
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

        _t_leftCurl = idle_leftCurl; _t_leftSpread = idle_leftSpread;
        _t_rightCurl = idle_rightCurl; _t_rightSpread = idle_rightSpread;

        _t_idleCurlDeg = idle_idleCurlDeg; _t_idleHz = idle_idleHz; _t_maxCurlDeg = idle_maxCurlDeg;
        _t_falloff = idle_falloff; _t_curlLerp = idle_curlLerp; _t_microDeg = idle_microDeg;

        _t_thumbFalloff = idle_thumbFalloff;
        _t_thumbMaxCurlL = idle_thumbMaxCurlL;
        _t_thumbMaxCurlR = idle_thumbMaxCurlR;

        _t_idleSpreadDeg = idle_idleSpreadDeg; _t_maxSpreadDeg = idle_maxSpreadDeg;
        _t_spreadLerp = idle_spreadLerp; _t_spreadAxis = idle_spreadAxis;
        _t_mirrorRightSpread = idle_mirrorRightSpread;

        _equipBlendSpeed = _unequipBlendSpeed = defaultBlendSpeed;
        _useOverrides = false;
    }

    void LateUpdate()
    {
        if (pollActiveEachFrame && equipmentController)
        {
            var cur = equipmentController.ActiveItem;
            if (cur != _lastItem) ApplyFromItem(cur);
        }
        if (!idleAnimator || !fingerAnimator) return;

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
        fingerAnimator.leftCurl = Mathf.Lerp(fingerAnimator.leftCurl, _t_leftCurl, k);
        fingerAnimator.leftSpread = Mathf.Lerp(fingerAnimator.leftSpread, _t_leftSpread, k);
        fingerAnimator.rightCurl = Mathf.Lerp(fingerAnimator.rightCurl, _t_rightCurl, k);
        fingerAnimator.rightSpread = Mathf.Lerp(fingerAnimator.rightSpread, _t_rightSpread, k);

        // Finger params
        fingerAnimator.idleCurlDeg = Mathf.Lerp(fingerAnimator.idleCurlDeg, _t_idleCurlDeg, k);
        fingerAnimator.idleCurlHz = Mathf.Lerp(fingerAnimator.idleCurlHz, _t_idleHz, k);
        fingerAnimator.maxCurlDeg = Mathf.Lerp(fingerAnimator.maxCurlDeg, _t_maxCurlDeg, k);
        fingerAnimator.distalFalloff = Mathf.Lerp(fingerAnimator.distalFalloff, _t_falloff, k);
        fingerAnimator.curlLerp = Mathf.Lerp(fingerAnimator.curlLerp, _t_curlLerp, k);
        fingerAnimator.microCurlDeg = Mathf.Lerp(fingerAnimator.microCurlDeg, _t_microDeg, k);

        fingerAnimator.thumbDistalFalloff = Mathf.Lerp(fingerAnimator.thumbDistalFalloff, _t_thumbFalloff, k);

        fingerAnimator.leftThumbMaxCurlDegXYZ = Vector3.Lerp(fingerAnimator.leftThumbMaxCurlDegXYZ, _t_thumbMaxCurlL, k);
        fingerAnimator.rightThumbMaxCurlDegXYZ = Vector3.Lerp(fingerAnimator.rightThumbMaxCurlDegXYZ, _t_thumbMaxCurlR, k);

        fingerAnimator.idleSpreadDeg = Mathf.Lerp(fingerAnimator.idleSpreadDeg, _t_idleSpreadDeg, k);
        fingerAnimator.maxSpreadDeg = Mathf.Lerp(fingerAnimator.maxSpreadDeg, _t_maxSpreadDeg, k);
        fingerAnimator.spreadLerp = Mathf.Lerp(fingerAnimator.spreadLerp, _t_spreadLerp, k);
        fingerAnimator.spreadAxis = _t_spreadAxis;
        fingerAnimator.mirrorRightSpread = _t_mirrorRightSpread;
    }

    public void ForceRefresh() => RefreshFromController();
}
