// Pickup.cs
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Pickup : MonoBehaviour
{
    [Header("Follow (PD)")]
    [Tooltip("Linear spring (N per meter) — scales with mass automatically below")]
    [SerializeField] float baseKp = 200f;
    [Tooltip("Linear damper (N per (m/s))")]
    [SerializeField] float baseKd = 40f;

    [Tooltip("Angular spring (N·m per rad)")]
    [SerializeField] float baseKr = 20f;
    [Tooltip("Angular damper (N·m per (rad/s))")]
    [SerializeField] float baseKo = 8f;

    [Header("Strength Scaling & Limits")]
    [Tooltip("Max mass (kg) the player can influence at all: base + strength * perPoint")]
    [SerializeField] float baseCarryMass = 5f;
    [SerializeField] float carryMassPerStrength = 1.5f;

    [Tooltip("Max linear force available from strength (Newtons): base + strength * perPoint")]
    [SerializeField] float baseMaxForce = 120f;
    [SerializeField] float maxForcePerStrength = 40f;

    [Tooltip("Max torque from strength (N·m): base + strength * perPoint")]
    [SerializeField] float baseMaxTorque = 10f;
    [SerializeField] float maxTorquePerStrength = 3.5f;

    [Header("General")]
    [SerializeField] float leashDistance = 3.5f;       // auto-drop if we get this far from the target
    [SerializeField] float allowedStartDistance = 2.0f;// must be this close to begin hold (ray checks too)

    [Header("Stability")]
    [SerializeField] float targetHalfLife = 0.08f;  // 80ms smoothing
    [SerializeField] float softZoneMeters = 0.12f;  // fade gains near target
    [SerializeField] float deadZoneMeters = 0.008f; // ignore micro error
    [SerializeField] float softZoneRadians = 4f * Mathf.Deg2Rad;
    [SerializeField] float deadZoneRadians = 0.5f * Mathf.Deg2Rad;

    [SerializeField] float natFreqMin = 5f;   // rad/s for tiny masses
    [SerializeField] float natFreqMax = 12f;  // rad/s for bigger masses
    [SerializeField] float strengthForMaxFreq = 12f; // strength that unlocks natFreqMax

    [SerializeField] float lightMass = 0.5f;      // below this, treat as "light" for linearDamping boost
    [SerializeField] float heldDragBoost = 1.0f;  // extra linearDamping while held (light objects)
    [SerializeField] float heldAngDragBoost = 0.2f;

    // internal smoothing
    Vector3 sTargetPos, prevSTargetPos;
    Quaternion sTargetRot;

    float baseDrag, baseAngDrag;

    Rigidbody rb;

    // follow target & attributes
    Transform target;
    IAttributeProvider attr;

    // internal
    bool holding;
    Vector3 lastTargetPos;
    Quaternion lastTargetRot;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

        baseDrag = rb.linearDamping;
        baseAngDrag = rb.angularDamping;

        // nicer joint/PD convergence
        rb.solverIterations = Mathf.Max(rb.solverIterations, 12);
        rb.solverVelocityIterations = Mathf.Max(rb.solverVelocityIterations, 12);
    }

    void FixedUpdate()
    {
        if (!holding || !target) return;

        float dt = Time.fixedDeltaTime;
        // --- Smooth target pose (exponential) ---
        float a = 1f - Mathf.Exp(-Mathf.Log(2f) * dt / Mathf.Max(0.0001f, targetHalfLife));
        sTargetPos = Vector3.Lerp(sTargetPos, target.position, a);
        sTargetRot = Quaternion.Slerp(sTargetRot, target.rotation, a);

        // target linear & angular velocities from smoothed pose
        Vector3 tVel = (sTargetPos - prevSTargetPos) / Mathf.Max(dt, 1e-5f);
        Vector3 tAngVel = GetAngularVelocity(prevSTargetPos, sTargetPos, sTargetRot, dt); // helper below

        prevSTargetPos = sTargetPos;

        float strength = attr != null ? attr.Get(AttributeType.Strength) : 10f;
        float maxInfluenceMass = baseCarryMass + strength * carryMassPerStrength;
        if (rb.mass > maxInfluenceMass) { ForceDrop(); return; }

        float maxF = baseMaxForce + strength * maxForcePerStrength;
        float maxT = baseMaxTorque + strength * maxTorquePerStrength;

        // choose natural frequency from mass & strength (heavier/weak -> slower, strong -> snappier)
        float strength01 = Mathf.Clamp01(strength / Mathf.Max(0.001f, strengthForMaxFreq));
        float wn = Mathf.Lerp(natFreqMin, natFreqMax, strength01); // rad/s
        float m = rb.mass;

        // Critically damped gains
        float Kp = m * wn * wn;
        float Kd = 2f * m * wn;

        // --- Linear PD with dead/soft zones ---
        Vector3 com = rb.worldCenterOfMass;
        Vector3 posError = sTargetPos - com;
        float posErrMag = posError.magnitude;

        // deadzone
        if (posErrMag < deadZoneMeters) posError = Vector3.zero;

        // soft zone scale near target
        float softScale = 1f;
        if (posErrMag < softZoneMeters)
            softScale = Mathf.Clamp01((posErrMag - deadZoneMeters) / Mathf.Max(1e-4f, softZoneMeters - deadZoneMeters));

        Vector3 velError = tVel - rb.GetPointVelocity(com);
        Vector3 desiredForce = softScale * (Kp * posError + Kd * velError);
        // clamp by strength
        if (desiredForce.sqrMagnitude > maxF * maxF) desiredForce = desiredForce.normalized * maxF;
        rb.AddForce(desiredForce, ForceMode.Force);

        // --- Angular PD with dead/soft zones ---
        Quaternion delta = sTargetRot * Quaternion.Inverse(rb.rotation);
        delta.ToAngleAxis(out float deg, out Vector3 axis);
        if (float.IsNaN(axis.x)) axis = Vector3.zero;
        float rad = Mathf.Deg2Rad * Mathf.DeltaAngle(0f, deg);
        float angMag = Mathf.Abs(rad);

        if (angMag < deadZoneRadians) { axis = Vector3.zero; rad = 0f; }

        float aSoft = 1f;
        if (angMag < softZoneRadians)
            aSoft = Mathf.Clamp01((angMag - deadZoneRadians) / Mathf.Max(1e-4f, softZoneRadians - deadZoneRadians));

        // estimate scalar inertia around current axis (very rough) using bounds radius
        float r = ApproxRadius();
        float I = Mathf.Max(0.01f, 0.4f * m * r * r); // ~solid sphere 2/5 m r^2

        float Kr = I * wn * wn;
        float Ko = 2f * I * wn;

        Vector3 desiredTorque = aSoft * (Kr * rad * axis + Ko * (tAngVel - rb.angularVelocity));
        if (desiredTorque.sqrMagnitude > maxT * maxT) desiredTorque = desiredTorque.normalized * maxT;
        rb.AddTorque(desiredTorque, ForceMode.Force);

        // leash safeguard
        if (Vector3.Distance(com, sTargetPos) > leashDistance) ForceDrop();
    }

    public bool BeginForceHold(Transform followTarget, IAttributeProvider provider, float maxDistAllowed)
    {
        if (holding) return true;
        if (!rb || !followTarget) return false;

        float startDist = Vector3.Distance(transform.position, followTarget.position);
        if (startDist > Mathf.Min(maxDistAllowed, allowedStartDistance)) return false;

        target = followTarget;
        attr = provider;
        holding = true;

        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.isKinematic = false;
        rb.useGravity = true;

        // init smoothing state
        sTargetPos = prevSTargetPos = target.position;
        sTargetRot = target.rotation;

        // add damping for feather-weight items
        if (rb.mass <= lightMass)
        {
            rb.linearDamping = baseDrag + heldDragBoost;
            rb.angularDamping = baseAngDrag + heldAngDragBoost;
        }

        return true;
    }


    public void ForceDrop()
    {
        if (!holding) return;
        holding = false;
        target = null;

        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        rb.isKinematic = false;
        rb.useGravity = true;

        // restore drags
        rb.linearDamping = baseDrag;
        rb.angularDamping = baseAngDrag;
    }

    public void ForceThrow(Vector3 dir, float impulse)
    {
        // stop following before impulse
        if (holding) ForceDrop();

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Impulse is naturally “slower” for heavy masses because impulse = Δp = m*Δv.
        // You can still clamp here if desired.
        rb.AddForce(dir.normalized * Mathf.Max(0f, impulse), ForceMode.Impulse);
    }

    // --- helpers ---
    static Vector3 GetAngularVelocity(Vector3 prevPos, Vector3 curPos, Quaternion curRot, float dt)
    {
        // This returns 0 for now; angular target velocity already smoothed via slerp—good enough.
        return Vector3.zero;
    }

    float ApproxRadius()
    {
        var col = GetComponent<Collider>();
        if (!col) return 0.25f;
        var ext = col.bounds.extents;
        return Mathf.Max(0.05f, Mathf.Max(ext.x, Mathf.Max(ext.y, ext.z)));
    }
}
