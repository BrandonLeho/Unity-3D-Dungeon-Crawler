using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum NavMode { Idle, Peek, Investigate, SearchSpiral, Intercept }

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyNavigator : MonoBehaviour
{
    [Header("Movement")]
    public NavMeshAgent agent;
    [SerializeField] float peekRadius = 2.5f;
    [SerializeField] int peekSamples = 8;
    [SerializeField] float searchSpiralStep = 2.0f;
    [SerializeField] int searchSpiralMaxSteps = 12;

    [Header("Personal Space / Ranges")]
    [SerializeField, Min(0f)] float defaultPersonalSpace = 1.25f; // melee stop distance fallback
    [SerializeField] float orbitSpeed = 1.5f;   // radians/sec-ish
    [SerializeField] float orbitJitter = 0.6f;  // randomize the orbit a bit
    [SerializeField] float sampleMaxDistance = 2.0f; // NavMesh.SamplePosition search radius


    [Header("Debug")]
    [SerializeField] bool drawGizmos = true;

    NavMode mode = NavMode.Idle;
    Vector3 anchor;
    int spiralIndex;
    readonly List<Vector3> spiralCache = new();

    float orbitPhase; // unique per enemy
    void Awake() { orbitPhase = Random.value * 10f; }


    void Reset()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.stoppingDistance = 0.2f;
        agent.autoRepath = true;
    }

    public void Idle() { mode = NavMode.Idle; if (agent) agent.ResetPath(); }

    public void PeekAround(Vector3 targetPos)
    {
        mode = NavMode.Peek;
        Vector3 best = ComputePeekPoint(targetPos);
        if (best != Vector3.zero) agent?.SetDestination(best);
    }

    public void Investigate(Vector3 pos)
    {
        mode = NavMode.Investigate;
        anchor = pos;
        agent?.SetDestination(anchor);
    }

    public void StartSearchSpiral(Vector3 center)
    {
        mode = NavMode.SearchSpiral;
        anchor = center;
        spiralIndex = 0;
        spiralCache.Clear();
        // precompute spiral waypoints
        float angle = 0f, step = searchSpiralStep;
        for (int i = 0; i < searchSpiralMaxSteps; i++)
        {
            float r = (i + 1) * step;
            angle += 137.5f * Mathf.Deg2Rad; // golden angle
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * r;
            Vector3 p = center + offset;
            if (NavMesh.SamplePosition(p, out var hit, 2.0f, NavMesh.AllAreas))
                spiralCache.Add(hit.position);
        }
        if (spiralCache.Count > 0) agent?.SetDestination(spiralCache[0]);
    }

    public void Intercept(Vector3 targetPos, Vector3 targetVel, float selfSpeed)
    {
        mode = NavMode.Intercept;
        Vector3 intercept = ComputeInterceptPoint(transform.position, selfSpeed, targetPos, targetVel);
        agent?.SetDestination(intercept);
    }

    void Update()
    {
        if (agent == null || !agent.isOnNavMesh) return;

        switch (mode)
        {
            case NavMode.SearchSpiral:
                if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
                {
                    spiralIndex++;
                    if (spiralIndex < spiralCache.Count)
                        agent.SetDestination(spiralCache[spiralIndex]);
                    else
                        Idle();
                }
                break;
            case NavMode.Peek:
                // If we reached peek, go idle; brain can reissue if needed
                if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
                    Idle();
                break;
        }
    }

    Vector3 ComputePeekPoint(Vector3 targetPos)
    {
        // Sample points on a ring around the agent facing the target and pick a spot with a clear LOS
        Vector3 origin = transform.position;
        Vector3 toTarget = (targetPos - origin);
        toTarget.y = 0f;
        float baseAngle = Mathf.Atan2(toTarget.x, toTarget.z);

        Vector3 best = Vector3.zero;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < peekSamples; i++)
        {
            float a = baseAngle + (i / (float)peekSamples) * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            Vector3 p = origin + dir * peekRadius;

            if (!NavMesh.SamplePosition(p, out var hit, 1.5f, NavMesh.AllAreas)) continue;
            float score = Vector3.Dot(dir, toTarget.normalized); // prefer facing target
            if (score > bestScore) { bestScore = score; best = hit.position; }
        }
        return best;
    }

    static Vector3 ComputeInterceptPoint(Vector3 shooterPos, float shooterSpeed, Vector3 targetPos, Vector3 targetVel)
    {
        // Simple lead: assume constant vel, solve t ~ distance / (speed+eps)
        float eps = 0.01f;
        float timeToReach = Vector3.Distance(shooterPos, targetPos) / Mathf.Max(shooterSpeed, eps);
        return targetPos + targetVel * timeToReach;
    }

    bool SampleAndSet(Vector3 worldPos)
    {
        if (NavMesh.SamplePosition(worldPos, out var hit, sampleMaxDistance, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
            return true;
        }
        return false;
    }

    public void KeepDistanceTo(Transform target, float minDistance, float maxDistance, bool orbitInBand)
    {
        if (!agent || !target) return;

        // Clamp and set stopping distance so the agent stops *before* touching
        minDistance = Mathf.Max(minDistance, 0.01f);
        if (maxDistance < minDistance) maxDistance = minDistance;
        agent.stoppingDistance = minDistance;

        Vector3 me = transform.position;
        Vector3 tp = target.position;
        Vector3 flatDir = (tp - me); flatDir.y = 0f;
        float d = flatDir.magnitude;

        // Choose a desired point on the ring around the target at 'minDistance'
        if (d < minDistance - 0.05f)
        {
            // Too close → back up to ring
            Vector3 dir = (me - tp).normalized;
            Vector3 dest = tp + dir * minDistance;
            SampleAndSet(dest);
            return;
        }
        if (d > maxDistance + 0.05f)
        {
            // Too far → move in to ring
            Vector3 dir = (tp - me).normalized;
            Vector3 dest = tp - dir * minDistance; // aim just at the inner edge
            SampleAndSet(dest);
            return;
        }

        // In the distance band → either hold or orbit
        if (orbitInBand)
        {
            float radius = Mathf.Lerp(minDistance, maxDistance, 0.6f);
            float a = orbitPhase + Time.time * orbitSpeed;
            // wobble the radius/angle for less robotic motion
            radius += Mathf.Sin(Time.time * 1.7f + orbitPhase) * orbitJitter * 0.25f;
            float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            Vector3 offset = new Vector3(ca, 0f, sa) * radius;
            Vector3 dest = tp + offset;

            // steer to the orbit point but don't fight if we already have a good path
            if (!agent.hasPath || agent.remainingDistance > agent.stoppingDistance + 0.25f)
                SampleAndSet(dest);
        }
        else
        {
            // Just hold position (let combat system handle facing/attacks)
            if (agent.hasPath && agent.remainingDistance <= agent.stoppingDistance + 0.05f)
                agent.ResetPath();
        }
    }


    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, peekRadius);

        if (mode == NavMode.SearchSpiral && spiralCache.Count > 0)
        {
            Gizmos.color = new Color(0f, 0.6f, 1f, 0.6f);
            foreach (var p in spiralCache) Gizmos.DrawSphere(p, 0.15f);
        }

        if (agent && agent.hasPath)
        {
            Gizmos.color = Color.blue;
            var path = agent.path;
            for (int i = 0; i < path.corners.Length - 1; i++)
                Gizmos.DrawLine(path.corners[i], path.corners[i + 1]);
        }
    }
}
