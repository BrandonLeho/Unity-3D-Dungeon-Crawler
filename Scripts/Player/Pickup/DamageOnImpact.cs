using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class DamageOnImpact : MonoBehaviour
{
    [Header("Damage Model")]
    public float MomentumScale = 1.0f;  // damage ~ m * v * scale
    public float EnergyScale = 0f;      // if > 0, damage ~ 0.5 * mu * v^2 * scale (overrides momentum)
    public float MinHitSpeed = 1.0f;
    public float CritSpeed = 10.0f;

    [Header("Repeat Hit Control")]
    [Tooltip("Minimum time before THIS hitter can damage the SAME target again.")]
    public float SameTargetCooldown = 0.20f;
    [Tooltip("Short grace between any two hits from this hitter (limits multi-contact spam).")]
    public float GlobalCooldown = 0.04f;

    [Header("Ownership / Safety")]
    [Tooltip("If true, never apply damage to the current holder (or their children) while held.")]
    public bool IgnoreOwnerWhileHeld = true;

    [Tooltip("After release/throw, keep ignoring the last holder for this many seconds.")]
    public float PostReleaseOwnerGrace = 0.25f;

    [Tooltip("Layers that can receive damage (optional).")]
    public LayerMask HittableLayers = ~0;

    [Header("Debug")]
    public bool DebugLog;

    Rigidbody rb;

    // --- ownership state ---
    Transform currentOwner;            // non-null while held
    Transform lastOwner;               // who last held it (for post-release grace)
    float lastOwnerReleaseTime;

    // --- cooldowns ---
    float nextGlobalHitTime;
    readonly Dictionary<int, float> nextTimePerTargetId = new Dictionary<int, float>();

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.maxAngularVelocity = Mathf.Max(rb.maxAngularVelocity, 20f);
    }

    /// Call when the player starts holding the object (pass the player/root transform).
    public void SetOwner(Transform owner)
    {
        currentOwner = owner;
        // clear any stale last-owner window, we're now firmly owned again
        lastOwner = null;
    }

    /// Call when the player drops OR throws the object (pass the same player/root).
    public void ClearOwner(Transform owner)
    {
        if (currentOwner == owner)
        {
            lastOwner = currentOwner;
            lastOwnerReleaseTime = Time.time;
            currentOwner = null;
        }
    }

    /// Convenience for your throw path (equivalent to ClearOwner but intent-expressive)
    public void NotifyThrown(Transform owner) => ClearOwner(owner);

    void OnCollisionEnter(Collision c) => TryDamage(c);
    void OnCollisionStay(Collision c) => TryDamage(c);

    void TryDamage(Collision c)
    {
        // layer filter
        if (((1 << c.gameObject.layer) & HittableLayers) == 0) return;

        // never hit the current owner while held
        if (IgnoreOwnerWhileHeld && currentOwner && c.transform.IsChildOf(currentOwner))
            return;

        // short grace against the last owner after release/throw
        if (lastOwner)
        {
            if (Time.time - lastOwnerReleaseTime <= PostReleaseOwnerGrace)
            {
                if (c.transform.IsChildOf(lastOwner)) return;
            }
            else
            {
                // grace expired
                lastOwner = null;
            }
        }

        // global spam limiter
        if (Time.time < nextGlobalHitTime) return;

        // find a damageable on the other object
        IDamageable damageable = c.collider.GetComponentInParent<IDamageable>();
        if (damageable == null) return;

        // per-target cooldown (same hitter → same target)
        int targetKey = GetTargetKey(damageable);
        if (nextTimePerTargetId.TryGetValue(targetKey, out float readyTime) && Time.time < readyTime)
            return;

        // contact-point relative speed (counts spin)
        ContactPoint cp = c.GetContact(0);
        Vector3 hitPoint = cp.point;
        Vector3 hitNormal = cp.normal;

        Vector3 vSelf = rb.GetPointVelocity(hitPoint);
        Vector3 vOther = c.rigidbody ? c.rigidbody.GetPointVelocity(hitPoint) : Vector3.zero;
        Vector3 relVel = vSelf - vOther;
        float relSpeed = relVel.magnitude;

        if (relSpeed < MinHitSpeed) return;

        // damage calc (energy overrides momentum if set)
        float damage;
        if (EnergyScale > 0f)
        {
            float m1 = Mathf.Max(0.01f, rb.mass);
            float m2 = (c.rigidbody && c.rigidbody.mass > 0f) ? c.rigidbody.mass : m1;
            float mu = (m1 * m2) / Mathf.Max(0.01f, m1 + m2); // reduced mass
            damage = 0.5f * mu * relSpeed * relSpeed * EnergyScale;
        }
        else
        {
            damage = Mathf.Max(0.01f, rb.mass) * relSpeed * Mathf.Max(0f, MomentumScale);
        }

        // optional bias toward direct impacts
        float normalFactor = Mathf.Abs(Vector3.Dot(relVel.normalized, hitNormal));
        damage *= Mathf.Lerp(0.8f, 1.2f, normalFactor);

        if (damage <= 0f) return;

        damageable.ApplyDamage(damage, hitPoint, hitNormal, source: this);

        if (DebugLog)
            Debug.Log($"[DamageOnImpact] {name} dealt {damage:F1} dmg to {targetKey} @ {relSpeed:F2} m/s");

        // cooldowns
        nextGlobalHitTime = Time.time + GlobalCooldown;
        nextTimePerTargetId[targetKey] = Time.time + SameTargetCooldown;
    }

    int GetTargetKey(IDamageable dmg)
    {
        if (dmg is Component comp) return comp.gameObject.GetInstanceID();
        return dmg.GetHashCode();
    }
}
