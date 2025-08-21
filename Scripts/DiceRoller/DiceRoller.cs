using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Physics-rolled 3D die that:
///  • rolls on request (via DiceRollEvents),
///  • finds the face-up result,
///  • snaps to the exact winning rotation,
///  • locks rotation while gliding to center,
///  • queues overlapping requests and invokes a completion callback with the result.
/// 
/// Requires a DiceSides component with local-space Normals/Values for each face.
/// </summary>
[RequireComponent(typeof(DiceSides))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(AudioSource))]
public class DiceRoller : MonoBehaviour
{
    [Header("Roll Behaviour")]
    [SerializeField] bool playOnStart = false;
    [SerializeField] float rollForce = 5f;
    [SerializeField] float torqueAmount = 5f;
    [SerializeField] float maxRollTime = 3f;
    [SerializeField] float settleAngularSpeed = 0.15f; // below this we consider it "settled" for a bit
    [SerializeField] float settleTime = 0.35f;         // sustain low spin for this long

    [Header("Return To Center")]
    [SerializeField] Transform center;                 // optional; if null, uses start position
    [SerializeField] float moveSpeed = 3.0f;           // units/sec while returning
    [SerializeField] float rotateSnapSpeed = 8f;       // deg/sec * 180 (see usage below)

    [Header("Layers")]
    [SerializeField] LayerMask worldMask = ~0;         // what the die can collide with during roll

    [Header("Result Text")]
    [SerializeField] TMPro.TMP_Text resultText;
    // Components
    Rigidbody rb;
    DiceSides diceSides;
    AudioSource audioSource;

    // Start pose
    Vector3 startPos;
    Quaternion startRot;

    // State machine
    enum State { Idle, Rolling, Finalizing, Returning }
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

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        diceSides = GetComponent<DiceSides>();
        audioSource = GetComponent<AudioSource>();

        startPos = transform.position;
        startRot = transform.rotation;

        if (center == null) // default to start position if no explicit center
        {
            var go = new GameObject($"{name}_Center");
            go.transform.SetPositionAndRotation(startPos, Quaternion.identity);
            center = go.transform;
        }
    }

    void OnEnable() => DiceRollEvents.RollRequested += Enqueue;
    void OnDisable() => DiceRollEvents.RollRequested -= Enqueue;

    void Start()
    {
        if (playOnStart) BeginRoll();
    }

    void Update()
    {
        switch (state)
        {
            case State.Rolling: UpdateRolling(); break;
            case State.Finalizing: UpdateFinalizing(); break;
            case State.Returning: UpdateReturning(); break;
        }
    }

    // -----------------------------
    // Queue / external API wiring
    // -----------------------------
    void Enqueue(DiceRollRequest req)
    {
        queue.Enqueue(req);
        TryDequeue();
    }

    void TryDequeue()
    {
        if (busy || queue.Count == 0) return;
        busy = true;
        currentReq = queue.Dequeue();
        BeginRoll();
    }

    // -----------------------------
    // Rolling lifecycle
    // -----------------------------
    void BeginRoll()
    {
        // reset physical state
        rb.isKinematic = false;
        rb.constraints = RigidbodyConstraints.None;
        rotationLocked = false;

        transform.SetPositionAndRotation(startPos, startRot);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        if (resultText) resultText.text = "";

        // kick
        Vector3 randomFlat = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;
        rb.AddForce((randomFlat + Vector3.up * 0.35f).normalized * rollForce, ForceMode.Impulse);
        rb.AddTorque(Random.onUnitSphere * torqueAmount, ForceMode.Impulse);

        // audio (optional): loop shaking/rolling clip if you use one
        // audioSource.clip = rollClip; audioSource.loop = true; audioSource.Play();

        state = State.Rolling;
        rollElapsed = 0f;
        settleTimer = 0f;
        finalResult = -1;
    }

    void UpdateRolling()
    {
        rollElapsed += Time.deltaTime;

        // Detect a "settled" window: low angular velocity sustained for settleTime
        float ang = rb.angularVelocity.magnitude;
        if (ang < settleAngularSpeed)
            settleTimer += Time.deltaTime;
        else
            settleTimer = 0f;

        if (rollElapsed >= maxRollTime || settleTimer >= settleTime)
        {
            // Lock-in the winning face and start aligning/returning
            PrepareFinalize();
        }
    }

    // Compute best face, lock rotation against physics, start aligning
    void PrepareFinalize()
    {
        // Find the side whose *world* normal is most aligned with +Y
        int bestIndex = -1;
        float bestDot = -1f;

        for (int i = 0; i < diceSides.Sides.Length; i++)
        {
            var side = diceSides.Sides[i];
            Vector3 worldNormal = transform.TransformDirection(side.Normal);
            float dot = Vector3.Dot(worldNormal, Vector3.up);
            if (dot > bestDot) { bestDot = dot; bestIndex = i; }
        }

        finalResult = (bestIndex >= 0) ? diceSides.Sides[bestIndex].Value : -1;

        // Build a rotation that rotates current "winning" normal up to +Y
        if (bestIndex >= 0)
        {
            Vector3 currentUp = transform.TransformDirection(diceSides.Sides[bestIndex].Normal);
            Quaternion align = Quaternion.FromToRotation(currentUp, Vector3.up);
            targetRotation = align * transform.rotation;
        }
        else
        {
            targetRotation = transform.rotation;
        }

        // Stop physics from changing rotation while we align/move
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rotationLocked = true;

        // audioSource.loop = false; // if you had a loop clip

        state = State.Finalizing;
    }

    void UpdateFinalizing()
    {
        // Smoothly snap to the exact face-up orientation
        if (rotationLocked)
        {
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotateSnapSpeed * 180f * Time.deltaTime);
        }

        // Start returning to center as we align
        Vector3 toCenter = center.position - transform.position;
        float step = moveSpeed * Time.deltaTime;
        transform.position = (toCenter.magnitude <= step)
            ? center.position
            : transform.position + toCenter.normalized * step;

        // Done when we're at center and aligned
        if (Vector3.Distance(transform.position, center.position) < 0.01f &&
            Quaternion.Angle(transform.rotation, targetRotation) < 0.5f)
        {
            FinalizeRoll();
        }
    }

    void FinalizeRoll()
    {
        // Show result here if you have UI, VFX, SFX, etc.
        // resultText.text = finalResult.ToString();
        // audioSource.PlayOneShot(doneClip);

        if (resultText) resultText.text = finalResult.ToString();

        // Notify requester
        currentReq.onComplete?.Invoke(finalResult);

        // Stay at center, keep pose until the next roll
        state = State.Returning;
    }

    void UpdateReturning()
    {
        // Idle at center/locked orientation until next request
        // (You could add a small delay here if you want before idling.)
        state = State.Idle;
        rb.isKinematic = true;
        //rb.constraints = RigidbodyConstraints.FreezeAll;

        // Release the queue
        busy = false;
        TryDequeue();
    }

    // -----------------------------
    // Optional: collision assist (tiny bounce polish if needed)
    // -----------------------------
    void OnCollisionEnter(Collision col)
    {
        // If you want to cancel "early settle" because we hit something, you can do:
        // if (state == State.Rolling) settleTimer = 0f;
    }
}
