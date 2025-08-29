using UnityEngine;

/// <summary>
/// Per-item pose & parameter overrides for hands, fingers, and IK.
/// Attach this to the equipped item (or a child) so ProceduralGripAnimator can read it.
/// </summary>
public class HandEquipmentPose : MonoBehaviour
{
    // ---------------- Arm/Hand baselines ----------------
    [Header("Right Hand Pose (degrees)")]
    public Vector3 upperArmR_BaseEuler = new Vector3(12, -18, 6);
    public Vector3 lowerArmR_BaseEuler = new Vector3(10, 0, 0);
    public Vector3 handR_BaseEuler = new Vector3(0, 0, 0);

    [Header("Left Hand (optional)")]
    public bool useLeftHand = false;
    public Vector3 upperArmL_BaseEuler = new Vector3(8, 22, -4);
    public Vector3 lowerArmL_BaseEuler = new Vector3(8, 0, 0);
    public Vector3 handL_BaseEuler = new Vector3(0, 0, 0);

    // ---------------- Finger inputs ----------------
    [Header("Fingers: Inputs (0..1)")]
    public bool usePerHandFingerInputs = false;

    [Range(0f, 1f)] public float fingerCurl = 0f;   // used if usePerHandFingerInputs = false
    [Range(0f, 1f)] public float fingerSpread = 0f; // used if usePerHandFingerInputs = false

    [Range(0f, 1f)] public float leftFingerCurl = 0f;
    [Range(0f, 1f)] public float leftFingerSpread = 0f;
    [Range(0f, 1f)] public float rightFingerCurl = 0f;
    [Range(0f, 1f)] public float rightFingerSpread = 0f;

    // ---------------- Optional finger parameter overrides ----------------
    public enum FingerAxis { X, Y, Z }

    [Header("Finger Parameter Overrides (optional)")]
    public bool overrideFingerSettings = false;

    // Curl (non-thumb)
    [Header("• Curl (Non-Thumb)")]
    [Range(0f, 30f)] public float fingerIdleCurlDeg = 4f;
    [Range(0f, 3f)] public float fingerIdleHz = 0.25f;
    [Range(0f, 180f)] public float fingerMaxCurlDeg = 120f;
    [Range(0f, 1.5f)] public float fingerFalloff = 0.65f;
    [Range(0.1f, 30f)] public float fingerRotLerp = 12f;
    [Range(0f, 6f)] public float fingerMicroDeg = 0.8f;

    // Thumb specifics (NEW: per-thumb XYZ)
    [Header("• Thumb (Per-Axis Max Curl)")]
    public Vector3 leftThumbMaxCurlDegXYZ = new Vector3(0f, 0f, 100f);
    public Vector3 rightThumbMaxCurlDegXYZ = new Vector3(0f, 0f, 100f);
    [Range(0f, 1.5f)] public float thumbFalloff = 0.75f;

    // Spread
    [Header("• Spread")]
    [Range(0f, 20f)] public float fingerIdleSpreadDeg = 1.5f;
    [Range(0f, 60f)] public float fingerMaxSpreadDeg = 18f;
    public FingerAxis fingerSpreadAxis = FingerAxis.Y;
    public bool mirrorRightHand = true;
    [Range(0.1f, 30f)] public float fingerSpreadRotLerp = 12f;

    // ---------------- Blend speeds ----------------
    [Header("Blend Speeds")]
    [Range(1f, 30f)] public float equipBlendSpeed = 16f;
    [Range(1f, 30f)] public float unequipBlendSpeed = 16f;

    // ---------------- IK (kept) ----------------
    [Header("IK (optional) - Right Hand")]
    public bool useIKRight = false;
    public Transform rightIKTarget;
    public Transform rightElbowHint;
    [Range(0f, 1f)] public float rightIKPositionWeight = 1f;
    [Range(0f, 1f)] public float rightIKRotationWeight = 1f;

    [Header("IK (optional) - Left Hand")]
    public bool useIKLeft = false;
    public Transform leftIKTarget;       // e.g., "Grip_L" for two-handed items
    public Transform leftElbowHint;      // optional
    [Range(0f, 1f)] public float leftIKPositionWeight = 1f;
    [Range(0f, 1f)] public float leftIKRotationWeight = 1f;

#if UNITY_EDITOR
    [ContextMenu("Zero Out Pose")]
    void ZeroOutPose()
    {
        upperArmR_BaseEuler = lowerArmR_BaseEuler = handR_BaseEuler = Vector3.zero;
        upperArmL_BaseEuler = lowerArmL_BaseEuler = handL_BaseEuler = Vector3.zero;

        fingerCurl = fingerSpread = 0f;
        leftFingerCurl = leftFingerSpread = rightFingerCurl = rightFingerSpread = 0f;
    }

    [ContextMenu("Disable Finger Overrides")]
    void DisableFingerOverrides() => overrideFingerSettings = false;
#endif
}
