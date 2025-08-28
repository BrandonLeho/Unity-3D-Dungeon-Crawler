using UnityEngine;

[DefaultExecutionOrder(+90)] // after controller update/camera offset composition
public class TorsoBobFromCamera : MonoBehaviour
{
    [Header("References")]
    public FPController controller;  // exposes CameraPositionOffset after all activities
    public Transform hips;           // gets most of the motion
    public Transform spine;          // gets some of the motion
    public Transform head;           // optional light counter/assist

    [Header("Mapping")]
    [Tooltip("How much of camera Y offset goes into hips (0..1). Remainder goes to spine.")]
    [Range(0f, 1f)] public float hipsYShare = 0.6f;
    [Tooltip("Forward/back Z coupling (0 = ignore).")]
    [Range(0f, 1f)] public float zShare = 0.25f;

    [Header("Smoothing")]
    [Range(1f, 40f)] public float posLerp = 18f;
    [Range(1f, 40f)] public float rotLerp = 18f;
    [Tooltip("Small pitch to follow Z motion (visual cohesion).")]
    public float spinePitchPerMeterZ = -6f; // negative pitches back when cam offsets forward

    Vector3 _hips0, _spine0Pos; Quaternion _spine0Rot, _head0Rot;

    void Reset()
    {
        if (!controller) controller = GetComponentInParent<FPController>();
        if (!hips) hips = transform.Find("Hips");
        if (!spine) spine = transform.Find("Hips/Spine");
        if (!head) head = transform.Find("Hips/Spine/Head");
    }

    void Awake()
    {
        if (!controller) controller = GetComponentInParent<FPController>();
        if (hips) _hips0 = hips.localPosition;
        if (spine) { _spine0Pos = spine.localPosition; _spine0Rot = spine.localRotation; }
        if (head) _head0Rot = head.localRotation;
    }

    void LateUpdate()
    {
        if (!controller) return;

        // This is the final cumulative offset the camera used this frame,
        // composed by FPController via RequestCameraOffset.  :contentReference[oaicite:3]{index=3}
        Vector3 camOff = controller.CameraPositionOffset;

        // Split Y motion across hips/spine
        float yHips = camOff.y * hipsYShare;
        float ySpine = camOff.y * (1f - hipsYShare);

        // Optional Z mapping
        float zHips = camOff.z * zShare * 0.25f;
        float zSpine = camOff.z * zShare * 0.75f;

        float k = 1f - Mathf.Exp(-posLerp * Time.deltaTime);

        if (hips)
        {
            Vector3 target = _hips0 + new Vector3(0f, yHips, zHips);
            hips.localPosition = Vector3.Lerp(hips.localPosition, target, k);
        }

        if (spine)
        {
            Vector3 targetPos = _spine0Pos + new Vector3(0f, ySpine, zSpine);
            spine.localPosition = Vector3.Lerp(spine.localPosition, targetPos, k);

            // Tiny pitch from forward/back offsets for coherence
            float pitchDeg = camOff.z * spinePitchPerMeterZ;
            Quaternion targetRot = _spine0Rot * Quaternion.Euler(pitchDeg, 0f, 0f);
            spine.localRotation = Quaternion.Slerp(spine.localRotation, targetRot, 1f - Mathf.Exp(-rotLerp * Time.deltaTime));
        }

        if (head)
        {
            // Head stays mostly at baseline; if you want counter-motion, add it here.
            head.localRotation = Quaternion.Slerp(head.localRotation, _head0Rot, 1f - Mathf.Exp(-rotLerp * Time.deltaTime));
        }
    }
}
