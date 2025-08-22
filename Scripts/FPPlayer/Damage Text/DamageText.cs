using UnityEngine;
using TMPro;

public class DamageText : MonoBehaviour
{
    [Header("References (wired by manager)")]
    [SerializeField] TMP_Text text;

    // Runtime state
    Transform followTarget;
    Vector3 worldSpawnPos;
    Vector3 baseScreenOffset;
    Camera cam;
    System.Func<float, float> jitterFactorByDistance;

    // Tunables (passed in by manager)
    float lifetime, growTime, shrinkTime, holdTime;
    float startScale, endScale, critScaleMul;
    Color normalColor, critColor;
    Vector2 randomScreenJitter;
    bool isCrit;

    float age;

    public struct Settings
    {
        public float lifetime, growTime, shrinkTime, holdTime;
        public float startScale, endScale, critScaleMul;
        public Color normalColor, critColor;
        public Vector2 randomScreenJitter;
    }

    public void Init(
        Camera activeCamera,
        Transform follow,
        Vector3 worldPos,
        int amount,
        bool crit,
        Settings s,
        System.Func<float, float> jitterEvaluator = null)
    {
        cam = activeCamera;
        followTarget = follow;
        worldSpawnPos = worldPos;
        isCrit = crit;

        lifetime = s.lifetime;
        growTime = s.growTime;
        shrinkTime = s.shrinkTime;
        holdTime = s.holdTime;
        startScale = s.startScale;
        endScale = s.endScale;
        critScaleMul = s.critScaleMul;
        normalColor = s.normalColor;
        critColor = s.critColor;
        randomScreenJitter = s.randomScreenJitter;

        jitterFactorByDistance = jitterEvaluator;

        if (!text) text = GetComponent<TMP_Text>();
        text.text = amount.ToString();
        text.color = isCrit ? critColor : normalColor;

        float mul = isCrit ? critScaleMul : 1f;
        transform.localScale = Vector3.one * (startScale * mul);

        baseScreenOffset = new Vector3(
            Random.Range(-randomScreenJitter.x, randomScreenJitter.x),
            Random.Range(0, randomScreenJitter.y * 2f),
            0f
        );

        UpdateScreenPosition(0f);
    }

    void Update()
    {
        age += Time.deltaTime;
        if (age >= lifetime) { Destroy(gameObject); return; }

        AnimateScale();
        AnimateFade();
        UpdateScreenPosition(Time.deltaTime);
    }

    void AnimateScale()
    {
        float mul = isCrit ? critScaleMul : 1f;
        float s0 = startScale * mul;
        float s1 = endScale * mul;

        float scale = s1;
        if (age <= growTime)
        {
            float t = Mathf.Clamp01(age / growTime);
            scale = Mathf.Lerp(s0, s1, t);
        }
        else if (age <= growTime + holdTime)
        {
            scale = s1;
        }
        else if (age <= growTime + holdTime + shrinkTime)
        {
            scale = s1; // reserved for extra shrink curve if needed
        }
        transform.localScale = Vector3.one * scale;
    }

    void AnimateFade()
    {
        float fadeStart = lifetime * 0.65f;
        float alpha = (age < fadeStart) ? 1f : Mathf.InverseLerp(lifetime, fadeStart, age);
        var c = text.color;
        c.a = alpha;
        text.color = c;
    }

    void UpdateScreenPosition(float dt)
    {
        if (!cam) cam = Camera.main;
        Vector3 wp = followTarget ? followTarget.position : worldSpawnPos;
        wp += Vector3.up * 0.4f;

        Vector3 sp = cam ? cam.WorldToScreenPoint(wp) : wp;
        if (cam && sp.z < 0f) sp.z = 0.1f;

        float dist = cam ? Vector3.Distance(cam.transform.position, wp) : 0f;
        float factor = jitterFactorByDistance != null ? jitterFactorByDistance(dist) : 1f;

        transform.position = sp + baseScreenOffset * factor;
    }
}
