using UnityEngine;

public class FPAttack : FPControllerModule
{
    [Header("Attacking")]
    public float attackDistance = 3f;
    public float attackDelay = 0.4f;
    public float attackSpeed = 1f;
    public int attackDamage = 1;
    public LayerMask attackLayer;

    public GameObject hitEffect;
    public AudioClip swordSwing;
    public AudioClip hitSound;

    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private AudioSource audioSource;


    // Animation states
    public const string IDLE = "Idle";
    public const string WALK = "Walk";
    public const string ATTACK1 = "Attack 1";
    public const string ATTACK2 = "Attack 2";

    string currentAnimationState;

    bool attacking = false;
    bool readyToAttack = true;
    int attackCount = 0;

    protected override void Awake()
    {
        base.Awake();

        Controller.TryAttack += Attack;
    }

    private void Update()
    {
        SetAnimations();
    }

    public void Attack()
    {
        if (!readyToAttack || attacking) return;

        readyToAttack = false;
        attacking = true;

        Invoke(nameof(ResetAttack), attackSpeed);
        Invoke(nameof(AttackRaycast), attackDelay);

        if (swordSwing != null && audioSource != null)
        {
            audioSource.pitch = Random.Range(0.9f, 1.1f);
            audioSource.PlayOneShot(swordSwing);
        }

        if (attackCount == 0)
        {
            ChangeAnimationState(ATTACK1);
            attackCount++;
        }
        else
        {
            ChangeAnimationState(ATTACK2);
            attackCount = 0;
        }
    }

    void ResetAttack()
    {
        attacking = false;
        readyToAttack = true;
    }

    void AttackRaycast()
    {
        // When you want to roll:
        DiceRollEvents.Request(result =>
        {
            if (Physics.Raycast(Controller.CameraTransform.position, Controller.CameraTransform.forward, out RaycastHit hit, attackDistance, attackLayer))
            {
                var actor = hit.collider.GetComponent<Actor>();
                if (actor != null)
                {
                    actor.TakeDamage(result);
                }

                if (hitEffect != null)
                {
                    GameObject go = Instantiate(hitEffect, hit.point, Quaternion.identity);
                    Destroy(go, 20f);
                }

                if (hitSound != null && audioSource != null)
                {
                    audioSource.pitch = 1f;
                    audioSource.PlayOneShot(hitSound);
                }
            }
        }, "Player Melee");
    }

    void ChangeAnimationState(string newState)
    {
        if (currentAnimationState == newState) return;

        currentAnimationState = newState;
        if (animator != null)
        {
            animator.CrossFadeInFixedTime(currentAnimationState, 0.2f);
        }
    }

    void SetAnimations()
    {
        if (!attacking)
        {
            Vector3 velocity = Controller.CurrentVelocity;
            if (velocity.x == 0 && velocity.z == 0)
                ChangeAnimationState(IDLE);
            else
                ChangeAnimationState(WALK);
        }
    }
}
