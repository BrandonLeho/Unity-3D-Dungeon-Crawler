using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Physics‑rolled 3D die that:
///  • rolls on request (via DiceRollEvents),
///  • finds the face‑up result,
///  • snaps to the exact winning rotation,
///  • locks rotation while gliding to center,
///  • queues overlapping requests and invokes a completion callback with the result,
///  • and performs a smooth floating+rotating idle animation when not rolling.
/// 
/// Performance-focused refactor:
///  • Physics work moved to FixedUpdate.
///  • Rigidbody modes/solver iterations tightened per-state.
///  • Option to disable collisions while idle.
///  • Idle animation can be throttled (Hz) and anchors cleanly to last pose.
///  • Fewer GC allocations; transform cached; repeated math consolidated.
/// </summary>
[RequireComponent(typeof(DiceSides))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(AudioSource))]
public class DiceRoller : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Roll Behaviour
    // ─────────────────────────────────────────────────────────────────────────────
    [Header("Roll Behaviour")]
    [SerializeField] bool playOnStart = false;
    [SerializeField] float rollForce = 5f;
    [SerializeField] float torqueAmount = 5f;
    [SerializeField] float maxRollTime = 3f;
    [SerializeField] float settleAngularSpeed = 0.15f; // below this we consider it "settled" for a bit
    [SerializeField] float settleTime = 0.35f;         // sustain low spin for this long

    // ─────────────────────────────────────────────────────────────────────────────
    // Return To Center
    // ─────────────────────────────────────────────────────────────────────────────
    [Header("Return To Center")]
    [SerializeField] Transform center;                 // optional; if null, uses start position
    [SerializeField] float moveSpeed = 3.0f;           // units/sec while returning
    [SerializeField] float rotateSnapSpeed = 8f;       // deg/sec * 180 (see usage below)

    // ─────────────────────────────────────────────────────────────────────────────
    // Layers
    // ─────────────────────────────────────────────────────────────────────────────
    [Header("Layers")]
    [SerializeField] LayerMask worldMask = ~0;         // what the die can collide with during roll

    // ─────────────────────────────────────────────────────────────────────────────
    // Result Text
    // ─────────────────────────────────────────────────────────────────────────────
    [Header("Result Text")]
    [SerializeField] TMPro.TMP_Text resultText;

    // ─────────────────────────────────────────────────────────────────────────────
    // Idle Motion
    // ─────────────────────────────────────────────────────────────────────────────
    [Header("Idle Motion")]
    [Tooltip("Enable or disable idle floating/rotating when not rolling.")]
    [SerializeField] bool enableIdleMotion = true;

    [Tooltip("Horizontal hover radius (meters).")]
    [SerializeField, Min(0f)] float idleHoverRadius = 0.06f;

    [Tooltip("Vertical hover amplitude (meters).")]
    [SerializeField, Min(0f)] float idleHoverHeight = 0.08f;

    [Tooltip("Horizontal pan speeds in cycles/sec (x = around XZ path, y = phase offset).")]
    [SerializeField] Vector2 idlePanSpeeds = new Vector2(0.18f, 0.27f);

    [Tooltip("Vertical bob speed in cycles/sec.")]
    [SerializeField, Min(0f)] float idleBobSpeed = 0.35f;

    [Tooltip("Degrees/second spin around XYZ while idle.")]
    [SerializeField] Vector3 idleSpinDegPerSec = new Vector3(8f, 22f, 12f);

    [Tooltip("Optional extra jitter (0..1) mixed via Perlin; set to 0 for perfectly smooth paths.")]
    [SerializeField, Range(0f, 1f)] float idleNoise = 0.15f;

    [Tooltip("How quickly the die eases toward its animated idle target (bigger = snappier).")]
    [SerializeField, Min(0.01f)] float idleEase = 6f;

    [Header("Idle Grace Period")]
    [SerializeField, Min(0f)] float idleGraceTime = 1.0f;

    [Header("Idle Start Options")]
    [SerializeField] bool idleAnchorToCenter = true; // true = hover around center; false = hover where the die landed

    // ─────────────────────────────────────────────────────────────────────────────
    // Player speed coupling
    // ─────────────────────────────────────────────────────────────────────────────
    [Header("Idle Speed Coupling")]
    [SerializeField] bool scaleIdleByPlayerSpeed = true;
    [Tooltip("Player controller to read CurrentSpeed from. If left null, will auto-find.")]
    [SerializeField] FPController playerController;
    [Tooltip("Remaps speed ratio (0..1) to an interpolation factor (0..1).")]
    [SerializeField] AnimationCurve idleSpeedCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Tooltip("Minimum multiplier when player is stationary.")]
    [SerializeField, Min(0f)] float idleSpeedMin = 0.85f;
    [Tooltip("Maximum multiplier when player is at sprint speed.")]
    [SerializeField, Min(0f)] float idleSpeedMax = 2.0f;
    [Tooltip("How quickly the die responds to player speed changes (bigger = snappier).")]
    [SerializeField, Min(0.01f)] float idleSpeedResponse = 6f;

    // ─────────────────────────────────────────────────────────────────────────────
    // Physics/Performance tuning
    // ─────────────────────────────────────────────────────────────────────────────
    [Header("Performance (Physics Modes)")]
    [SerializeField] CollisionDetectionMode rollCollisionMode = CollisionDetectionMode.ContinuousDynamic;
    [SerializeField] CollisionDetectionMode idleCollisionMode = CollisionDetectionMode.Discrete;
    [SerializeField] RigidbodyInterpolation rollInterpolation = RigidbodyInterpolation.Interpolate;
    [SerializeField] RigidbodyInterpolation idleInterpolation = RigidbodyInterpolation.None;
    [Tooltip("Lower solver iterations = cheaper physics; raise if jittery.")]
    [SerializeField, Range(1, 20)] int rollSolverIterations = 6, rollSolverVelIterations = 1;
    [SerializeField, Range(1, 20)] int idleSolverIterations = 1, idleSolverVelIterations = 1;
    [Tooltip("Disable collider/contacts while idle (re-enabled on roll).")]
    [SerializeField] bool disableCollisionsWhileIdle = true;
    [Tooltip("Limit how often the idle pose is updated (Hz). 60 = every frame @60 FPS.")]
    [SerializeField, Min(5f)] float idleUpdateHz = 60f;

    // runtime
    float idleSpeedMul = 1f;

    // Internal phases so idle starts from current pose without snapping
    float idlePhaseX, idlePhaseY, idlePhaseZ;
    bool idlePhaseInitialized;

    // Components & cached refs
    Rigidbody rb;
    DiceSides diceSides;
    AudioSource audioSource;
    Transform tr;

    // Start pose
    Vector3 startPos;
    Quaternion startRot;

    // State machine
    enum State { Idle, Rolling, Finalizing, Returning, Grace }
    State state = State.Idle;

    // Timers
    float rollElapsed;
    float settleTimer;

    // Finalization
    int finalResult = -1;
    Quaternion targetRotation;
    bool rotationLocked;

    // Queue / requests
    readonly Queue<DiceRollRequest> queue = new();
    DiceRollRequest currentReq;
    bool busy;

    // Idle internals
    float idleTime;
    float idleUpdateAccum; // throttles idle updates
    Vector3 idleAngles;           // accumulated spin angles
    Quaternion idleBaseRot;       // idle orientation baseline (e.g., last snapped face-up)
    Vector3 idleBasePos;          // center position baseline

    float graceTimer;

    void Awake()
    {
        tr = transform;
        rb = GetComponent<Rigidbody>();
        diceSides = GetComponent<DiceSides>();
        audioSource = GetComponent<AudioSource>();

        startPos = tr.position;
        startRot = tr.rotation;

        if (center == null)
        {
            var go = new GameObject($"{name}_Center");
            go.transform.SetPositionAndRotation(startPos, Quaternion.identity);
            center = go.transform;
        }

        if (playerController == null)
            playerController = FindFirstObjectByType<FPController>();

        // Prepare idle baselines
        idleBasePos = center.position;
        idleBaseRot = startRot;

        // Apply cheap physics defaults for idle at startup
        ApplyIdlePhysicsSettings();
    }

    void OnEnable() => DiceRollEvents.RollRequested += Enqueue;
    void OnDisable() => DiceRollEvents.RollRequested -= Enqueue;

    void Start()
    {
        if (playOnStart) BeginRoll();
        else EnterIdle();
    }

    // Split work: physics in FixedUpdate, visuals/idle in Update
    void Update()
    {
        switch (state)
        {
            case State.Idle: UpdateIdle(Time.deltaTime); break;
            case State.Finalizing: UpdateFinalizing(Time.deltaTime); break;
            case State.Returning: UpdateReturning(); break;
            case State.Grace: UpdateGrace(Time.deltaTime); break;
        }
    }

    void FixedUpdate()
    {
        if (state == State.Rolling)
            UpdateRollingFixed(Time.fixedDeltaTime);
    }

    // -----------------------------
    // Queue / external API wiring
    // -----------------------------
    void Enqueue(DiceRollRequest req)
    {
        // Debounce spam: ignore any roll requests unless fully idle.
        if (busy || state != State.Idle)
            return;


        busy = true;
        currentReq = req;
        BeginRoll();
    }


    // -----------------------------
    // Rolling lifecycle
    // -----------------------------
    void BeginRoll()
    {
        if (state == State.Rolling || state == State.Finalizing || state == State.Grace)
            return; // safeguard: never begin a roll unless idle/returning

        // physics on
        rb.isKinematic = false;
        rb.constraints = RigidbodyConstraints.None;
        ApplyRollPhysicsSettings();

        // reset pose at center
        tr.SetPositionAndRotation(center.position, startRot);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        if (resultText) resultText.text = string.Empty;

        // kick with random up+flat impulse
        Vector3 randomFlat = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;
        rb.AddForce((randomFlat + Vector3.up * 0.35f).normalized * rollForce, ForceMode.Impulse);
        rb.AddTorque(Random.onUnitSphere * torqueAmount, ForceMode.Impulse);

        state = State.Rolling;
        rollElapsed = 0f;
        settleTimer = 0f;
        finalResult = -1;
    }

    void UpdateRollingFixed(float dt)
    {
        rollElapsed += dt;

        // Detect a "settled" window: low angular velocity sustained for settleTime
        float ang = rb.angularVelocity.magnitude;
        settleTimer = (ang < settleAngularSpeed) ? (settleTimer + dt) : 0f;

        if (rollElapsed >= maxRollTime || settleTimer >= settleTime)
        {
            PrepareFinalize();
        }
    }

    // Compute best face, lock rotation against physics, start aligning
    void PrepareFinalize()
    {
        // Find the side whose world normal is most aligned with +Y
        int bestIndex = -1; float bestDot = -1f;
        var sides = diceSides.Sides;
        for (int i = 0; i < sides.Length; i++)
        {
            Vector3 worldNormal = tr.TransformDirection(sides[i].Normal);
            float dot = Vector3.Dot(worldNormal, Vector3.up);
            if (dot > bestDot) { bestDot = dot; bestIndex = i; }
        }
        finalResult = (bestIndex >= 0) ? sides[bestIndex].Value : -1;

        // Build a rotation that rotates current "winning" normal up to +Y
        if (bestIndex >= 0)
        {
            Vector3 currentUp = tr.TransformDirection(sides[bestIndex].Normal);
            Quaternion align = Quaternion.FromToRotation(currentUp, Vector3.up);
            targetRotation = align * tr.rotation;
        }
        else targetRotation = tr.rotation;

        // stop physics from changing rotation while we align/move
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rotationLocked = true;

        // cheaper modes for kinematic stage
        ApplyIdlePhysicsSettings();

        state = State.Finalizing;
    }

    void UpdateFinalizing(float dt)
    {
        // Smoothly snap to the exact face-up orientation
        if (rotationLocked)
        {
            tr.rotation = Quaternion.RotateTowards(
                tr.rotation,
                targetRotation,
                rotateSnapSpeed * 180f * dt);
        }

        // Start returning to center as we align
        Vector3 toCenter = center.position - tr.position;
        float step = moveSpeed * dt;
        tr.position = (toCenter.magnitude <= step)
            ? center.position
            : tr.position + toCenter.normalized * step;

        // Done when we're at center and aligned
        if (Vector3.Distance(tr.position, center.position) < 0.01f &&
            Quaternion.Angle(tr.rotation, targetRotation) < 0.5f)
        {
            FinalizeRoll();
        }
    }

    void FinalizeRoll()
    {
        if (resultText) resultText.text = finalResult.ToString();

        // Notify requester
        currentReq.onComplete?.Invoke(finalResult);

        // Enter a short grace period
        state = State.Grace;
        graceTimer = 0f;
    }

    void UpdateReturning()
    {
        // Transition into idle, release the queue
        EnterIdle();
        busy = false;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Idle animation
    // ─────────────────────────────────────────────────────────────────────────────
    void EnterIdle()
    {
        state = State.Idle;

        rb.isKinematic = true;
        rb.constraints = RigidbodyConstraints.None;
        ApplyIdlePhysicsSettings();

        // Choose anchor for the idle envelope
        idleBasePos = idleAnchorToCenter ? center.position : tr.position;

        // Use the actual landed orientation as the baseline for rotation
        idleBaseRot = tr.rotation;

        // Compute per-axis phases so the first idle sample equals current position
        ComputeIdlePhasesFromCurrent();

        // Seed rotation so Euler idle spin starts exactly at the current rotation
        Quaternion delta = Quaternion.Inverse(idleBaseRot) * tr.rotation;
        idleAngles = delta.eulerAngles;

        idleTime = 0f;
        idleUpdateAccum = 0f;
        idlePhaseInitialized = true;
    }

    void UpdateIdle(float dt)
    {
        if (!enableIdleMotion) return;

        // throttle idle math to save CPU
        idleUpdateAccum += dt;
        float idleStep = 1f / Mathf.Max(5f, idleUpdateHz);
        if (idleUpdateAccum < idleStep) return;
        dt = idleUpdateAccum;           // use accumulated dt so motion stays consistent
        idleUpdateAccum = 0f;

        idleTime += dt;

        // dynamic speed multiplier from player motion
        float mul = ComputeIdleSpeedMultiplier(dt);

        // Angular speeds (radians/sec) for each axis — scaled by player speed
        float wx = Mathf.PI * 2f * idlePanSpeeds.x * mul;
        float wz = Mathf.PI * 2f * idlePanSpeeds.y * mul;
        float wy = Mathf.PI * 2f * idleBobSpeed * mul;

        // Phase-matched offsets (prevents snapping)
        float x = Mathf.Sin(wx * idleTime + (idlePhaseInitialized ? idlePhaseX : 0f)) * idleHoverRadius;
        float z = Mathf.Cos(wz * idleTime + (idlePhaseInitialized ? idlePhaseZ : 0f)) * idleHoverRadius;
        float y = Mathf.Sin(wy * idleTime + (idlePhaseInitialized ? idlePhaseY : 0f)) * idleHoverHeight;

        // Optional noise (evaluated at throttled rate)
        if (idleNoise > 0f)
        {
            float nX = (Mathf.PerlinNoise(3.7f, idleTime * 0.7f) - 0.5f) * 2f;
            float nZ = (Mathf.PerlinNoise(11.1f, idleTime * 0.6f) - 0.5f) * 2f;
            float nY = (Mathf.PerlinNoise(6.2f, idleTime * 0.8f) - 0.5f) * 2f;
            x += nX * idleHoverRadius * idleNoise * 0.5f;
            z += nZ * idleHoverRadius * idleNoise * 0.5f;
            y += nY * idleHoverHeight * idleNoise * 0.5f;
        }

        Vector3 desiredPos = idleBasePos + new Vector3(x, y, z);

        // Spin rate scales with speed as well
        idleAngles += (idleSpinDegPerSec * mul) * dt;
        Quaternion desiredRot = idleBaseRot * Quaternion.Euler(idleAngles);

        float t = 1f - Mathf.Exp(-idleEase * dt);
        tr.position = Vector3.Lerp(tr.position, desiredPos, t);
        tr.rotation = Quaternion.Slerp(tr.rotation, desiredRot, t);
    }

    void UpdateGrace(float dt)
    {
        graceTimer += dt;
        if (graceTimer >= idleGraceTime)
            state = State.Returning;
    }

    void ComputeIdlePhasesFromCurrent()
    {
        // Guard against zero radii
        float rx = Mathf.Max(1e-4f, idleHoverRadius);
        float ry = Mathf.Max(1e-4f, idleHoverHeight);
        float rz = rx; // same horizontal radius used for Z path

        Vector3 cur = tr.position;
        Vector3 baseP = idleBasePos;

        // Clamp ratio to [-1,1] to satisfy asin/acos domain even with tiny round-off.
        float nx = Mathf.Clamp((cur.x - baseP.x) / rx, -1f, 1f);
        float ny = Mathf.Clamp((cur.y - baseP.y) / ry, -1f, 1f);
        float nz = Mathf.Clamp((cur.z - baseP.z) / rz, -1f, 1f);

        // For X/Y we’ll use asin; for Z we’ll use acos to keep a nice lissajous pairing
        idlePhaseX = Mathf.Asin(nx);               // solves sin(φx) = nx
        idlePhaseY = Mathf.Asin(ny);               // solves sin(φy) = ny
        idlePhaseZ = Mathf.Acos(Mathf.Clamp(nz, -1f, 1f)); // solves cos(φz) = nz
    }

    float ComputeIdleSpeedMultiplier(float dt)
    {
        if (!scaleIdleByPlayerSpeed || playerController == null || playerController.Preset == null)
            return Mathf.Lerp(idleSpeedMin, idleSpeedMax, 0f); // default to min when unknown

        float maxRef = Mathf.Max(0.0001f, playerController.Preset.SprintSpeed);
        float ratio = Mathf.Clamp01(playerController.CurrentSpeed / maxRef); // 0..1
        float shaped = Mathf.Clamp01(idleSpeedCurve.Evaluate(ratio));          // curve map
        float target = Mathf.Lerp(idleSpeedMin, idleSpeedMax, shaped);

        // critically-damped style smoothing so it feels responsive but not jittery
        float a = 1f - Mathf.Exp(-idleSpeedResponse * dt);
        idleSpeedMul = Mathf.Lerp(idleSpeedMul, target, a);

        return idleSpeedMul;
    }

    // -----------------------------
    // Optional: collision assist
    // -----------------------------
    void OnCollisionEnter(Collision col)
    {
        // If you want to cancel "early settle" because we hit something:
        // if (state == State.Rolling) settleTimer = 0f;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (center == null) return;

        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
        // Visualize idle hover envelope
        Gizmos.DrawWireSphere(center.position + Vector3.up * idleHoverHeight, idleHoverRadius);
        Gizmos.DrawWireSphere(center.position - Vector3.up * idleHoverHeight, idleHoverRadius);
    }
#endif

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers: switch physics presets per state
    // ─────────────────────────────────────────────────────────────────────────────
    void ApplyRollPhysicsSettings()
    {
        rb.collisionDetectionMode = rollCollisionMode;
        rb.interpolation = rollInterpolation;
        rb.solverIterations = rollSolverIterations;
        rb.solverVelocityIterations = rollSolverVelIterations;
        rb.detectCollisions = true; // ensure collisions while rolling
        var col = GetComponent<Collider>();
        if (col) col.enabled = true;
    }

    void ApplyIdlePhysicsSettings()
    {
        rb.collisionDetectionMode = idleCollisionMode;
        rb.interpolation = idleInterpolation;
        rb.solverIterations = idleSolverIterations;
        rb.solverVelocityIterations = idleSolverVelIterations;
        rb.detectCollisions = !disableCollisionsWhileIdle;
        var col = GetComponent<Collider>();
        if (col) col.enabled = !disableCollisionsWhileIdle;
    }
}
