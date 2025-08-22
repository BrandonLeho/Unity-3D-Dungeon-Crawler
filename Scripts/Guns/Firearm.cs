using UnityEngine;
using System.Collections;
using TMPro;
using UnityEngine.InputSystem; // only used to read Mouse button for hold-to-fire

[DisallowMultipleComponent]
public class Firearm : MonoBehaviour, HandItem // HandItem is a tiny optional interface below
{
    [Header("Wiring")]
    [SerializeField] FPController controller;          // auto-filled if missing
    [SerializeField] Transform muzzle;                 // projectile spawn
    [SerializeField] Projectile projectilePrefab;      // projectile
    [SerializeField] TMP_Text ammoText;               // optional HUD slot

    [Header("Fire Mode")]
    [SerializeField] bool allowHoldToFire = true;
    [SerializeField] int bulletsPerShot = 1;          // pellets per trigger (shotgun)
    [SerializeField] float timeBetweenShots = 0.1f;   // delay between trigger pulls or auto steps
    [SerializeField] int burstCount = 1;              // shots fired within a burst
    [SerializeField] float burstWindow = 0.08f;       // time to dump the burst (perishable window)

    [Header("Shotgun / Pellets")]
    [SerializeField, Min(1)] int pelletsPerShot = 1;       // how many projectiles per trigger
    [SerializeField] float damagePerPellet = 8f;           // per-pellet damage override (optional)
    [SerializeField] bool overrideProjectileDamage = false; // if true, set Projectile.Damage
    public enum SpreadPattern { RandomCone, UniformCircleAtDistance }
    [SerializeField] SpreadPattern spreadPattern = SpreadPattern.RandomCone;

    // Optional: scale recoil by pellet count (shotguns kick harder)
    [SerializeField] bool scaleRecoilWithPellets = true;
    [SerializeField] float recoilPelletScale = 0.06f; // extra pitch per pellet (additive)


    [Header("Ballistics")]
    [SerializeField] float projectileSpeed = 40f;
    [SerializeField] float shootForce = 0f;           // optional extra impulse (added to speed)
    [SerializeField] float extraUpwardForce = 0f;

    [Header("Spread")]
    [Tooltip("Degrees cone spread (per pellet).")]
    [SerializeField] float spreadDegrees = 1.2f;
    [Tooltip("If > 0, compute offsets so the group approximates 'spread' at this world distance (useful for shotguns).")]
    [SerializeField] float spreadDistanceReference = 0f;

    [Header("Reload / Ammo")]
    [SerializeField] int magazineSize = 30;
    [SerializeField] float reloadTime = 1.6f;
    [SerializeField] bool autoReload = true;

    [Header("Recoil")]
    [SerializeField] float recoilPitchPerShot = 1.0f;     // camera pitch up
    [SerializeField] float recoilYawRandom = 0.4f;         // +/- yaw
    [SerializeField] float recoilCamReturnSpeed = 12f;     // how quickly camera recenters
    [SerializeField] Vector3 recoilKickLocal = new(0f, 0f, -0.04f); // slight gun kick
    [SerializeField] float recoilKickReturn = 8f;

    [Header("Misc")]
    [SerializeField] LayerMask aimMask = ~0;          // for spread-at-distance sampling
    [SerializeField] Transform aimReference;          // if null, uses controller.CameraTransform
    [SerializeField] bool debugGizmos = false;

    int ammo;
    bool reloading;
    bool busyBurst;
    float nextShotTime;
    Quaternion cameraRecoilOffset = Quaternion.identity;
    Vector3 gunKick = Vector3.zero;

    // -------- HandItem small interface so EquipmentController can talk to it (optional) --------
    public void OnEquip() { gameObject.SetActive(true); UpdateAmmoUI(); }
    public void OnUnequip() { gameObject.SetActive(false); }
    public void PrimaryUse() { TryFire(); }

    void OnEnable()
    {
        if (!controller) controller = GetComponentInParent<FPController>();
        if (controller) controller.TryAttack += TryFire;
    }

    void OnDisable()
    {
        if (controller) controller.TryAttack -= TryFire;
        StopAllCoroutines(); // cancel bursts/reloads mid-swap
    }


    void Update()
    {
        // Hold-to-fire support (uses Mouse directly to keep Player.cs unchanged)
        if (allowHoldToFire && Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            if (Time.time >= nextShotTime) TryFire();
        }

        // Smooth camera & gun recoil return
        if (controller)
        {
            // camera pitch/yaw: multiply a decaying offset
            cameraRecoilOffset = Quaternion.Slerp(cameraRecoilOffset, Quaternion.identity, 1f - Mathf.Exp(-recoilCamReturnSpeed * Time.deltaTime));
            controller.CameraTransform.localRotation = cameraRecoilOffset * controller.CameraTransform.localRotation;
        }

        gunKick = Vector3.Lerp(gunKick, Vector3.zero, 1f - Mathf.Exp(-recoilKickReturn * Time.deltaTime));
        transform.localPosition = gunKick;
    }

    void TryFire()
    {
        if (!isActiveAndEnabled || !gameObject.activeInHierarchy) return; // <- prevents the error
        if (reloading || busyBurst) return;
        if (ammo <= 0) { if (autoReload) StartCoroutine(ReloadCo()); return; }
        if (Time.time < nextShotTime) return;
        StartCoroutine(BurstCo());
    }


    IEnumerator BurstCo()
    {
        busyBurst = true;
        float burstEnd = Time.time + burstWindow;
        int fired = 0;

        while (fired < burstCount && Time.time <= burstEnd)
        {
            if (ammo <= 0)
            {
                if (autoReload) StartCoroutine(ReloadCo());
                break;
            }

            FireOnce();
            fired++;

            if (burstCount > 1) yield return new WaitForSeconds(timeBetweenShots * 0.5f);
            else yield return null;
        }

        nextShotTime = Time.time + timeBetweenShots;
        busyBurst = false;
    }

    void FireOnce()
    {
        ammo--;
        UpdateAmmoUI();

        // Compute a target point once (for distance-referenced patterns)
        Transform cam = (aimReference != null) ? aimReference : controller.CameraTransform;
        Vector3 origin = muzzle ? muzzle.position : cam.position;
        Vector3 dir = cam.forward;

        // Default target far away
        Vector3 targetPoint = origin + dir * 1000f;

        // If you use distance-referenced spread, lock a plane/point to pattern around
        if (spreadDistanceReference > 0f)
        {
            if (Physics.Raycast(cam.position, cam.forward, out var hit, 999f, aimMask, QueryTriggerInteraction.Ignore))
                targetPoint = hit.point;
            else
                targetPoint = cam.position + cam.forward * spreadDistanceReference;
        }

        // Generate all pellet directions first (so patterns look nice)
        var dirs = GeneratePelletDirections(cam, origin, targetPoint, pelletsPerShot);

        // Spawn pellets
        for (int i = 0; i < dirs.Count; i++)
        {
            var proj = SpawnProjectile(origin, dirs[i]);

            if (overrideProjectileDamage && proj != null)
                proj.Damage = damagePerPellet; // per-pellet damage
        }

        // Scale recoil for shotguns if desired
        if (scaleRecoilWithPellets && pelletsPerShot > 1)
        {
            float extraPitch = recoilPelletScale * (pelletsPerShot - 1);
            cameraRecoilOffset = Quaternion.Euler(-extraPitch, 0f, 0f) * cameraRecoilOffset;
        }


        ApplyRecoil();

        // If mag empty and autoReload, kick it off
        if (ammo <= 0 && autoReload) StartCoroutine(ReloadCo());
    }

    Vector3 ComputeSpreadDirection(Transform cam, Vector3 targetPoint)
    {
        if (spreadDegrees <= 0.0001f && spreadDistanceReference <= 0f)
            return cam.forward;

        if (spreadDistanceReference > 0f)
        {
            // Offset target point in camera's right/up so pattern width is consistent at distance
            Vector2 off = Random.insideUnitCircle * Mathf.Tan(spreadDegrees * Mathf.Deg2Rad) * spreadDistanceReference;
            Vector3 right = cam.right, up = cam.up;
            Vector3 p = targetPoint + right * off.x + up * off.y;
            return (p - (muzzle ? muzzle.position : cam.position)).normalized;
        }
        else
        {
            // Pure angular cone
            Quaternion q = Random.rotationUniform;
            Vector3 v = q * Vector3.forward;
            float angle = spreadDegrees * Mathf.Deg2Rad * Random.value;
            Vector3 axis = Vector3.Cross(Vector3.forward, v).normalized;
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.up;
            Quaternion cone = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, axis);
            return (cone * cam.forward).normalized;
        }
    }

    Projectile SpawnProjectile(Vector3 origin, Vector3 dir)
    {
        var p = Instantiate(projectilePrefab, origin, Quaternion.LookRotation(dir));
        float initial = projectileSpeed + Mathf.Max(0f, shootForce);
        p.Launch(dir, extraUpwardForce, initial, this);
        return p;
    }


    System.Collections.Generic.List<Vector3> GeneratePelletDirections(
        Transform cam, Vector3 origin, Vector3 targetPoint, int count)
    {
        var list = new System.Collections.Generic.List<Vector3>(count);

        if (count <= 1)
        {
            list.Add(cam.forward);
            return list;
        }

        // Angular spread (in radians)
        float spreadRad = Mathf.Max(0f, spreadDegrees) * Mathf.Deg2Rad;

        switch (spreadPattern)
        {
            case SpreadPattern.RandomCone:
                {
                    for (int i = 0; i < count; i++)
                    {
                        // Sample a small cone around camera forward
                        // Use a small-angle approximation for uniform-ish distribution
                        Vector2 r = Random.insideUnitCircle; // [-1,1] disk
                        float angle = spreadRad * r.magnitude;
                        Vector3 axis = Vector3.Cross(cam.forward, cam.right * r.x + cam.up * r.y);
                        if (axis.sqrMagnitude < 1e-6f) axis = cam.up;
                        Quaternion q = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, axis.normalized);
                        list.Add((q * cam.forward).normalized);
                    }
                    break;
                }

            case SpreadPattern.UniformCircleAtDistance:
                {
                    // Place pellets evenly on a disk at the reference distance (or hit point),
                    // then back-compute directions from the muzzle/origin to those points.
                    // Use golden-angle sampling for uniform distribution.
                    float dist = (targetPoint - origin).magnitude;
                    if (spreadDistanceReference > 0f) dist = spreadDistanceReference;

                    // Radius at that distance corresponding to spread angle
                    float radius = Mathf.Tan(spreadRad) * dist;

                    const float golden = 2.39996323f; // ~137.5° in radians
                    for (int i = 0; i < count; i++)
                    {
                        float t = (i + 0.5f) / count;
                        float r = Mathf.Sqrt(t) * radius;         // sqrt for even area distribution
                        float a = i * golden;

                        Vector3 right = cam.right;
                        Vector3 up = cam.up;
                        Vector3 center = (spreadDistanceReference > 0f) ?
                                        (cam.position + cam.forward * dist) : targetPoint;

                        Vector3 p = center + right * (r * Mathf.Cos(a)) + up * (r * Mathf.Sin(a));
                        Vector3 d = (p - origin).normalized;
                        list.Add(d);
                    }
                    break;
                }
        }

        return list;
    }


    IEnumerator ReloadCo()
    {
        if (reloading) yield break;
        reloading = true;
        yield return new WaitForSeconds(reloadTime);
        ammo = magazineSize;
        UpdateAmmoUI();
        reloading = false;
    }

    void UpdateAmmoUI()
    {
        if (ammoText) ammoText.text = $"{ammo}/{magazineSize}";
    }

    void ApplyRecoil()
    {
        // Camera: pitch up + small random yaw
        float yaw = Random.Range(-recoilYawRandom, recoilYawRandom);
        Quaternion kick = Quaternion.Euler(-recoilPitchPerShot, yaw, 0f);
        cameraRecoilOffset = kick * cameraRecoilOffset;

        // Gun kick (local)
        gunKick += recoilKickLocal;
    }

    void OnValidate()
    {
        if (magazineSize < 1) magazineSize = 1;
        if (burstCount < 1) burstCount = 1;
    }

    // Debug
    void OnDrawGizmosSelected()
    {
        if (!debugGizmos) return;
        Transform cam = (aimReference != null) ? aimReference : (controller ? controller.CameraTransform : transform);
        if (!cam) return;

        Gizmos.color = Color.yellow;
        Vector3 o = muzzle ? muzzle.position : cam.position;
        Gizmos.DrawLine(o, o + cam.forward * 2f);
    }
}

// Minimal hook so a generic equipment system can talk to this without knowing class
public interface HandItem
{
    void OnEquip();
    void OnUnequip();
    void PrimaryUse();
}
