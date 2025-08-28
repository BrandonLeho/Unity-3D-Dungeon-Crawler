using UnityEngine;

[DefaultExecutionOrder(+100)]
public class FPCameraStabilizer : MonoBehaviour
{
    public enum FollowMode { RootOffset, HeadLocalOffset, Hybrid }

    [Header("References")]
    public Transform playerRoot;
    public FPController controller;
    public Transform headBone;

    [Header("Follow Mode")]
    public FollowMode followMode = FollowMode.HeadLocalOffset;

    [Tooltip("Root-space offset from playerRoot (used in RootOffset & Hybrid).")]
    public Vector3 rootLocalEyeOffset = new Vector3(0f, 1.65f, 0f);

    [Tooltip("Head-bone local offset for the eye position (used in HeadLocalOffset & Hybrid).")]
    public Vector3 headLocalEyeOffset = new Vector3(0.0f, 0.08f, 0.09f);

    [Tooltip("In Hybrid, 0 = pure RootOffset, 1 = pure HeadLocalOffset.")]
    [Range(0f, 1f)] public float hybridHeadWeight = 1f;

    [Header("Smoothing")]
    [Tooltip("Seconds to reach ~63% of the distance to the target position. Set to 0 for a perfect lock.")]
    public float positionSmoothTime = 0.0f;
    [Range(1f, 40f)] public float rotationLerp = 22f;

    [Header("Rotation")]
    [Tooltip("Keep roll at 0 so horizon is level.")]
    public bool zeroRoll = true;

    Vector3 _posVel;
    float _smPitch;
    Quaternion _lastRot;

    void Reset()
    {
        if (!playerRoot) playerRoot = GetComponentInParent<Transform>();
        if (!controller) controller = GetComponentInParent<FPController>();
    }

    void Awake()
    {
        _lastRot = transform.rotation;
    }

    void LateUpdate()
    {
        if (!playerRoot || !controller) return;

        float dt = Time.deltaTime;

        float yaw = playerRoot.eulerAngles.y;
        _smPitch = Mathf.Lerp(_smPitch, controller.CurrentPitch, 1f - Mathf.Exp(-rotationLerp * dt));

        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        Quaternion pitchRot = Quaternion.Euler(_smPitch, 0f, 0f);
        Quaternion targetRot = yawRot * pitchRot;

        if (zeroRoll)
        {
            Vector3 f = targetRot * Vector3.forward;
            targetRot = Quaternion.LookRotation(f, Vector3.up);
        }

        _lastRot = Quaternion.Slerp(_lastRot, targetRot, 1f - Mathf.Exp(-rotationLerp * dt));
        transform.rotation = _lastRot;

        Vector3 posRoot = playerRoot.TransformPoint(rootLocalEyeOffset);

        Vector3 posHead = posRoot;
        if (headBone)
            posHead = headBone.TransformPoint(headLocalEyeOffset);

        Vector3 targetPos;
        switch (followMode)
        {
            case FollowMode.RootOffset:
                targetPos = posRoot;
                break;
            case FollowMode.HeadLocalOffset:
                targetPos = posHead;
                break;
            default:
                targetPos = Vector3.Lerp(posRoot, posHead, Mathf.Clamp01(hybridHeadWeight));
                break;
        }

        if (positionSmoothTime <= 0f)
        {
            transform.position = targetPos;
        }
        else
        {
            transform.position = Vector3.SmoothDamp(
                transform.position, targetPos, ref _posVel, positionSmoothTime
            );
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Capture Head Local Eye Offset From Current Camera")]
    void CaptureHeadLocalFromCamera()
    {
        if (!headBone)
        {
            Debug.LogWarning("[FPCameraStabilizer] Assign headBone to capture.");
            return;
        }
        headLocalEyeOffset = headBone.InverseTransformPoint(transform.position);
        Debug.Log($"[FPCameraStabilizer] Captured headLocalEyeOffset = {headLocalEyeOffset}");
    }
#endif
}
