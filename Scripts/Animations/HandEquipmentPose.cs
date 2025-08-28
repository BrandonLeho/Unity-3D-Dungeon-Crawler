using UnityEngine;

public class HandEquipmentPose : MonoBehaviour
{
    [Header("Right Hand Pose (degrees)")]
    public Vector3 upperArmR_BaseEuler = new Vector3(12, -18, 6);
    public Vector3 lowerArmR_BaseEuler = new Vector3(10, 0, 0);
    public Vector3 handR_BaseEuler = new Vector3(0, 0, 0);

    [Header("Left Hand (optional)")]
    public bool useLeftHand = false;
    public Vector3 upperArmL_BaseEuler = new Vector3(8, 22, -4);
    public Vector3 lowerArmL_BaseEuler = new Vector3(8, 0, 0);
    public Vector3 handL_BaseEuler = new Vector3(0, 0, 0);

    [Header("Fingers: Per-Item Inputs (0..1)")]
    [Range(0f, 1f)] public float fingerCurl = 0.6f;   // 0=open, 1=closed
    [Range(0f, 1f)] public float fingerSpread = 0.0f; // 0=together, 1=wide

    [Header("Fingers: Optional Overrides")]
    public bool overrideFingerSettings = false;

    [Header("Curl Overrides")]
    [Range(0f, 150f)] public float fingerIdleCurlDeg = 4f;
    [Range(0f, 3f)] public float fingerIdleHz = 0.25f;
    [Range(0f, 150f)] public float fingerMaxCurlDeg = 55f;
    [Range(0f, 1f)] public float fingerFalloff = 0.65f;
    [Range(0f, 30f)] public float fingerRotLerp = 12f;
    [Range(0f, 4f)] public float fingerMicroDeg = 0.8f;

    public enum FingerAxis { X, Y, Z }

    [Header("Spread Overrides")]
    [Range(0f, 15f)] public float fingerIdleSpreadDeg = 1.5f;
    [Range(0f, 45f)] public float fingerMaxSpreadDeg = 12f;
    public FingerAxis fingerSpreadAxis = FingerAxis.Y;
    public bool mirrorRightHand = true;
    [Range(0f, 30f)] public float fingerSpreadRotLerp = 12f;

    [Header("Blend Speeds")]
    [Range(1f, 30f)] public float equipBlendSpeed = 16f;
    [Range(1f, 30f)] public float unequipBlendSpeed = 16f;

    [Header("IK (optional) - Right Hand")]
    public bool useIKRight = false;
    public Transform rightIKTarget;      // e.g., child named "Grip_R" on the item
    public Transform rightElbowHint;     // optional pole/hint (e.g., near right elbow)
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
    }
    [ContextMenu("Disable Finger Overrides")]
    void DisableFingerOverrides()
    {
        overrideFingerSettings = false;
        fingerCurl = 0f; fingerSpread = 0f;
    }
#endif
}
