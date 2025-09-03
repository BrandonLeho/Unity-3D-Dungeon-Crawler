using UnityEngine;
using UnityEngine.Events;

/// Attach this to any Rigidbody you want to be able to BREAK when it hits hard.
/// The required break speed INCREASES with the object's mass (tunable curve).
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class BreakableOnImpact : MonoBehaviour, IBreakable
{
    [Header("Break Threshold (Speed)")]
    [Tooltip("Base speed (m/s) required to break at 1 kg of mass.")]
    public float BaseBreakSpeed = 4.0f;

    [Tooltip("Extra required speed per kg^Exponent. Positive => heavier needs more speed.")]
    public float BreakSpeedPerKg = 0.35f;

    [Tooltip("Exponent used in the speed curve. 0.5 = sqrt(mass), 1.0 = linear with mass, etc.")]
    [Range(0.2f, 2f)] public float MassExponent = 0.5f;

    [Tooltip("If > 0, overrides Rigidbody.mass when computing threshold (useful for composite objects).")]
    public float MassOverride = 0f;

    [Header("Filters")]
    [Tooltip("Minimum relative speed at contact point needed to even evaluate break.")]
    public float MinConsiderSpeed = 0.5f;

    [Tooltip("Only these layers can trigger a break (0 = everything).")]
    public LayerMask BreakOnLayers = ~0;

    [Tooltip("Ignore hits coming from my current owner (e.g., the player) for a brief window, if provided.")]
    public float OwnerGraceTime = 0.2f;

    [Header("Cooldowns")]
    [Tooltip("Small delay so one crash doesn't cause repeated break calls across many contacts.")]
    public float SelfCooldown = 0.05f;

    [Header("On Break")]
    public bool DestroySelf = true;
    [Tooltip("Optional replacement prefab (debris) spawned on break.")]
    public GameObject ReplaceWithPrefab;
    [Tooltip("Spawn offset from hit point (to avoid z-fighting).")]
    public float SpawnOffsetAlongNormal = 0.02f;
    public float DestroyDelay = 0f;
    public UnityEvent OnBroken;

    [Header("Debug")]
    public bool DebugLog;

    // --- runtime ---
    public bool IsBroken { get; private set; }
    public Transform CurrentOwner { get; private set; }
    float ownerSetTime;
    float nextSelfCheckTime;

    Rigidbody rb;
    Collider col;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();

        // sensible physics defaults for crash interactions
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        // ensure collider is enabled
        col.enabled = true;
    }

    /// Call this from your pickup/throw code (same way as DamageOnImpact.SetOwner/NotifyThrown)
    public void SetOwner(Transform owner)
    {
        CurrentOwner = owner;
        ownerSetTime = Time.time;
    }

    /// Call when throwing (keeps grace so you don’t immediately break on yourself)
    public void NotifyThrown(Transform owner)
    {
        CurrentOwner = owner;
        ownerSetTime = Time.time;
    }

    void OnCollisionEnter(Collision c) => TryProcessCollision(c);
    void OnCollisionStay(Collision c) => TryProcessCollision(c);

    void TryProcessCollision(Collision c)
    {
        if (IsBroken || Time.time < nextSelfCheckTime) return;
        if (((1 << c.gameObject.layer) & BreakOnLayers) == 0) return;

        // Ignore our owner during grace
        if (CurrentOwner && (Time.time - ownerSetTime) < OwnerGraceTime)
        {
            if (c.transform.IsChildOf(CurrentOwner)) return;
        }

        // Compute relative speed at the FIRST contact point (spin counts!)
        ContactPoint cp = c.GetContact(0);
        Vector3 hitPoint = cp.point;
        Vector3 hitNormal = cp.normal;

        Vector3 vSelf = rb.GetPointVelocity(hitPoint);
        Vector3 vOther = c.rigidbody ? c.rigidbody.GetPointVelocity(hitPoint) : Vector3.zero;
        float relSpeed = (vSelf - vOther).magnitude;

        if (relSpeed < MinConsiderSpeed) return;

        // My personal break threshold (speed increases with my mass)
        float myMass = MassOverride > 0f ? MassOverride : Mathf.Max(0.01f, rb.mass);
        float requiredSpeed = BreakSpeedRequired(myMass);

        if (relSpeed >= requiredSpeed)
        {
            // We break
            Break(hitPoint, hitNormal, source: c.collider);
            return;
        }

        // Also try to break the other object if THEY are breakable (they will likely
        // process their own collision too, but this gives a one-sided push if needed).
        var otherBreakable = c.collider.GetComponentInParent<BreakableOnImpact>();
        if (otherBreakable != null && !otherBreakable.IsBroken)
        {
            // Check THEIR threshold against the SAME relSpeed
            float otherMass = otherBreakable.MassOverride > 0f
                ? otherBreakable.MassOverride
                : (otherBreakable.TryGetComponent<Rigidbody>(out var orb) ? Mathf.Max(0.01f, orb.mass) : 1f);

            float otherRequired = otherBreakable.BreakSpeedRequired(otherMass);
            if (relSpeed >= otherRequired)
            {
                otherBreakable.Break(hitPoint, -hitNormal, source: col);
            }
        }

        nextSelfCheckTime = Time.time + SelfCooldown;
    }

    public void Break(Vector3 hitPoint, Vector3 hitNormal, object source = null)
    {
        if (IsBroken) return;
        IsBroken = true;

        if (DebugLog)
        {
            float m = MassOverride > 0f ? MassOverride : rb.mass;
            Debug.Log($"[BreakableOnImpact] {name} BROKE (mass={m:F2})");
        }

        OnBroken?.Invoke();

        if (ReplaceWithPrefab)
        {
            Vector3 spawnPos = hitPoint + hitNormal * SpawnOffsetAlongNormal;
            Quaternion spawnRot = Quaternion.LookRotation(hitNormal);
            Instantiate(ReplaceWithPrefab, spawnPos, spawnRot);
        }

        if (DestroySelf)
        {
            // Disable collider immediately to avoid extra interactions
            if (col) col.enabled = false;

            // Optional: detach visual children before destroy if you want them to persist
            Destroy(gameObject, Mathf.Max(0f, DestroyDelay));
        }
    }

    /// Speed needed to break at a given mass (monotonically increasing with mass).
    /// v_required(m) = BaseBreakSpeed + BreakSpeedPerKg * m^Exponent
    public float BreakSpeedRequired(float massKg)
    {
        return BaseBreakSpeed + BreakSpeedPerKg * Mathf.Pow(Mathf.Max(0.01f, massKg), MassExponent);
    }
}
