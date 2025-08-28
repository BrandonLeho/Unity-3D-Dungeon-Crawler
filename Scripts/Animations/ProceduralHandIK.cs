using UnityEngine;

[DefaultExecutionOrder(+90)]
public class ProceduralHandIK : MonoBehaviour
{
    [Header("References")]
    public EquipmentController equipmentController;

    [Header("Right Arm Bones")]
    public Transform upperArmR;
    public Transform lowerArmR;
    public Transform handR;

    [Header("Left Arm Bones")]
    public Transform upperArmL;
    public Transform lowerArmL;
    public Transform handL;

    [Header("General")]
    [Tooltip("How quickly IK weights blend when items change.")]
    [Range(1f, 40f)] public float weightLerp = 18f;
    [Tooltip("Softens reach when the target is beyond the chain length (0=no soften, 1=more soften).")]
    [Range(0f, 1f)] public float reachSoften = 0.2f;

    // cached bone lengths (kept constant)
    float _lenUR, _lenLR, _lenUL, _lenLL;
    bool _hasR, _hasL;

    // smoothed IK weights
    float _wPosR, _wRotR, _wPosL, _wRotL;

    // active per-hand data
    Transform _targetR, _hintR, _targetL, _hintL;
    float _tPosR, _tRotR, _tPosL, _tRotL;

    void Reset()
    {
        if (!equipmentController) equipmentController = GetComponentInParent<EquipmentController>();
    }

    void Awake()
    {
        CacheLengths();
        if (equipmentController)
        {
            equipmentController.OnItemEquipped += OnEquipped;
            equipmentController.OnItemUnequipped += OnUnequipped;
        }
        RefreshFromActive();
    }

    void OnDestroy()
    {
        if (equipmentController)
        {
            equipmentController.OnItemEquipped -= OnEquipped;
            equipmentController.OnItemUnequipped -= OnUnequipped;
        }
    }

    void CacheLengths()
    {
        _hasR = upperArmR && lowerArmR && handR;
        _hasL = upperArmL && lowerArmL && handL;

        if (_hasR)
        {
            _lenUR = Vector3.Distance(upperArmR.position, lowerArmR.position);
            _lenLR = Vector3.Distance(lowerArmR.position, handR.position);
        }
        if (_hasL)
        {
            _lenUL = Vector3.Distance(upperArmL.position, lowerArmL.position);
            _lenLL = Vector3.Distance(lowerArmL.position, handL.position);
        }
    }

    void OnUnequipped(int slot, HandEquipment item)
    {
        // Turn IK off smoothly
        _targetR = _hintR = _targetL = _hintL = null;
        _tPosR = _tRotR = _tPosL = _tRotL = 0f;
    }

    void OnEquipped(int slot, HandEquipment item) => ReadPoseFrom(item);

    void RefreshFromActive()
    {
        var item = equipmentController ? equipmentController.ActiveItem : null;
        ReadPoseFrom(item);
    }

    void ReadPoseFrom(HandEquipment item)
    {
        var comp = item as Component;
        var pose = comp ? comp.GetComponentInChildren<HandEquipmentPose>(true) : null;

        if (!pose)
        {
            _targetR = _hintR = _targetL = _hintL = null;
            _tPosR = _tRotR = _tPosL = _tRotL = 0f;
            return;
        }

        // Right
        if (pose.useIKRight && pose.rightIKTarget && _hasR)
        {
            _targetR = pose.rightIKTarget;
            _hintR = pose.rightElbowHint;
            _tPosR = Mathf.Clamp01(pose.rightIKPositionWeight);
            _tRotR = Mathf.Clamp01(pose.rightIKRotationWeight);
        }
        else { _targetR = _hintR = null; _tPosR = _tRotR = 0f; }

        // Left
        if (pose.useIKLeft && pose.leftIKTarget && _hasL)
        {
            _targetL = pose.leftIKTarget;
            _hintL = pose.leftElbowHint;
            _tPosL = Mathf.Clamp01(pose.leftIKPositionWeight);
            _tRotL = Mathf.Clamp01(pose.leftIKRotationWeight);
        }
        else { _targetL = _hintL = null; _tPosL = _tRotL = 0f; }
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        // Smooth weights (so toggles/weapon swaps don’t pop)
        _wPosR = Mathf.Lerp(_wPosR, _tPosR, 1f - Mathf.Exp(-weightLerp * dt));
        _wRotR = Mathf.Lerp(_wRotR, _tRotR, 1f - Mathf.Exp(-weightLerp * dt));
        _wPosL = Mathf.Lerp(_wPosL, _tPosL, 1f - Mathf.Exp(-weightLerp * dt));
        _wRotL = Mathf.Lerp(_wRotL, _tRotL, 1f - Mathf.Exp(-weightLerp * dt));

        if (_hasR && _wPosR > 0.0001f && _targetR) SolveTwoBoneIK(
            upperArmR, lowerArmR, handR, _lenUR, _lenLR, _targetR, _hintR, _wPosR, _wRotR);

        if (_hasL && _wPosL > 0.0001f && _targetL) SolveTwoBoneIK(
            upperArmL, lowerArmL, handL, _lenUL, _lenLL, _targetL, _hintL, _wPosL, _wRotL);
    }

    void SolveTwoBoneIK(
        Transform shoulder, Transform elbow, Transform wrist,
        float L1, float L2,
        Transform target, Transform hint,
        float posWeight, float rotWeight)
    {
        // Desired positions / directions
        Vector3 S = shoulder.position;
        Vector3 E = elbow.position;
        Vector3 W = wrist.position;

        Vector3 toTarget = (target.position - S);
        float dist = toTarget.magnitude;

        // Clamp distance to reachable range, soften beyond reach
        float maxReach = L1 + L2;
        if (dist > maxReach)
        {
            float t = Mathf.InverseLerp(maxReach, maxReach * (1f + 0.5f), dist);
            dist = Mathf.Lerp(maxReach, dist, Mathf.Lerp(1f, 1f - reachSoften, t));
        }
        dist = Mathf.Clamp(dist, Mathf.Epsilon, maxReach - 1e-4f);
        Vector3 dirN = toTarget / (toTarget.sqrMagnitude > 1e-8f ? toTarget.magnitude : 1f);

        // Bend plane / hint
        Vector3 pole = hint ? (hint.position - S) : Vector3.zero;
        Vector3 bendDir = Vector3.ProjectOnPlane(pole, dirN).normalized;
        if (bendDir.sqrMagnitude < 1e-6f)
        {
            // fallback to current plane
            Vector3 cu = (E - S).normalized;
            Vector3 cl = (W - E).normalized;
            Vector3 n = Vector3.Cross(cu, cl);
            bendDir = Vector3.Cross(dirN, n).normalized;
            if (bendDir.sqrMagnitude < 1e-6f)
                bendDir = Vector3.Cross(dirN, Vector3.up).normalized;
        }

        // 3) Triangle solve (law of cosines) – angle at shoulder
        float cosTheta = Mathf.Clamp((dist * dist + L1 * L1 - L2 * L2) / (2f * dist * L1), -1f, 1f);
        float theta = Mathf.Acos(cosTheta); // radians

        // Elbow target position
        Vector3 elbowT = S + dirN * (Mathf.Cos(theta) * L1) + bendDir * (Mathf.Sin(theta) * L1);

        // Rotate shoulder to point from S -> elbowT
        Quaternion origShoulderRot = shoulder.rotation;
        Vector3 curUpperDir = (E - S).normalized;
        Vector3 newUpperDir = (elbowT - S).normalized;
        Quaternion deltaU = Quaternion.FromToRotation(curUpperDir, newUpperDir);
        Quaternion newShoulderRot = deltaU * origShoulderRot;

        // Align roll so old and new bend planes match
        Vector3 curLowerDir = (W - E).normalized;
        Vector3 tarLowerDir = (target.position - elbowT).normalized;
        Vector3 nCur = Vector3.Cross(curUpperDir, curLowerDir);
        Vector3 nTar = Vector3.Cross(newUpperDir, tarLowerDir);
        float twist = Vector3.SignedAngle(nCur, nTar, newUpperDir);
        newShoulderRot = Quaternion.AngleAxis(twist, newUpperDir) * newShoulderRot;

        shoulder.rotation = Quaternion.Slerp(origShoulderRot, newShoulderRot, posWeight);

        // After rotating shoulder, recalc elbow world pos and rotate elbow toward target
        Vector3 E2 = elbow.position;
        Vector3 curLowerDir2 = (wrist.position - E2).normalized;
        Vector3 tarLowerDir2 = (target.position - E2).normalized;

        Quaternion origElbowRot = elbow.rotation;
        Quaternion deltaL = Quaternion.FromToRotation(curLowerDir2, tarLowerDir2);
        elbow.rotation = Quaternion.Slerp(origElbowRot, deltaL * origElbowRot, posWeight);

        // Wrist rotation alignment (optional)
        if (rotWeight > 0f)
        {
            Quaternion origWristRot = wrist.rotation;
            Quaternion targetRot = target.rotation;
            wrist.rotation = Quaternion.Slerp(origWristRot, targetRot, rotWeight * posWeight);
        }
    }
}
