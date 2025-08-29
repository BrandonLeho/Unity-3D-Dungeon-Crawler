using System.Collections.Generic;
using UnityEngine;

/// <summary>Whole-body procedural idle animator (zero assets). Fingers moved to ProceduralFingerAnimator.</summary>
public class ProceduralIdleAnimator : MonoBehaviour
{
    [Header("Required Transforms")]
    [Tooltip("Main body root (usually where CharacterController lives).")]
    public Transform root;
    public Transform hips;
    public Transform spine;
    public Transform head;

    [Header("Arms (Left)")]
    public bool enableLeftArm = true;
    public Transform upperArmL;
    public Transform lowerArmL;
    public Transform handL;

    [Header("Arms (Right)")]
    public bool enableRightArm = true;
    public Transform upperArmR;
    public Transform lowerArmR;
    public Transform handR;

    [Header("Global Toggles")]
    public bool animateBody = true;
    public bool animateHead = true;

    [Header("Breathing")]
    [Range(0f, 0.15f)] public float breathHeight = 0.035f; // meters on hips
    [Range(0f, 10f)] public float breathHz = 0.25f;
    [Range(0f, 10f)] public float breathSpinePitchDeg = 2.0f; // small spine pitch
    [Range(0f, 10f)] public float headCounterPitchDeg = 1.2f;  // counter to spine

    [Header("Body Sway")]
    [Range(0f, 10f)] public float swayHz = 0.35f;        // slow lateral sway
    [Range(0f, 6f)] public float swayRollDeg = 1.4f;     // roll at spine
    [Range(0f, 6f)] public float swayYawDeg = 1.2f;      // yaw at spine

    [Header("Micro Jitter (Perlin)")]
    [Range(0f, 3f)] public float microPosMm = 0.8f;       // ~millimeters at hips
    [Range(0f, 5f)] public float microRotDeg = 0.6f;      // tiny random rotation
    [Range(0f, 5f)] public float microHz = 0.8f;

    [Header("Arms: Baseline Poses (degrees)")]
    [Tooltip("Baseline local rotation for each joint. Idle anim will add on top.")]
    public Vector3 upperArmL_BaseEuler = new Vector3(10, 20, -5);
    public Vector3 lowerArmL_BaseEuler = new Vector3(10, 0, 0);
    public Vector3 handL_BaseEuler = new Vector3(0, 0, 0);

    public Vector3 upperArmR_BaseEuler = new Vector3(10, -20, 5);
    public Vector3 lowerArmR_BaseEuler = new Vector3(10, 0, 0);
    public Vector3 handR_BaseEuler = new Vector3(0, 0, 0);

    [Header("Arms: Idle Additive Motion")]
    [Range(0f, 10f)] public float armHz = 0.5f;     // slow arm drift
    [Range(0f, 10f)] public float armSwingDeg = 3f; // fwd/back around local X
    [Range(0f, 10f)] public float armYawDeg = 2f;   // left/right around local Y
    [Range(0f, 10f)] public float wristDeg = 2f;    // tiny wrist motion

    [Header("Smoothing")]
    [Range(0.1f, 30f)] public float rotLerp = 10f;
    [Range(0.1f, 30f)] public float posLerp = 10f;

    // Cached defaults (for additive offsets)
    Vector3 _hipsLocalPos0;
    Quaternion _spine0, _head0;
    Quaternion _uArmL0, _lArmL0, _handL0;
    Quaternion _uArmR0, _lArmR0, _handR0;

    float _seedX, _seedY, _seedZ;     // per-instance randomization

    void Reset()
    {
        if (!root) root = transform;
        if (!hips) hips = transform.Find("Hips");
        if (!spine) spine = transform.Find("Hips/Spine");
        if (!head) head = transform.Find("Hips/Spine/Head");

        if (!upperArmL) upperArmL = transform.Find("Hips/UpperArm_L");
        if (!lowerArmL) lowerArmL = transform.Find("Hips/UpperArm_L/LowerArm_L");
        if (!handL) handL = transform.Find("Hips/UpperArm_L/LowerArm_L/Hand_L");

        if (!upperArmR) upperArmR = transform.Find("Hips/UpperArm_R");
        if (!lowerArmR) lowerArmR = transform.Find("Hips/UpperArm_R/LowerArm_R");
        if (!handR) handR = transform.Find("Hips/UpperArm_R/LowerArm_R/Hand_R");
    }

    void Awake()
    {
        if (!root) root = transform;

        if (hips) _hipsLocalPos0 = hips.localPosition;
        if (spine) _spine0 = spine.localRotation;
        if (head) _head0 = head.localRotation;

        if (upperArmL) _uArmL0 = upperArmL.localRotation;
        if (lowerArmL) _lArmL0 = lowerArmL.localRotation;
        if (handL) _handL0 = handL.localRotation;

        if (upperArmR) _uArmR0 = upperArmR.localRotation;
        if (lowerArmR) _lArmR0 = lowerArmR.localRotation;
        if (handR) _handR0 = handR.localRotation;

        _seedX = Random.Range(0f, 1000f);
        _seedY = Random.Range(0f, 1000f);
        _seedZ = Random.Range(0f, 1000f);
    }

    void LateUpdate()
    {
        float t = Time.time;
        float dt = Time.deltaTime;

        // Body
        if (animateBody && hips && spine)
        {
            float breathPhase = Mathf.Sin(t * breathHz * Mathf.PI * 2f);
            Vector3 targetHips = _hipsLocalPos0 + new Vector3(0f, breathHeight * (0.5f * breathPhase + 0.5f), 0f);

            float nX = Perlin(t * microHz, _seedX) - 0.5f;
            float nY = Perlin(t * microHz, _seedY) - 0.5f;
            float nZ = Perlin(t * microHz, _seedZ) - 0.5f;
            targetHips += new Vector3(nX, nY, nZ) * (microPosMm * 0.001f);

            hips.localPosition = Vector3.Lerp(hips.localPosition, targetHips, 1f - Mathf.Exp(-posLerp * dt));

            float swayPhase = Mathf.Sin(t * swayHz * Mathf.PI * 2f);
            Quaternion targetSpine =
                _spine0
                * Quaternion.Euler(breathPhase * breathSpinePitchDeg, 0f, 0f)
                * Quaternion.Euler(0f, swayPhase * swayYawDeg, swayPhase * swayRollDeg);

            targetSpine *= Quaternion.Euler(
                (Perlin(t * microHz, _seedX + 11.1f) - 0.5f) * microRotDeg,
                (Perlin(t * microHz, _seedY + 22.2f) - 0.5f) * microRotDeg,
                (Perlin(t * microHz, _seedZ + 33.3f) - 0.5f) * microRotDeg
            );

            spine.localRotation = Quaternion.Slerp(spine.localRotation, targetSpine, 1f - Mathf.Exp(-rotLerp * dt));
        }

        // Head
        if (animateHead && head)
        {
            float breathPhase = Mathf.Sin(t * breathHz * Mathf.PI * 2f);
            Quaternion targetHead = _head0 * Quaternion.Euler(-breathPhase * headCounterPitchDeg, 0f, 0f);
            head.localRotation = Quaternion.Slerp(head.localRotation, targetHead, 1f - Mathf.Exp(-rotLerp * dt));
        }

        // Arms
        float armSin = Mathf.Sin(t * armHz * Mathf.PI * 2f);
        float armCos = Mathf.Cos(t * armHz * Mathf.PI * 2f);

        if (enableLeftArm && upperArmL && lowerArmL && handL)
        {
            Quaternion baseU = Quaternion.Euler(upperArmL_BaseEuler);
            Quaternion baseL = Quaternion.Euler(lowerArmL_BaseEuler);
            Quaternion baseH = Quaternion.Euler(handL_BaseEuler);

            Quaternion addU = Quaternion.Euler(armSin * armSwingDeg, armCos * armYawDeg, 0f);
            Quaternion addL = Quaternion.Euler(Mathf.Abs(armSin) * (armSwingDeg * 0.5f), 0f, 0f);
            Quaternion addH = Quaternion.Euler(armCos * (wristDeg * 0.5f), armSin * (wristDeg * 0.5f), 0f);

            Quaternion targetU = _uArmL0 * baseU * addU;
            Quaternion targetL = _lArmL0 * baseL * addL;
            Quaternion targetH = _handL0 * baseH * addH;

            upperArmL.localRotation = Quaternion.Slerp(upperArmL.localRotation, targetU, 1f - Mathf.Exp(-rotLerp * dt));
            lowerArmL.localRotation = Quaternion.Slerp(lowerArmL.localRotation, targetL, 1f - Mathf.Exp(-rotLerp * dt));
            handL.localRotation = Quaternion.Slerp(handL.localRotation, targetH, 1f - Mathf.Exp(-rotLerp * dt));
        }

        if (enableRightArm && upperArmR && lowerArmR && handR)
        {
            Quaternion baseU = Quaternion.Euler(upperArmR_BaseEuler);
            Quaternion baseL = Quaternion.Euler(lowerArmR_BaseEuler);
            Quaternion baseH = Quaternion.Euler(handR_BaseEuler);

            Quaternion addU = Quaternion.Euler(-armSin * armSwingDeg, -armCos * armYawDeg, 0f);
            Quaternion addL = Quaternion.Euler(Mathf.Abs(armSin) * (armSwingDeg * 0.5f), 0f, 0f);
            Quaternion addH = Quaternion.Euler(-armCos * (wristDeg * 0.5f), -armSin * (wristDeg * 0.5f), 0f);

            Quaternion targetU = _uArmR0 * baseU * addU;
            Quaternion targetL = _lArmR0 * baseL * addL;
            Quaternion targetH = _handR0 * baseH * addH;

            upperArmR.localRotation = Quaternion.Slerp(upperArmR.localRotation, targetU, 1f - Mathf.Exp(-rotLerp * dt));
            lowerArmR.localRotation = Quaternion.Slerp(lowerArmR.localRotation, targetL, 1f - Mathf.Exp(-rotLerp * dt));
            handR.localRotation = Quaternion.Slerp(handR.localRotation, targetH, 1f - Mathf.Exp(-rotLerp * dt));
        }
    }

    static float Perlin(float t, float seed) => Mathf.PerlinNoise(t, seed);

#if UNITY_EDITOR
    [ContextMenu("Capture Current As Arm Baselines")]
    void CaptureCurrentBaselines()
    {
        if (upperArmL) upperArmL_BaseEuler = NormalizeEuler(Quaternion.Inverse(_uArmL0) * upperArmL.localRotation);
        if (lowerArmL) lowerArmL_BaseEuler = NormalizeEuler(Quaternion.Inverse(_lArmL0) * lowerArmL.localRotation);
        if (handL) handL_BaseEuler = NormalizeEuler(Quaternion.Inverse(_handL0) * handL.localRotation);

        if (upperArmR) upperArmR_BaseEuler = NormalizeEuler(Quaternion.Inverse(_uArmR0) * upperArmR.localRotation);
        if (lowerArmR) lowerArmR_BaseEuler = NormalizeEuler(Quaternion.Inverse(_lArmR0) * lowerArmR.localRotation);
        if (handR) handR_BaseEuler = NormalizeEuler(Quaternion.Inverse(_handR0) * handR.localRotation);

        Debug.Log("[ProceduralIdleAnimator] Captured current arm rotations as baselines.");
    }

    static Vector3 NormalizeEuler(Quaternion q)
    {
        Vector3 e = q.eulerAngles;
        e.x = NormalizeAngle(e.x);
        e.y = NormalizeAngle(e.y);
        e.z = NormalizeAngle(e.z);
        return e;
    }

    static float NormalizeAngle(float a)
    {
        a %= 360f;
        if (a > 180f) a -= 360f;
        return a;
    }
#endif
}
