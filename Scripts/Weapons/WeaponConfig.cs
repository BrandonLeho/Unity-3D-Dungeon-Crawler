using UnityEngine;

[CreateAssetMenu(fileName = "WeaponConfig", menuName = "Game/Weapons/Weapon Config")]
public class WeaponConfig : ScriptableObject
{
    public enum WeaponKind { Melee, Hitscan /* Projectile later */ }

    [Header("Identity")]
    public string displayName = "Weapon";
    public Sprite icon;

    [Header("Classification")]
    public WeaponKind kind = WeaponKind.Hitscan;

    [Header("Core Stats")]
    [Min(0f)] public float damage = 10f;
    [Min(0f)] public float range = 3f;            // melee reach or hitscan distance
    [Min(0f)] public float fireRate = 2f;         // attacks per second (cooldown = 1/fireRate)
    [Min(0f)] public float cooldownOnMiss = 0f;   // optional extra penalty on whiff

    [Header("Accuracy/Recoil (for hitscan)")]
    [Range(0f, 10f)] public float spreadDegrees = 0f; // simple cone spread

    [Header("Physics/Effects")]
    public LayerMask hitMask = ~0;                // what this weapon can hit
    public GameObject hitVFX;                     // optional impact fx
    public GameObject muzzleVFX;                  // optional muzzle fx (for ranged)

    [Header("Ammo (optional)")]
    public bool usesAmmo = false;
    [Min(0)] public int magazineSize = 0;
    [Min(0)] public int reserveAmmo = 0;
    [Min(0f)] public float reloadTime = 1.5f;

    [Header("Animation Hooks (reserved)")]
    [Tooltip("Names, IDs, or anything you’ll wire later. Intentionally left generic.")]
    public string startAnimKey = "Attack_Start";
    public string impactAnimKey = "Attack_Impact";
    public string endAnimKey = "Attack_End";
}
