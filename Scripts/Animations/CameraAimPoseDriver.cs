using UnityEngine;

[DefaultExecutionOrder(+60)] // Runs after idle/grip so we snapshot post-pose state
public class CameraAimPoseDriver : MonoBehaviour
{
    [Header("References")]
    public FPController controller;
    public EquipmentController equipmentController;
    public Transform hips, spine, neckOrUpperSpine, head;
    public Transform upperArmL, upperArmR;

    [Header("Pitch Coupling (deg per 1 deg camera pitch)")]
    public float spinePitchGain = 0.65f;
    public float neckPitchGain = 0.25f;
    public float headPitchGain = 0.15f;

    [Header("Arm/Shoulder Assist")]
    public bool enableArmAssist = true;
    public bool armAssistUpOnly = true;              // Disable downward assist
    public bool armAssistWhenEquipped = false;       // Disabled while equipped by default
    [Range(0f, 1f)] public float armAssistEquippedScale = 0.2f;

    public float shoulderForwardDeg = 3f;            // Forward roll (local X)
    public float shoulderRaiseDeg = 1.5f;            // Raise/lower (local Z)
    public float maxPitchForAssist = 60f;            // Normalize assist mapping
    public bool invertRaiseZ = false;                // Flip Z if rig inverted
    public bool invertForwardX = false;              // Flip X if rig feels reversed

    [Header("Body Micro Translation")]
    public float hipsLiftPerDeg = 0.002f;

    [Header("Smoothing")]
    [Range(1f, 40f)] public float rotLerp = 18f;
    [Range(1f, 40f)] public float posLerp = 18f;
    public bool zeroRoll = true;

    // Cached baselines
    Quaternion _spine0, _neck0, _head0;
    Vector3 _hipsLocal0;

    // Runtime state
    float _smPitch;
    Quaternion _lastRot;

    void Reset()
    {
        if (!controller) controller = GetComponentInParent<FPController>();
        if (!equipmentController) equipmentController = GetComponentInParent<EquipmentController>();
    }

    void Awake()
    {
        if (!controller) controller = GetComponentInParent<FPController>();
        if (!equipmentController) equipmentController = GetComponentInParent<EquipmentController>();

        if (spine) _spine0 = spine.localRotation;
        if (neckOrUpperSpine) _neck0 = neckOrUpperSpine.localRotation;
        if (head) _head0 = head.localRotation;
        if (hips) _hipsLocal0 = hips.localPosition;

        _lastRot = transform.rotation;
    }

    void LateUpdate()
    {
        if (!controller) return;
        float dt = Time.deltaTime;

        // Camera pivot rotation (yaw + smoothed pitch)
        float yaw = transform.root.eulerAngles.y;
        _smPitch = Mathf.Lerp(_smPitch, controller.CurrentPitch, 1f - Mathf.Exp(-rotLerp * dt));

        Quaternion targetRot = Quaternion.Euler(_smPitch, yaw, 0f);
        if (zeroRoll)
            targetRot = Quaternion.LookRotation(targetRot * Vector3.forward, Vector3.up);

        _lastRot = Quaternion.Slerp(_lastRot, targetRot, 1f - Mathf.Exp(-rotLerp * dt));
        transform.rotation = _lastRot;

        // Torso pitch coupling
        ApplyTorsoPitch(dt);

        // Arm assist
        ApplyArmAssist(dt);

        // Hips lift
        if (hips)
        {
            float lift = _smPitch * hipsLiftPerDeg;
            Vector3 target = _hipsLocal0 + new Vector3(0f, lift, 0f);
            hips.localPosition = Vector3.Lerp(hips.localPosition, target, 1f - Mathf.Exp(-posLerp * dt));
        }
    }

    void ApplyTorsoPitch(float dt)
    {
        float spinePitch = _smPitch * spinePitchGain;
        float neckPitch = _smPitch * neckPitchGain;
        float headPitch = _smPitch * headPitchGain;

        if (spine)
            spine.localRotation = Quaternion.Slerp(spine.localRotation, _spine0 * Quaternion.Euler(spinePitch, 0f, 0f), 1f - Mathf.Exp(-rotLerp * dt));
        if (neckOrUpperSpine)
            neckOrUpperSpine.localRotation = Quaternion.Slerp(neckOrUpperSpine.localRotation, _neck0 * Quaternion.Euler(neckPitch, 0f, 0f), 1f - Mathf.Exp(-rotLerp * dt));
        if (head)
            head.localRotation = Quaternion.Slerp(head.localRotation, _head0 * Quaternion.Euler(headPitch, 0f, 0f), 1f - Mathf.Exp(-rotLerp * dt));
    }

    void ApplyArmAssist(float dt)
    {
        if (!enableArmAssist || (!upperArmL && !upperArmR)) return;

        bool equipped = equipmentController && equipmentController.ActiveItem != null;
        float equipScale = equipped ? (armAssistWhenEquipped ? armAssistEquippedScale : 0f) : 1f;
        if (equipScale <= 0f) return;

        Quaternion preL = upperArmL ? upperArmL.localRotation : Quaternion.identity;
        Quaternion preR = upperArmR ? upperArmR.localRotation : Quaternion.identity;

        float n = Mathf.Clamp(_smPitch / Mathf.Max(1e-3f, maxPitchForAssist), -1f, 1f);
        float fwd = n * shoulderForwardDeg * (invertForwardX ? -1f : 1f);
        float raise = n * shoulderRaiseDeg;

        if (armAssistUpOnly)
        {
            fwd = Mathf.Max(0f, fwd);
            raise = Mathf.Max(0f, raise);
        }

        float zL = invertRaiseZ ? +raise : -raise;
        float zR = invertRaiseZ ? -raise : +raise;

        if (upperArmL)
        {
            Quaternion target = preL * Quaternion.Euler(fwd * equipScale, 0f, zL * equipScale);
            upperArmL.localRotation = Quaternion.Slerp(preL, target, 1f - Mathf.Exp(-rotLerp * dt));
        }
        if (upperArmR)
        {
            Quaternion target = preR * Quaternion.Euler(fwd * equipScale, 0f, zR * equipScale);
            upperArmR.localRotation = Quaternion.Slerp(preR, target, 1f - Mathf.Exp(-rotLerp * dt));
        }
    }
}
