using UnityEngine;

public class WeaponAttack : HandEquipment
{
    [Header("Attacking")]
    [SerializeField] float attackDistance = 3f;
    [SerializeField] float attackDelay = 0.4f;      // time from swing start to hit check
    [SerializeField] float attackCooldown = 0.5f;   // lockout between attacks
    [SerializeField] int baseDamage = 1;
    [SerializeField] LayerMask hitMask = ~0;

    [Header("FX")]
    [SerializeField] GameObject hitEffect;
    [SerializeField] AudioClip swingSfx;
    [SerializeField] AudioClip hitSfx;
    [SerializeField] Animator animator;
    [SerializeField] AudioSource audioSource;

    // Animator state names from your original
    static readonly int IdleHash = Animator.StringToHash("Idle");
    static readonly int WalkHash = Animator.StringToHash("Walk");
    static readonly int Attack1Hash = Animator.StringToHash("Attack 1");
    static readonly int Attack2Hash = Animator.StringToHash("Attack 2");

    FPController fp;   // grabbed from parent at runtime
    bool attacking;
    bool onCooldown;
    int comboIndex;    // alternate between Attack 1/2

    void Awake()
    {
        fp = GetComponentInParent<FPController>();
        if (!animator) animator = GetComponentInChildren<Animator>(true);
        if (!audioSource) audioSource = GetComponentInChildren<AudioSource>(true);
    }

    void Update()
    {
        // Simple locomotion -> idle/walk when not attacking
        if (!attacking && fp && animator && gameObject.activeInHierarchy)
        {
            var v = fp.CurrentVelocity;
            if (Mathf.Approximately(v.x, 0f) && Mathf.Approximately(v.z, 0f))
                CrossFade(IdleHash);
            else
                CrossFade(WalkHash);
        }
    }

    public override void PrimaryUse()
    {
        if (GameplayLock.Any(LockCategory.AnimationStart | LockCategory.AttackUse)) return;
        if (onCooldown || attacking) return;
        var anim = FindAnyObjectByType<FPArmAttackAnimator>();
        if (anim) anim.PlayActive("Attack");
        StartAttack();
    }

    void StartAttack()
    {
        attacking = true;
        onCooldown = true;

        // Play swing SFX
        if (audioSource && swingSfx)
        {
            audioSource.pitch = Random.Range(0.95f, 1.05f);
            audioSource.PlayOneShot(swingSfx);
        }

        // Alternate attacks for variety
        if (animator)
        {
            CrossFade((comboIndex++ % 2 == 0) ? Attack1Hash : Attack2Hash);
        }

        // Schedule hit + cooldown reset
        Invoke(nameof(DoHit), attackDelay);
        Invoke(nameof(ResetAttack), attackCooldown);
    }

    void ResetAttack()
    {
        attacking = false;
        onCooldown = false;
    }

    void DoHit()
    {
        // Preserve your dice-roll-to-damage flow
        DiceRollEvents.Request(result =>
        {
            if (!fp) return;

            if (Physics.Raycast(fp.CameraTransform.position,
                                fp.CameraTransform.forward,
                                out var hit, attackDistance, hitMask,
                                QueryTriggerInteraction.Ignore))
            {
                var actor = hit.collider.GetComponent<Actor>();
                if (actor) actor.TakeDamage(Mathf.Max(baseDamage, result));

                if (hitEffect)
                {
                    var go = Instantiate(hitEffect, hit.point, Quaternion.identity);
                    Destroy(go, 20f);
                }

                if (audioSource && hitSfx)
                {
                    audioSource.pitch = 1f;
                    audioSource.PlayOneShot(hitSfx);
                }
            }
        }, "Player Melee");
    }

    void CrossFade(int stateHash)
    {
        if (animator) animator.CrossFadeInFixedTime(stateHash, 0.15f);
    }

    public override void OnEquip()
    {
        // HandEquipment turns the GO on; keep any per-equip toggles here.
        base.OnEquip();
        attacking = false;
        onCooldown = false;
    }

    public override void OnUnequip()
    {
        base.OnUnequip(); // HandEquipment will disable the GO
        attacking = false;
    }
}
