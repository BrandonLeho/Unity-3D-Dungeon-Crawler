using UnityEngine;

[DefaultExecutionOrder(+15)]
public class ProceduralLowerBodyLocomotion : MonoBehaviour
{
    [Header("Controller")]
    public FPController controller; // Provides MoveInput, CurrentSpeed/Velocity, CameraPositionOffset, RequestCameraOffset

    [Header("Bones")]
    public Transform hips;
    public Transform spine;
    public Transform thighL, calfL, footL;
    public Transform thighR, calfR, footR;

    [Header("Hip Yaw Toward Movement")]
    [Range(0f, 80f)] public float maxHipYaw = 35f;
    [Range(1f, 30f)] public float hipYawLerp = 12f;
    public float strafeBias = 1.0f;
    public float minMoveForYaw = 0.15f;

    [Header("Backpedal")]
    public float backpedalThreshold = -0.2f;     // MoveInput.y < this => backpedal
    [Range(0f, 1f)] public float backpedalStrideScale = 0.85f;

    [Header("Step Timing")]
    public float stepFrequencyPerMS = 1.8f;      // Hz at 1 m/s
    [Range(0.2f, 2.0f)] public float cycleBias = 1.0f;

    [Header("Pelvis Motion")]
    public float bodyBobHeight = 0.045f;         // vertical bob (m)
    public float pelvisSideDropDeg = 4.0f;       // roll Z toward stance foot
    public float pelvisSideShift = 0.02f;        // meters toward stance foot

    [Header("Stride Shape (angles)")]
    public float strideDeg = 32f;                // thigh swing amplitude
    public float kneeBendDeg = 26f;              // base knee bend amplitude
    public float swingKneeLiftBonus = 10f;       // extra knee bend at swing apex
    public float stanceHeelOffDeg = 12f;         // plantarflex during heel-off
    public float swingToeClearDeg = 14f;         // dorsiflex during swing apex
    public float footPitchContactDeg = 10f;      // contact toe pitch

    [Header("Stop / Idle Reset")]
    [Tooltip("Below this speed the character is 'stopped' and glides to the default pose.")]
    public float stopSpeedThreshold = 0.05f;     // m/s
    [Tooltip("How quickly to return legs/hips/spine to their default rotations when stopped.")]
    [Range(1f, 40f)] public float idleResetLerp = 14f;
    [Tooltip("Zero hip yaw when stopped.")]
    public bool zeroHipYawOnStop = true;

    [Header("Torso Bob Sync (with FP camera)")]
    public bool syncToCameraOffset = true;
    [Range(0f, 1f)] public float torsoBobFollow = 1.0f;
    [Range(1f, 40f)] public float torsoBobLerp = 16f;
    public float additionalBobY = 0.0f;
    public float additionalBobZ = 0.0f;

    [Header("Smoothing")]
    [Range(1f, 40f)] public float poseLerp = 14f;
    [Range(1f, 40f)] public float posLerp = 14f;

    // caches
    Quaternion _hips0, _spine0, _thighL0, _thighR0, _calfL0, _calfR0, _footL0, _footR0;
    Vector3 _hipsPos0;
    float _phase;                // gait phase (rad)
    float _hipYawTarget, _hipYawSm;
    Vector3 _torsoBobTarget, _torsoBobSm;

    void Reset()
    {
        if (!controller) controller = GetComponentInParent<FPController>();
    }

    void Awake()
    {
        if (!controller) controller = GetComponentInParent<FPController>();

        if (hips) { _hips0 = hips.localRotation; _hipsPos0 = hips.localPosition; }
        if (spine) _spine0 = spine.localRotation;

        if (thighL) _thighL0 = thighL.localRotation;
        if (calfL) _calfL0 = calfL.localRotation;
        if (footL) _footL0 = footL.localRotation;

        if (thighR) _thighR0 = thighR.localRotation;
        if (calfR) _calfR0 = calfR.localRotation;
        if (footR) _footR0 = footR.localRotation;

        if (controller && syncToCameraOffset)
            controller.RequestCameraOffset += OnRequestCameraOffsetMirror;
    }

    void OnDestroy()
    {
        if (controller)
            controller.RequestCameraOffset -= OnRequestCameraOffsetMirror;
    }

    // Mirror FP camera offset so torso bob matches the head bob pipeline
    void OnRequestCameraOffsetMirror()
    {
        if (!syncToCameraOffset) return;
        Vector3 camOff = controller.CameraPositionOffset;
        _torsoBobTarget = new Vector3(0f, camOff.y, camOff.z) + new Vector3(0f, additionalBobY, additionalBobZ);
    }

    void ResetToIdle(float dt)
    {
        float k = 1f - Mathf.Exp(-idleResetLerp * dt);
        float kHip = 1f - Mathf.Exp(-hipYawLerp * dt);

        if (zeroHipYawOnStop)
            _hipYawSm = Mathf.Lerp(_hipYawSm, 0f, kHip);

        hips.localPosition = Vector3.Lerp(hips.localPosition, _hipsPos0, k);
        hips.localRotation = Quaternion.Slerp(hips.localRotation, _hips0 * Quaternion.Euler(0f, _hipYawSm, 0f), k);

        if (spine)
            spine.localRotation = Quaternion.Slerp(spine.localRotation, _spine0, k);

        if (thighL) thighL.localRotation = Quaternion.Slerp(thighL.localRotation, _thighL0, k);
        if (calfL) calfL.localRotation = Quaternion.Slerp(calfL.localRotation, _calfL0, k);
        if (footL) footL.localRotation = Quaternion.Slerp(footL.localRotation, _footL0, k);

        if (thighR) thighR.localRotation = Quaternion.Slerp(thighR.localRotation, _thighR0, k);
        if (calfR) calfR.localRotation = Quaternion.Slerp(calfR.localRotation, _calfR0, k);
        if (footR) footR.localRotation = Quaternion.Slerp(footR.localRotation, _footR0, k);

        if (syncToCameraOffset)
        {
            _torsoBobSm = Vector3.Lerp(_torsoBobSm, _torsoBobTarget * torsoBobFollow, 1f - Mathf.Exp(-torsoBobLerp * dt));
            hips.localPosition += new Vector3(0f, _torsoBobSm.y + additionalBobY, 0f);
        }
    }

    void LateUpdate()
    {
        if (!controller || !hips) return;
        float dt = Time.deltaTime;

        float speed = controller.CurrentSpeed; // m/s
        if (speed <= stopSpeedThreshold || controller.MoveInput.sqrMagnitude < 0.0001f)
        {
            ResetToIdle(dt);
            return;
        }

        float freq = stepFrequencyPerMS * Mathf.Clamp(speed, 0f, 3f) * cycleBias;
        _phase += freq * dt * Mathf.PI * 2f;

        // Left / Right phases (out of phase by PI)
        float pL = _phase;
        float pR = _phase + Mathf.PI;

        // Sine helpers
        float sL = Mathf.Sin(pL);
        float cL = Mathf.Cos(pL);
        float sR = Mathf.Sin(pR);
        float cR = Mathf.Cos(pR);

        bool backpedal = controller.MoveInput.y < backpedalThreshold;

        // Desired hip yaw from move direction (no backward-facing on backpedal)
        Vector3 vel = controller.CurrentVelocity; // world
        Vector3 localVel = transform.InverseTransformDirection(new Vector3(vel.x, 0f, vel.z));
        float strafe = Mathf.Clamp(localVel.x, -1f, 1f);
        float fwd = Mathf.Clamp(localVel.z, -1f, 1f);

        float desiredYaw = 0f;
        if (speed > minMoveForYaw)
        {
            if (backpedal)
            {
                desiredYaw = Mathf.Clamp(strafe, -1f, 1f) * maxHipYaw * 0.6f;
            }
            else
            {
                float dir = Mathf.Atan2(strafe * strafeBias, Mathf.Max(0.0001f, fwd)) * Mathf.Rad2Deg;
                desiredYaw = Mathf.Clamp(dir, -maxHipYaw, maxHipYaw);
            }
        }
        _hipYawTarget = desiredYaw;
        _hipYawSm = Mathf.Lerp(_hipYawSm, _hipYawTarget, 1f - Mathf.Exp(-hipYawLerp * dt));

        // Pelvis bob + side drop/shift toward stance foot
        bool leftStance = cL > 0f;
        float bobY = (Mathf.Sin(_phase * 2f) * 0.5f + 0.5f) * bodyBobHeight * Mathf.Clamp01(speed);
        Vector3 hipsPosTarget = _hipsPos0 + new Vector3(0f, bobY, 0f);

        float sideSign = leftStance ? -1f : +1f;
        Quaternion hipsRoll = Quaternion.Euler(0f, 0f, pelvisSideDropDeg * sideSign * Mathf.Clamp01(speed));
        Vector3 sideShift = new Vector3(pelvisSideShift * sideSign * Mathf.Clamp01(speed), 0f, 0f);

        hips.localPosition = Vector3.Lerp(hips.localPosition, hipsPosTarget + sideShift, 1f - Mathf.Exp(-posLerp * dt));
        hips.localRotation = Quaternion.Slerp(hips.localRotation, _hips0 * hipsRoll * Quaternion.Euler(0f, _hipYawSm, 0f), 1f - Mathf.Exp(-poseLerp * dt));

        // Keep torso facing camera (counter-rotate by -hipsYaw)
        if (spine)
        {
            Quaternion targetSpine = _spine0 * Quaternion.Euler(0f, -_hipYawSm, 0f);
            spine.localRotation = Quaternion.Slerp(spine.localRotation, targetSpine, 1f - Mathf.Exp(-poseLerp * dt));
        }

        // Torso bob synced with head bobbing
        if (spine && syncToCameraOffset)
        {
            _torsoBobSm = Vector3.Lerp(_torsoBobSm, _torsoBobTarget * torsoBobFollow, 1f - Mathf.Exp(-torsoBobLerp * dt));
            hips.localPosition += new Vector3(0f, _torsoBobSm.y, 0f);
        }

        // Scale stride for backpedal
        float strideScale = Mathf.Clamp01(speed) * (backpedal ? backpedalStrideScale : 1f);

        // LEG GAIT (no IK)
        float stanceL = Mathf.Max(0f, cL);
        float swingL = Mathf.Max(0f, -cL);
        float stanceR = Mathf.Max(0f, cR);
        float swingR = Mathf.Max(0f, -cR);

        float thighSwingL = sL * strideDeg * strideScale;
        float thighSwingR = sR * strideDeg * strideScale;

        float kneeL = (1f - stanceL) * kneeBendDeg + swingL * swingKneeLiftBonus;
        float kneeR = (1f - stanceR) * kneeBendDeg + swingR * swingKneeLiftBonus;

        float footPitchL =
              stanceL * (footPitchContactDeg * Mathf.SmoothStep(0f, 1f, stanceL))
            + (1f - stanceL) * (swingL * swingToeClearDeg)
            + Mathf.Max(0f, sL) * stanceHeelOffDeg * stanceL;

        float footPitchR =
              stanceR * (footPitchContactDeg * Mathf.SmoothStep(0f, 1f, stanceR))
            + (1f - stanceR) * (swingR * swingToeClearDeg)
            + Mathf.Max(0f, sR) * stanceHeelOffDeg * stanceR;

        // LEFT leg
        if (thighL && calfL)
        {
            Quaternion tL = _thighL0 * Quaternion.Euler(thighSwingL, 0f, 0f);
            Quaternion cLq = _calfL0 * Quaternion.Euler(-kneeL, 0f, 0f);
            thighL.localRotation = Quaternion.Slerp(thighL.localRotation, tL, 1f - Mathf.Exp(-poseLerp * dt));
            calfL.localRotation = Quaternion.Slerp(calfL.localRotation, cLq, 1f - Mathf.Exp(-poseLerp * dt));

            if (footL)
            {
                float bank = sL * 2.0f * strideScale; // small lateral bank during swing
                Quaternion fLq = _footL0 * Quaternion.Euler(footPitchL, 0f, bank);
                footL.localRotation = Quaternion.Slerp(footL.localRotation, fLq, 1f - Mathf.Exp(-poseLerp * dt));
            }
        }

        // RIGHT leg
        if (thighR && calfR)
        {
            Quaternion tR = _thighR0 * Quaternion.Euler(thighSwingR, 0f, 0f);
            Quaternion cRq = _calfR0 * Quaternion.Euler(-kneeR, 0f, 0f);
            thighR.localRotation = Quaternion.Slerp(thighR.localRotation, tR, 1f - Mathf.Exp(-poseLerp * dt));
            calfR.localRotation = Quaternion.Slerp(calfR.localRotation, cRq, 1f - Mathf.Exp(-poseLerp * dt));

            if (footR)
            {
                float bank = sR * -2.0f * strideScale;
                Quaternion fRq = _footR0 * Quaternion.Euler(footPitchR, 0f, bank);
                footR.localRotation = Quaternion.Slerp(footR.localRotation, fRq, 1f - Mathf.Exp(-poseLerp * dt));
            }
        }
    }
}
