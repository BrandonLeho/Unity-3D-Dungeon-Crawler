using UnityEngine;
using AIToolkit;

[DisallowMultipleComponent]
public class HearingSensor : MonoBehaviour
{
    [Header("Hearing")]
    [SerializeField] float maxHearRadius = 20f;     // at Loudness=1
    [SerializeField] float minLoudnessToHear = 0.05f;
    [SerializeField] LayerMask obstructionMask;     // optional occlusion test (walls)

    [Header("Output")]
    public System.Action<AISignal> OnSignal;

    [Header("Debug")]
    [SerializeField] bool drawGizmos = false;
    Vector3 lastHeard;
    float lastHeardTime;

    void OnEnable() => NoiseEvents.Emitted += OnNoise;
    void OnDisable() => NoiseEvents.Emitted -= OnNoise;

    void OnNoise(Noise n)
    {
        float radius = Mathf.Max(0f, maxHearRadius) * Mathf.Clamp01(n.Loudness);
        if (radius < 0.001f || n.Loudness < minLoudnessToHear) return;

        float d = Vector3.Distance(transform.position, n.Position);
        if (d > radius) return;

        // Optional simple occlusion: a wall halves effective loudness
        if (obstructionMask.value != 0 &&
            Physics.Linecast(n.Position, transform.position, obstructionMask, QueryTriggerInteraction.Ignore))
        {
            // reduce strength if occluded
            if (n.Loudness * 0.5f < minLoudnessToHear) return;
        }

        float strength = Mathf.Clamp01(1f - d / radius);
        lastHeard = n.Position; lastHeardTime = n.Time;

        OnSignal?.Invoke(new AISignal(SignalType.Hearing, n.Source, n.Position, Vector3.zero, strength, n.Time));
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        Gizmos.color = new Color(1f, 0.75f, 0f, 0.15f);
        Gizmos.DrawWireSphere(transform.position, maxHearRadius);
        if (lastHeardTime > 0f)
        {
            Gizmos.color = new Color(1f, 0.75f, 0f, 0.6f);
            Gizmos.DrawSphere(lastHeard, 0.2f);
        }
    }
}
