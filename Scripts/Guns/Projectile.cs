using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
public class Projectile : MonoBehaviour
{
    [Header("Kinematics")]
    [SerializeField] float speed = 40f;
    [SerializeField] bool useGravity = false;
    [SerializeField] float gravityScale = 1.0f;
    [SerializeField] float lifeTime = 6f;
    [SerializeField] float drag = 0f;
    [SerializeField] float angularDrag = 0f;

    [Header("Hit Detection")]
    [Tooltip("Radius of the projectile’s collision (0 = ray-like).")]
    [SerializeField] float hitRadius = 0.05f;
    [SerializeField] LayerMask hitMask = ~0;
    [SerializeField] QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Damage / Impact")]
    [SerializeField] float damage = 10f;
    [SerializeField] bool explode = false;
    [SerializeField] float explosionDamage = 40f;
    [SerializeField] float explosionRadius = 4f;
    [SerializeField] AnimationCurve explosionFalloff = AnimationCurve.Linear(0, 1, 1, 0);
    [SerializeField] bool explodeOnCollision = true;

    // --- Direct-Hit Damage Falloff ---
    [Header("Direct-Hit Damage Falloff")]
    [SerializeField] bool useDamageFalloff = false;
    [Tooltip("Meters from spawn where falloff begins.")]
    [SerializeField] float falloffStart = 10f;
    [Tooltip("Meters from spawn where falloff reaches its minimum multiplier (clamped by minMultiplier).")]
    [SerializeField] float falloffEnd = 40f;
    [Tooltip("Distance 0..1 (remapped from [falloffStart..falloffEnd]) → damage multiplier.")]
    [SerializeField] AnimationCurve falloffCurve = AnimationCurve.Linear(0, 1, 1, 0.5f);
    [Tooltip("Never drop below this fraction of base damage.")]
    [SerializeField, Range(0f, 1f)] float minMultiplier = 0.1f;

    [Header("Penetration / Collaterals")]
    [SerializeField] bool allowPenetration = true;
    [SerializeField, Min(0)] int maxPenetrations = 1;          // how many extra targets after first
    [SerializeField, Range(0f, 1f)] float penetrationDampen = 0.7f; // speed multiplier after each pass-through
    [SerializeField] float penetrationAdvance = 0.03f;         // how far to teleport beyond a hit so we don’t re-hit the same collider
    [SerializeField] LayerMask penetrationMask = ~0;           // what can be penetrated (e.g. flesh/wood; exclude thick steel)
    [SerializeField] float minSpeedAfterPen = 5f;              // stop if speed drops below this

    [Header("Ricochet / Bounce")]
    [SerializeField] bool ricochet = false;
    [SerializeField] int maxRicochets = 2;
    [Range(0f, 1f)][SerializeField] float bounciness = 0.6f; // normal reflection scale
    [SerializeField] float ricochetSpeedDampen = 0.8f;       // lose speed per bounce

    [Header("FX (optional)")]
    [SerializeField] GameObject hitVfx;
    [SerializeField] GameObject explosionVfx;
    [SerializeField] TrailRenderer trail;

    Rigidbody rb;
    int ricochets;
    float deathTime;
    bool alive = true;
    object damageSource;

    public float Damage
    {
        get => damage;
        set => damage = value;
    }

    Vector3 spawnPosition;
    float traveledDistance;

    int penetrationsLeft;



    public void Launch(Vector3 direction, float extraUpwardForce, float initialSpeed, object source)
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearDamping = drag;
        rb.angularDamping = angularDrag;
        damageSource = source;
        penetrationsLeft = maxPenetrations;

        Vector3 v = direction.normalized * (initialSpeed > 0 ? initialSpeed : speed);
        if (extraUpwardForce != 0f) v += Vector3.up * extraUpwardForce;
        rb.linearVelocity = v;

        spawnPosition = transform.position;
        traveledDistance = 0f;

        deathTime = Time.time + lifeTime;
        alive = true;
    }


    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        if (!alive) return;
        if (Time.time >= deathTime)
        {
            if (explode) DoExplosion(transform.position, Vector3.up);
            Destroy(gameObject);
        }
    }

    void FixedUpdate()
    {
        if (!alive) return;

        // Continuous hit check (handles fast movers), sphere if radius > 0
        float dist = rb.linearVelocity.magnitude * Time.fixedDeltaTime + 0.01f;
        if (dist <= 0f) return;

        // Distance advanced this step if no hit occurs (approx).
        float stepDistance = rb.linearVelocity.magnitude * Time.fixedDeltaTime;

        Ray ray = new Ray(transform.position, rb.linearVelocity.normalized);
        bool hitSomething;
        RaycastHit hit;

        if (hitRadius > 0f)
            hitSomething = Physics.SphereCast(ray, hitRadius, out hit, dist, hitMask, triggerInteraction);
        else
            hitSomething = Physics.Raycast(ray, out hit, dist, hitMask, triggerInteraction);

        if (useGravity)
        {
            rb.AddForce(Physics.gravity * gravityScale, ForceMode.Acceleration);
        }

        if (hitSomething)
        {
            // accumulate distance precisely to contact
            traveledDistance += hit.distance;
            OnImpact(hit);
        }
        else
        {
            traveledDistance += stepDistance;
        }

    }

    void OnImpact(in RaycastHit hit)
    {
        // ----- Direct-hit damage (with your falloff logic if you added it) -----
        float appliedDamage = damage;
        // (If you added damage falloff earlier, keep that block here)
        var id = hit.collider.GetComponentInParent<IDamageable>();
        if (id != null) id.ApplyDamage(appliedDamage, hit.point, hit.normal, damageSource);

        if (hitVfx) Instantiate(hitVfx, hit.point, Quaternion.LookRotation(hit.normal));

        // ----- Explosion-on-collision overrides everything -----
        if (explode && explodeOnCollision)
        {
            DoExplosion(hit.point, hit.normal);
            Destroy(gameObject);
            return;
        }

        // ----- Penetration: pass through, lose speed, continue flight -----
        if (allowPenetration && penetrationsLeft > 0 && IsPenetrable(hit.collider))
        {
            penetrationsLeft--;

            // reduce speed; stop if too slow
            Vector3 v = rb.linearVelocity;
            v *= penetrationDampen;
            if (v.magnitude < minSpeedAfterPen)
            {
                Destroy(gameObject);
                return;
            }
            rb.linearVelocity = v;

            // move a tiny step past the surface so we don't immediately hit the same collider again
            Vector3 dir = v.sqrMagnitude > 0f ? v.normalized : transform.forward;
            float advance = Mathf.Max(penetrationAdvance, hitRadius * 0.5f);
            transform.position = hit.point + dir * advance;

            // continue this frame; do NOT ricochet/destroy
            return;
        }

        // ----- Otherwise, do your original ricochet (if enabled) -----
        if (ricochet && ricochets < maxRicochets)
        {
            ricochets++;
            Vector3 v = rb.linearVelocity;
            Vector3 r = Vector3.Reflect(v, hit.normal) * (bounciness);
            rb.linearVelocity = r * ricochetSpeedDampen;

            // nudge away from surface
            transform.position = hit.point + hit.normal * Mathf.Max(0.001f, hitRadius * 0.5f);
            return;
        }

        // ----- Default: stop -----
        Destroy(gameObject);
    }

    bool IsPenetrable(Collider col)
    {
        // Only allow penetration if the collider’s layer is in the mask
        return ((penetrationMask.value & (1 << col.gameObject.layer)) != 0);
    }



    void DoExplosion(Vector3 pos, Vector3 normal)
    {
        if (explosionVfx) Instantiate(explosionVfx, pos, Quaternion.LookRotation(normal));

        var cols = Physics.OverlapSphere(pos, explosionRadius, hitMask, triggerInteraction);
        HashSet<IDamageable> damaged = new();
        foreach (var c in cols)
        {
            var d = c.GetComponentInParent<IDamageable>();
            if (d == null || damaged.Contains(d)) continue;

            Vector3 cp = c.ClosestPoint(pos);
            float t = Mathf.Clamp01(Vector3.Distance(cp, pos) / Mathf.Max(0.001f, explosionRadius));
            float mult = explosionFalloff.Evaluate(t);
            d.ApplyDamage(explosionDamage * mult, cp, (cp - pos).normalized, damageSource);
            damaged.Add(d);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (hitRadius > 0f)
        {
            Gizmos.color = new Color(1, 0.5f, 0f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, hitRadius);
        }
    }
}
