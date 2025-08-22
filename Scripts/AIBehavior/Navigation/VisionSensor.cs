using System.Collections.Generic;
using UnityEngine;
using AIToolkit;

[DisallowMultipleComponent]
public class VisionSensor : MonoBehaviour
{
    [Header("Vision")]
    [SerializeField] float viewRadius = 25f;
    [SerializeField, Range(0f, 180f)] float viewAngle = 75f;
    [SerializeField] LayerMask targetMask;          // Players
    [SerializeField] LayerMask obstructionMask;     // Walls, cover
    [SerializeField] float checkInterval = 0.1f;
    [Tooltip("Penalty to strength at the FOV edge (0 strong, 1 harsh).")]
    [SerializeField, Range(0f, 1f)] float edgeFalloff = 0.4f;

    [Header("Output")]
    public System.Action<AISignal> OnSignal;

    [Header("Debug")]
    [SerializeField] bool drawGizmos = false;

    float timer;

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= checkInterval)
        {
            timer = 0f;
            Scan();
        }
    }

    void Scan()
    {
        var hits = Physics.OverlapSphere(transform.position, viewRadius, targetMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return;

        Vector3 eye = transform.position;
        Vector3 fwd = transform.forward;

        foreach (var c in hits)
        {
            Transform t = c.transform;
            Vector3 to = t.position - eye;
            float dist = to.magnitude;
            if (dist <= 0.001f) continue;

            Vector3 dir = to / dist;
            float angle = Vector3.Angle(fwd, dir);
            if (angle > viewAngle) continue;

            // Line-of-sight
            if (Physics.Raycast(eye, dir, out RaycastHit block, dist, obstructionMask, QueryTriggerInteraction.Ignore))
                continue;

            // Strength: closer + more central = stronger
            float distFactor = 1f - Mathf.Clamp01(dist / viewRadius);
            float angleNorm = angle / viewAngle; // 0 center .. 1 edge
            float angleFactor = 1f - Mathf.Lerp(0f, edgeFalloff, angleNorm);

            float strength = Mathf.Clamp01(distFactor * angleFactor);

            Vector3 vel = Vector3.zero;
            var rb = t.GetComponentInParent<Rigidbody>();
            if (rb) vel = rb.linearVelocity;

            OnSignal?.Invoke(new AISignal(SignalType.Vision, t, t.position, vel, strength, Time.time));
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        Gizmos.color = new Color(0f, 1f, 0f, 0.15f);
        Gizmos.DrawWireSphere(transform.position, viewRadius);

        Vector3 a = DirFromAngle(-viewAngle);
        Vector3 b = DirFromAngle(+viewAngle);
        Gizmos.color = new Color(0f, 1f, 0f, 0.35f);
        Gizmos.DrawLine(transform.position, transform.position + a * viewRadius);
        Gizmos.DrawLine(transform.position, transform.position + b * viewRadius);
    }

    Vector3 DirFromAngle(float degrees) =>
        Quaternion.Euler(0f, degrees, 0f) * transform.forward;
}
