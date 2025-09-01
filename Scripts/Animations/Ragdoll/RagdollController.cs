using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DefaultExecutionOrder(-50)]
public class RagdollController : MonoBehaviour
{
    [Header("Rig")]
    [SerializeField] Animator animator;
    [SerializeField] Transform ragdollRoot;  // parent of bone rigidbodies/colliders

    [Header("Recovery")]
    public bool autoRecover = false;
    public float recoverAfter = 3.0f;

    [Header("Impulse")]
    public float impulseMultiplier = 1.0f;   // global multiplier (per-attack still provided in DamageInfo)

    public bool IsRagdolled { get; private set; }

    // caches
    Rigidbody[] bodies;
    Collider[] cols;
    CharacterController charController;
    NavMeshAgent agent;
    FPController fpCtrl;
    EnemyAIController aiCtrl;

    // optional gameplay lock handle (if you’re using the earlier GameplayLock helper)
    LockHandle lockHandle;

    void Awake()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
        charController = GetComponent<CharacterController>();
        agent = GetComponent<NavMeshAgent>();
        fpCtrl = GetComponent<FPController>();
        aiCtrl = GetComponent<EnemyAIController>();

        if (!ragdollRoot && animator) ragdollRoot = animator.transform;

        bodies = ragdollRoot ? ragdollRoot.GetComponentsInChildren<Rigidbody>(true) : new Rigidbody[0];
        cols = ragdollRoot ? ragdollRoot.GetComponentsInChildren<Collider>(true) : new Collider[0];

        SetRagdollActive(false, Vector3.zero);
    }

    public void EnterRagdoll(Vector3 impulse)
    {
        if (IsRagdolled) return;
        IsRagdolled = true;

        // disable controllers/AI while ragdolled
        if (agent) agent.enabled = false;                          // AI navigation off
        if (charController) charController.enabled = false;        // player motion off

        // optional: block actions globally (attack, switch, animate, move, interact)
        lockHandle = GameplayLock.GetOrCreate()
            .Acquire(LockCategory.Movement | LockCategory.AttackUse | LockCategory.AnimationStart | LockCategory.EquipmentSwitch | LockCategory.Interact,
                     this, tag: "Ragdoll", priority: 100);

        // animator → off, physics → on
        SetRagdollActive(true, impulse * impulseMultiplier);

        if (autoRecover && recoverAfter > 0f)
            Invoke(nameof(ExitRagdoll), recoverAfter);
    }

    public void ExitRagdoll()
    {
        if (!IsRagdolled) return;
        IsRagdolled = false;

        // physics → off, animator → on
        SetRagdollActive(false, Vector3.zero);

        // re-enable controllers
        if (charController) charController.enabled = true;
        if (agent) agent.enabled = true;

        lockHandle.Release();
    }

    void SetRagdollActive(bool active, Vector3 impulse)
    {
        if (animator) animator.enabled = !active;

        foreach (var c in cols) if (c && c.gameObject != gameObject) c.enabled = true; // keep colliders on either way
        foreach (var rb in bodies)
        {
            if (!rb || rb.gameObject == gameObject) continue;
            rb.isKinematic = !active;
            rb.detectCollisions = true;
        }

        if (active && impulse.sqrMagnitude > 0.0001f)
        {
            // best-effort: apply to hips or spread across bodies
            foreach (var rb in bodies)
            {
                if (!rb || rb.isKinematic) continue;
                rb.AddForce(impulse, ForceMode.Impulse);
                break; // one is often enough (hip). Remove 'break' to scatter to all.
            }
        }
    }
}
