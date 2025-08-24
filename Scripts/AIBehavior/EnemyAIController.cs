using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using AIToolkit;

public enum AIState { Idle, Investigate, Hunt }

[RequireComponent(typeof(VisionSensor))]
[RequireComponent(typeof(HearingSensor))]
[RequireComponent(typeof(EnemyNavigator))]
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAIController : MonoBehaviour
{
    [Header("Suspicion")]
    [SerializeField, Range(0f, 100f)] float suspicion = 0f;
    [SerializeField] float suspicionDecayPerSecond = 6f;
    [SerializeField] float visionGain = 45f;   // suspicion += strength * gain
    [SerializeField] float hearingGain = 25f;

    [SerializeField, Range(0f, 100f)] float investigateThreshold = 15f;
    [SerializeField, Range(0f, 100f)] float huntThreshold = 55f;

    [Header("Memory / Timers")]
    [SerializeField] float forgetSeenAfter = 4.0f;
    [SerializeField] float forgetHeardAfter = 6.0f;
    [SerializeField] int breadcrumbsMax = 12;
    [SerializeField] float breadcrumbInterval = 0.8f;

    [Header("Engagement Distances")]
    [SerializeField] bool usesRangedWeapon = false;

    [Tooltip("Where melee should stop (edge-to-edge), e.g., agent radius + player radius + small margin.")]
    [SerializeField, Min(0f)] float meleeStopDistance = 1.5f;

    [Tooltip("Ranged units keep between these distances.")]
    [SerializeField, Min(0f)] float rangedMinDistance = 6f;
    [SerializeField, Min(0f)] float rangedMaxDistance = 12f;

    [SerializeField] bool orbitWhenInRange = true; // strafe/orbit while in “good” range

    [Header("Target Lock / Proximity")]
    [SerializeField] float targetLockGrace = 1.25f; // seconds to keep tracking after LOS lost
    [SerializeField] float proximityRadius = 2.5f;  // 360° “body sense” radius
    [SerializeField] float proximityGain = 30f;     // suspicion bump if target enters radius
    [SerializeField] LayerMask targetMask;          // same as VisionSensor.targetMask
    [SerializeField] LayerMask proximityObstructionMask; // optional; empty = ignore occlusion

    [Header("Facing / Turning")]
    [SerializeField] bool manualTurnWhenNear = true;
    [SerializeField] float turnSpeedDegPerSec = 540f; // how fast to turn
    [SerializeField] float faceWhenWithin = 20f;      // start turning if target within this range


    [Header("Weights")]
    [SerializeField] float preferRecentSeenSec = 2.0f; // how strongly we prefer fresh vision over stale hearing

    [Header("Debug")]
    [SerializeField] bool drawGizmos = true;

    // Components
    VisionSensor vision;
    HearingSensor hearing;
    EnemyNavigator nav;
    NavMeshAgent agent;

    // Blackboard (memory)
    class Blackboard
    {
        public Transform CurrentTarget;
        public Vector3 LastKnownPos;
        public float LastSeenTime;
        public Vector3 LastSeenVelocity;

        public Vector3 LastHeardPos;
        public float LastHeardTime;

        public Vector3 PredictedPos;

        public readonly Queue<Vector3> Breadcrumbs = new();
        float lastCrumbTime;

        public void PushBreadcrumb(Vector3 p, float now, int max)
        {
            if (now - lastCrumbTime < 0.01f) return;
            Breadcrumbs.Enqueue(p);
            while (Breadcrumbs.Count > max) Breadcrumbs.Dequeue();
            lastCrumbTime = now;
        }
    }
    Blackboard bb = new();

    AIState state = AIState.Idle;

    float nextPlanTime;

    void Awake()
    {
        vision = GetComponent<VisionSensor>();
        hearing = GetComponent<HearingSensor>();
        nav = GetComponent<EnemyNavigator>();
        agent = GetComponent<NavMeshAgent>();
        vision.OnSignal += HandleSignal;
        hearing.OnSignal += HandleSignal;
    }

    void OnDestroy()
    {
        if (vision) vision.OnSignal -= HandleSignal;
        if (hearing) hearing.OnSignal -= HandleSignal;
    }

    void Update()
    {
        // Suspicion decays over time
        suspicion = Mathf.Max(0f, suspicion - suspicionDecayPerSecond * Time.deltaTime);

        // Cull stale memories
        float now = Time.time;
        if (now - bb.LastSeenTime > forgetSeenAfter) { bb.CurrentTarget = null; bb.LastSeenVelocity = Vector3.zero; }
        if (now - bb.LastHeardTime > forgetHeardAfter) { /* keep pos but lower usefulness */ }

        // State transitions
        AIState newState = state;
        if (suspicion >= huntThreshold) newState = AIState.Hunt;
        else if (suspicion >= investigateThreshold) newState = AIState.Investigate;
        else newState = AIState.Idle;

        if (newState != state)
        {
            state = newState;
            nextPlanTime = 0f; // replan immediately
        }

        // Periodic planning to avoid thrash
        if (Time.time >= nextPlanTime)
        {
            Plan();
            nextPlanTime = Time.time + 0.15f;
        }

        // Drop breadcrumbs while moving
        if (agent && agent.velocity.sqrMagnitude > 0.2f * 0.2f)
            bb.PushBreadcrumb(transform.position, now, breadcrumbsMax);

        MaintainTargetLock();
        ProximitySense();
        FaceTargetIfNeeded();
    }

    void HandleSignal(AISignal sig)
    {
        switch (sig.Type)
        {
            case SignalType.Vision:
                suspicion = Mathf.Min(100f, suspicion + sig.Strength * visionGain);
                bb.CurrentTarget = sig.Source;
                bb.LastKnownPos = sig.Position;
                bb.PredictedPos = sig.Position + sig.Velocity * 0.5f; // basic lead
                bb.LastSeenVelocity = sig.Velocity;
                bb.LastSeenTime = sig.Time;
                break;

            case SignalType.Hearing:
                suspicion = Mathf.Min(100f, suspicion + sig.Strength * hearingGain);
                // Only overwrite current target if we have none or sight is stale
                if (!bb.CurrentTarget || (Time.time - bb.LastSeenTime) > preferRecentSeenSec)
                    bb.CurrentTarget = sig.Source; // can be null (anonymous noise)
                bb.LastHeardPos = sig.Position;
                bb.LastHeardTime = sig.Time;
                if (bb.LastSeenTime <= 0f) bb.LastKnownPos = sig.Position; // seed
                break;
        }
    }

    void Plan()
    {
        switch (state)
        {
            case AIState.Idle:
                nav.Idle();
                // Optional: hook patrol here later
                break;

            case AIState.Investigate:
                {
                    // Prefer last seen if recent, else last heard
                    float tSeen = Time.time - bb.LastSeenTime;
                    float tHeard = Time.time - bb.LastHeardTime;

                    if (bb.LastSeenTime > 0f && tSeen <= forgetSeenAfter)
                    {
                        // Go peek around their last seen spot
                        nav.PeekAround(bb.LastKnownPos);
                    }
                    else if (bb.LastHeardTime > 0f && tHeard <= forgetHeardAfter)
                    {
                        nav.Investigate(bb.LastHeardPos);
                    }
                    else
                    {
                        // Lost both: search where we last *knew* something
                        if (bb.LastKnownPos != Vector3.zero)
                            nav.StartSearchSpiral(bb.LastKnownPos);
                        else
                            nav.Idle();
                    }
                    break;
                }

            case AIState.Hunt:
                {
                    float fresh = Time.time - bb.LastSeenTime;

                    if (bb.CurrentTarget != null && bb.LastSeenTime > 0f && fresh < 1.0f)
                    {
                        if (usesRangedWeapon)
                        {
                            nav.KeepDistanceTo(bb.CurrentTarget, rangedMinDistance, rangedMaxDistance, orbitWhenInRange);
                        }
                        else
                        {
                            nav.KeepDistanceTo(bb.CurrentTarget, meleeStopDistance, meleeStopDistance + 0.25f, orbitWhenInRange);
                        }
                    }
                    else if (bb.LastKnownPos != Vector3.zero)
                    {
                        nav.Investigate(bb.LastKnownPos);
                    }
                    else if (bb.LastHeardTime > 0f)
                    {
                        nav.Investigate(bb.LastHeardPos);
                    }
                    else
                    {
                        nav.StartSearchSpiral(transform.position);
                    }
                    break;
                }
        }
    }

    void MaintainTargetLock()
    {
        // If we saw a specific Transform recently, keep tracking it even if FOV lost.
        if (bb.CurrentTarget == null) return;

        float sinceSeen = Time.time - bb.LastSeenTime;
        if (sinceSeen <= targetLockGrace)
        {
            // “Sticky vision”: keep last known at the *actual* transform position
            bb.LastKnownPos = bb.CurrentTarget.position;

            // Optional: keep suspicion from instantly decaying
            suspicion = Mathf.Min(100f, suspicion + Time.deltaTime * 5f);

            // Predict a little lead while locked
            var rb = bb.CurrentTarget.GetComponentInParent<Rigidbody>();
            if (rb) { bb.PredictedPos = bb.CurrentTarget.position + rb.linearVelocity * 0.5f; }
        }
    }

    void ProximitySense()
    {
        if (proximityRadius <= 0f) return;

        var hits = Physics.OverlapSphere(transform.position, proximityRadius, targetMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return;

        foreach (var c in hits)
        {
            // Optional simple occlusion: ignore if a wall is between us
            if (proximityObstructionMask.value != 0)
            {
                if (Physics.Linecast(transform.position + Vector3.up * 1.6f,
                                    c.transform.position + Vector3.up * 1.0f,
                                    proximityObstructionMask, QueryTriggerInteraction.Ignore))
                    continue;
            }

            // Treat as a strong "vision-like" ping
            bb.CurrentTarget = c.transform;
            bb.LastKnownPos = c.transform.position;
            bb.LastSeenTime = Time.time;

            // Bump suspicion (stronger up close)
            float dist = Vector3.Distance(transform.position, c.transform.position);
            float strength01 = 1f - Mathf.Clamp01(dist / proximityRadius);
            suspicion = Mathf.Min(100f, suspicion + strength01 * proximityGain);
            break; // one is enough
        }
    }

    void FaceTargetIfNeeded()
    {
        if (!manualTurnWhenNear || bb.CurrentTarget == null) return;

        Vector3 to;
        float dist;

        // Prefer exact Transform while we have a lock, else last known
        if (Time.time - bb.LastSeenTime <= targetLockGrace)
        {
            to = bb.CurrentTarget.position - transform.position;
            dist = to.magnitude;
        }
        else if (bb.LastKnownPos != Vector3.zero)
        {
            to = bb.LastKnownPos - transform.position;
            dist = to.magnitude;
        }
        else return;

        to.y = 0f; if (to.sqrMagnitude < 0.0001f) return;

        if (dist <= faceWhenWithin || state == AIState.Hunt)
        {
            // Let us rotate even when agent has stopped
            if (agent) agent.updateRotation = false;

            Quaternion targetRot = Quaternion.LookRotation(to.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRot, turnSpeedDegPerSec * Time.deltaTime);
        }
        else
        {
            if (agent) agent.updateRotation = true; // let NavMesh steer at long range
        }
    }


    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        // Suspicion bar
        var p = transform.position + Vector3.up * 2.2f;
        float w = 1.6f;
        Gizmos.color = Color.black;
        Gizmos.DrawCube(p, new Vector3(w, 0.08f, 0.02f));
        Gizmos.color = Color.Lerp(Color.green, Color.red, suspicion / 100f);
        Gizmos.DrawCube(p + Vector3.left * (w * 0.5f - (suspicion / 100f) * w * 0.5f), new Vector3((suspicion / 100f) * w, 0.06f, 0.01f));

        // Memory pins
        if (bb.LastKnownPos != Vector3.zero)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(bb.LastKnownPos, 0.15f);
        }
        if (bb.LastHeardTime > 0f)
        {
            Gizmos.color = new Color(1f, 0.75f, 0f, 1f);
            Gizmos.DrawSphere(bb.LastHeardPos, 0.12f);
        }
        if (bb.PredictedPos != Vector3.zero)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(bb.PredictedPos, 0.2f);
        }

        // Breadcrumbs
        Gizmos.color = new Color(0f, 0.6f, 1f, 0.8f);
        foreach (var c in bb.Breadcrumbs) Gizmos.DrawSphere(c + Vector3.up * 0.05f, 0.06f);
    }
}
