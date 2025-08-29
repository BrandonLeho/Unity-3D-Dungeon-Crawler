using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(+45)]
public class ProceduralFingerAnimator : MonoBehaviour
{
    public enum Axis { X, Y, Z }
    public enum DistalFalloffMode { Exponential, Linear, Curve }
    [System.Serializable] public class Chain { public List<Transform> bones = new List<Transform>(); }

    [Header("Left Hand: Finger Chains (proximal → tip)")]
    public Chain leftThumb = new Chain();
    public Chain leftIndex = new Chain();
    public Chain leftMiddle = new Chain();
    public Chain leftRing = new Chain();
    public Chain leftPinky = new Chain();

    [Header("Right Hand: Finger Chains (proximal → tip)")]
    public Chain rightThumb = new Chain();
    public Chain rightIndex = new Chain();
    public Chain rightMiddle = new Chain();
    public Chain rightRing = new Chain();
    public Chain rightPinky = new Chain();

    [Header("Inputs (0=open, 1=closed / 1=wide)")]
    [Range(0f, 1f)] public float leftCurl = 0f;
    [Range(0f, 1f)] public float leftSpread = 0f;
    [Range(0f, 1f)] public float rightCurl = 0f;
    [Range(0f, 1f)] public float rightSpread = 0f;

    [Header("Curl Settings (Non-Thumb)")]
    [Range(0f, 30f)] public float idleCurlDeg = 4f;
    [Range(0f, 3f)] public float idleCurlHz = 0.25f;
    [Range(0f, 180f)] public float maxCurlDeg = 120f;

    [Tooltip("Per-joint attenuation from root→tip. <1 decreases distal curl, 1 keeps it, >1 increases it (exponential mode).")]
    [Range(0f, 1.5f)] public float distalFalloff = 0.65f;

    [Tooltip("How to compute the distal attenuation along the chain.")]
    public DistalFalloffMode distalFalloffMode = DistalFalloffMode.Exponential;

    [Tooltip("Custom attenuation curve vs. normalized bone index (0=root, 1=tip). Used when Mode=Curve.")]
    public AnimationCurve distalFalloffCurve = AnimationCurve.Linear(0, 1, 1, 0.4f);

    [Range(0.1f, 30f)] public float curlLerp = 12f;

    [Tooltip("Random micro jitter (deg). Scaled by distal attenuation so tips don’t overpower.")]
    [Range(0f, 6f)] public float microCurlDeg = 0.8f;

    [Header("Thumb Settings (per-axis max curl)")]
    [Tooltip("Per-axis max curl (degrees) for the LEFT thumb when input=1.0")]
    public Vector3 leftThumbMaxCurlDegXYZ = new Vector3(0f, 0f, 100f);
    [Tooltip("Per-axis max curl (degrees) for the RIGHT thumb when input=1.0")]
    public Vector3 rightThumbMaxCurlDegXYZ = new Vector3(0f, 0f, 100f);

    [Tooltip("Thumb per-joint attenuation from root→tip.")]
    [Range(0f, 1.5f)] public float thumbDistalFalloff = 0.75f;

    public DistalFalloffMode thumbFalloffMode = DistalFalloffMode.Exponential;
    public AnimationCurve thumbFalloffCurve = AnimationCurve.Linear(0, 1, 1, 0.5f);

    [Header("Spread Settings (root only)")]
    [Range(0f, 20f)] public float idleSpreadDeg = 1.5f;
    [Range(0f, 60f)] public float maxSpreadDeg = 18f;
    public Axis spreadAxis = Axis.Y;
    public bool mirrorRightSpread = true;
    [Range(0.1f, 30f)] public float spreadLerp = 12f;

    [Header("Per-Finger Spread Weights (Left)")]
    [Range(-2f, 2f)] public float L_Index = +1f;
    [Range(-2f, 2f)] public float L_Middle = 0.2f;
    [Range(-2f, 2f)] public float L_Ring = -0.4f;
    [Range(-2f, 2f)] public float L_Pinky = -1.0f;
    [Range(-2f, 2f)] public float L_Thumb = +0.7f;

    [Header("Per-Finger Spread Weights (Right)")]
    [Range(-2f, 2f)] public float R_Index = +1f;
    [Range(-2f, 2f)] public float R_Middle = 0.2f;
    [Range(-2f, 2f)] public float R_Ring = -0.4f;
    [Range(-2f, 2f)] public float R_Pinky = -1.0f;
    [Range(-2f, 2f)] public float R_Thumb = +0.7f;

    // Baselines
    readonly List<Quaternion> _LThumb0 = new(), _LIndex0 = new(), _LMiddle0 = new(), _LRing0 = new(), _LPinky0 = new();
    readonly List<Quaternion> _RThumb0 = new(), _RIndex0 = new(), _RMiddle0 = new(), _RRing0 = new(), _RPinky0 = new();

    float _seedL, _seedR;

    void Awake()
    {
        Capture(leftThumb.bones, _LThumb0);
        Capture(leftIndex.bones, _LIndex0);
        Capture(leftMiddle.bones, _LMiddle0);
        Capture(leftRing.bones, _LRing0);
        Capture(leftPinky.bones, _LPinky0);

        Capture(rightThumb.bones, _RThumb0);
        Capture(rightIndex.bones, _RIndex0);
        Capture(rightMiddle.bones, _RMiddle0);
        Capture(rightRing.bones, _RRing0);
        Capture(rightPinky.bones, _RPinky0);

        _seedL = Random.Range(0f, 1000f);
        _seedR = Random.Range(0f, 1000f);
    }

    void LateUpdate()
    {
        float t = Time.time;
        float dt = Time.deltaTime;

        // Idle phases (desync hands slightly)
        float idlePhaseL = Mathf.Sin((t + 0.31f) * idleCurlHz * Mathf.PI * 2f);
        float idlePhaseR = Mathf.Sin((t + 0.79f) * idleCurlHz * Mathf.PI * 2f);

        // Compute per-hand spread degrees once (root-only spread)
        float baseSpreadL = idlePhaseL * idleSpreadDeg + leftSpread * maxSpreadDeg;
        float baseSpreadR = (idlePhaseR * idleSpreadDeg + rightSpread * maxSpreadDeg) * (mirrorRightSpread ? -1f : +1f);

        // LEFT
        AnimateThumbWithRootSpread(leftThumb.bones, _LThumb0, leftCurl, idlePhaseL, _seedL + 10.1f,
            leftThumbMaxCurlDegXYZ, thumbDistalFalloff, thumbFalloffMode, thumbFalloffCurve,
            degreeRoot: baseSpreadL * L_Thumb, dt);

        AnimateChainCurlWithRootSpread(leftIndex.bones, _LIndex0, leftCurl, idlePhaseL, _seedL + 20.2f,
            maxCurlDeg, distalFalloff, distalFalloffMode, distalFalloffCurve, Axis.X, degreeRoot: baseSpreadL * L_Index, dt: dt);

        AnimateChainCurlWithRootSpread(leftMiddle.bones, _LMiddle0, leftCurl, idlePhaseL, _seedL + 30.3f,
            maxCurlDeg, distalFalloff, distalFalloffMode, distalFalloffCurve, Axis.X, degreeRoot: baseSpreadL * L_Middle, dt: dt);

        AnimateChainCurlWithRootSpread(leftRing.bones, _LRing0, leftCurl, idlePhaseL, _seedL + 40.4f,
            maxCurlDeg, distalFalloff, distalFalloffMode, distalFalloffCurve, Axis.X, degreeRoot: baseSpreadL * L_Ring, dt: dt);

        AnimateChainCurlWithRootSpread(leftPinky.bones, _LPinky0, leftCurl, idlePhaseL, _seedL + 50.5f,
            maxCurlDeg, distalFalloff, distalFalloffMode, distalFalloffCurve, Axis.X, degreeRoot: baseSpreadL * L_Pinky, dt: dt);

        // RIGHT
        AnimateThumbWithRootSpread(rightThumb.bones, _RThumb0, rightCurl, idlePhaseR, _seedR + 10.1f,
            rightThumbMaxCurlDegXYZ, thumbDistalFalloff, thumbFalloffMode, thumbFalloffCurve,
            degreeRoot: baseSpreadR * R_Thumb, dt);

        AnimateChainCurlWithRootSpread(rightIndex.bones, _RIndex0, rightCurl, idlePhaseR, _seedR + 20.2f,
            maxCurlDeg, distalFalloff, distalFalloffMode, distalFalloffCurve, Axis.X, degreeRoot: baseSpreadR * R_Index, dt: dt);

        AnimateChainCurlWithRootSpread(rightMiddle.bones, _RMiddle0, rightCurl, idlePhaseR, _seedR + 30.3f,
            maxCurlDeg, distalFalloff, distalFalloffMode, distalFalloffCurve, Axis.X, degreeRoot: baseSpreadR * R_Middle, dt: dt);

        AnimateChainCurlWithRootSpread(rightRing.bones, _RRing0, rightCurl, idlePhaseR, _seedR + 40.4f,
            maxCurlDeg, distalFalloff, distalFalloffMode, distalFalloffCurve, Axis.X, degreeRoot: baseSpreadR * R_Ring, dt: dt);

        AnimateChainCurlWithRootSpread(rightPinky.bones, _RPinky0, rightCurl, idlePhaseR, _seedR + 50.5f,
            maxCurlDeg, distalFalloff, distalFalloffMode, distalFalloffCurve, Axis.X, degreeRoot: baseSpreadR * R_Pinky, dt: dt);
    }

    // ---------- Helpers ----------

    static float DistalStep(int i, int count, float falloff, DistalFalloffMode mode, AnimationCurve curve)
    {
        if (count <= 1) return 1f;
        float u = Mathf.Clamp01(count > 1 ? (float)i / (count - 1) : 0f); // 0=root, 1=tip

        switch (mode)
        {
            case DistalFalloffMode.Linear:
                {
                    // falloff interpreted as tip factor (0..1). Root=1, Tip=falloff.
                    float tip = Mathf.Clamp01(falloff);
                    return Mathf.Lerp(1f, tip, u);
                }
            case DistalFalloffMode.Curve:
                {
                    // curve(0)=1 at root, curve(1)=whatever tip factor you want.
                    float v = (curve == null) ? 1f : curve.Evaluate(u);
                    return Mathf.Max(0f, v);
                }
            default: // Exponential
                {
                    // Classic pow model. falloff<1 → decreasing; 1 → flat; >1 → increasing.
                    float baseVal = Mathf.Clamp(falloff, 0.0001f, 2f);
                    return Mathf.Pow(baseVal, i);
                }
        }
    }

    void AnimateThumbWithRootSpread(List<Transform> chain, List<Quaternion> bases, float input, float idlePhase, float seed,
                                    Vector3 maxDegXYZ, float falloff, DistalFalloffMode mode, AnimationCurve curve,
                                    float degreeRoot, float dt)
    {
        if (chain == null || bases == null) return;

        float idleCurl = idlePhase * idleCurlDeg; // subtle life
        Vector3 rootCurlVec = maxDegXYZ * Mathf.Clamp01(input) + new Vector3(idleCurl, idleCurl, idleCurl);

        int n = chain.Count;
        for (int i = 0; i < n && i < bases.Count; i++)
        {
            var tr = chain[i]; if (!tr) continue;

            float step = DistalStep(i, n, falloff, mode, curve);
            float nse = (Mathf.PerlinNoise(Time.time * 1.1f, seed + i * 0.37f) - 0.5f) * microCurlDeg * Mathf.Max(0.2f, step);

            Vector3 curlDeg = (rootCurlVec * step) + new Vector3(nse, nse, nse);
            Quaternion qCurl = Quaternion.Euler(curlDeg);
            Quaternion qSpread = (i == 0) ? AxisAngle(spreadAxis, degreeRoot) : Quaternion.identity;

            Quaternion target = bases[i] * qSpread * qCurl;
            tr.localRotation = Quaternion.Slerp(tr.localRotation, target, 1f - Mathf.Exp(-curlLerp * dt));
        }
    }

    void AnimateChainCurlWithRootSpread(List<Transform> chain, List<Quaternion> bases, float input, float idlePhase, float seed,
                                        float maxDeg, float falloff, DistalFalloffMode mode, AnimationCurve curve,
                                        Axis axis, float degreeRoot, float dt)
    {
        if (chain == null || bases == null) return;

        float rootCurl = idlePhase * idleCurlDeg + Mathf.Clamp01(input) * maxDeg;

        int n = chain.Count;
        for (int i = 0; i < n && i < bases.Count; i++)
        {
            var tr = chain[i]; if (!tr) continue;

            float step = DistalStep(i, n, falloff, mode, curve);
            float noise = (Mathf.PerlinNoise(Time.time * 1.1f, seed + i * 0.37f) - 0.5f) * microCurlDeg * Mathf.Max(0.2f, step);
            float curlDeg = (rootCurl * step) + noise;

            Quaternion qCurl = axis switch
            {
                Axis.X => Quaternion.Euler(curlDeg, 0f, 0f),
                Axis.Y => Quaternion.Euler(0f, curlDeg, 0f),
                _ => Quaternion.Euler(0f, 0f, curlDeg),
            };

            Quaternion qSpread = (i == 0) ? AxisAngle(spreadAxis, degreeRoot) : Quaternion.identity;
            Quaternion target = bases[i] * qSpread * qCurl;

            tr.localRotation = Quaternion.Slerp(tr.localRotation, target, 1f - Mathf.Exp(-curlLerp * dt));
        }
    }

    static Quaternion AxisAngle(Axis axis, float deg)
    {
        return axis switch
        {
            Axis.X => Quaternion.Euler(deg, 0f, 0f),
            Axis.Y => Quaternion.Euler(0f, deg, 0f),
            _ => Quaternion.Euler(0f, 0f, deg),
        };
    }

    static void Capture(List<Transform> chain, List<Quaternion> dst)
    {
        dst.Clear();
        foreach (var b in chain) dst.Add(b ? b.localRotation : Quaternion.identity);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // Ensure reasonable curves for root=1
        if (distalFalloffMode == DistalFalloffMode.Curve && distalFalloffCurve != null)
        {
            // Encourage curve(0) ~ 1 if user left it near 0 by mistake
            if (Mathf.Abs(distalFalloffCurve.Evaluate(0f) - 1f) > 0.2f)
                distalFalloffCurve = AnimationCurve.Linear(0, 1, 1, distalFalloffCurve.Evaluate(1f));
        }
        if (thumbFalloffMode == DistalFalloffMode.Curve && thumbFalloffCurve != null)
        {
            if (Mathf.Abs(thumbFalloffCurve.Evaluate(0f) - 1f) > 0.2f)
                thumbFalloffCurve = AnimationCurve.Linear(0, 1, 1, thumbFalloffCurve.Evaluate(1f));
        }
    }
#endif
}
