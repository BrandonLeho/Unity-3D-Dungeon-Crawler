using UnityEngine;

[System.Flags]
public enum RagdollTriggerFlags
{
    None = 0,
    OnCrit = 1 << 0,
    OnDeath = 1 << 1,
    Always = 1 << 2,     // (future) force ragdoll regardless
    BigHit = 1 << 3      // (future) e.g., amount >= threshold
}

public struct DamageInfo
{
    public float amount;
    public Vector3 point;     // world-space hit point (if known)
    public Vector3 normal;    // world-space hit normal (if known)
    public Vector3 impulse;   // world-space knockback impulse to apply to ragdoll
    public bool isCrit;       // attacker can pre-mark; Actor will also auto-mark by amount if you want
    public object source;     // attacker or weapon
    public RagdollTriggerFlags ragdollWhen; // e.g., OnCrit | OnDeath
}
