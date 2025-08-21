using UnityEngine;

public class FPCrouch : FPActivity
{
    // Cached components / dimensions (auto-detected)
    CharacterController cc;
    CapsuleCollider capsule;

    float standingHeight;
    float standingRadius;
    Vector3 standingCenterLocal;

    protected override void Awake()
    {
        base.Awake();

        Controller.Crouch = this;

        // Prefer CharacterController if present, else CapsuleCollider
        cc = Controller.GetComponent<CharacterController>();
        capsule = (cc == null) ? Controller.GetComponent<CapsuleCollider>() : null;

        if (cc != null)
        {
            standingHeight = cc.height;          // cache original standing height
            standingRadius = cc.radius;
            standingCenterLocal = cc.center;     // local-space center when standing
        }
        else if (capsule != null)
        {
            standingHeight = capsule.height;
            standingRadius = capsule.radius;
            standingCenterLocal = capsule.center;
        }
        else
        {
            Debug.LogWarning("No CharacterController or CapsuleCollider found on the player. Falling back to defaults.");
            standingHeight = 2.0f;
            standingRadius = 0.3f;
            standingCenterLocal = Vector3.up * (standingHeight * 0.5f);
        }
    }

    public override bool CanStopActivty()
    {
        // Build the would-be standing capsule in WORLD space
        // using the cached standing dimensions (no hardcoded numbers).
        Transform t = (cc != null) ? cc.transform :
                      (capsule != null ? capsule.transform : Controller.transform);

        // Standing center position in world space
        Vector3 worldCenter = t.TransformPoint(standingCenterLocal);

        // Compute capsule endpoints (top/bottom) along the character's up axis.
        Vector3 up = t.up;
        float half = Mathf.Max(standingHeight * 0.5f, standingRadius); // ensure height >= 2*radius

        Vector3 bottom = worldCenter - up * (half - standingRadius);
        Vector3 top = worldCenter + up * (half - standingRadius);

        // Check if that capsule would overlap any obstacle.
        // If true => NOT enough room to stand up.
        bool blocked = Physics.CheckCapsule(
            bottom, top, standingRadius,
            Preset.ObstacleLayerMask,            // your existing mask
            QueryTriggerInteraction.Ignore
        );

        return !blocked; // can stop crouching only if not blocked
    }
}
