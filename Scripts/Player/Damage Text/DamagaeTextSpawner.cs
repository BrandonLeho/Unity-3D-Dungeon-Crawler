// DamageTextSpawner.cs
using UnityEngine;

public class DamageTextSpawner : MonoBehaviour
{
    [Tooltip("Optional: where the number should appear (defaults to this.transform)")]
    [SerializeField] Transform anchor;

    [Tooltip("Extra local world-space offset from anchor (e.g., above the head).")]
    [SerializeField] Vector3 worldOffset = new Vector3(0f, 0f, 0f);

    Transform Anchor => anchor ? anchor : transform;

    /// <summary>
    /// Call this from the object BEING HIT.
    /// </summary>
    public void ShowDamage(int amount, bool isCrit)
    {
        if (!DamageTextManager.Instance) return;

        Vector3 spawnPos = Anchor.position + Anchor.TransformVector(worldOffset);
        DamageTextManager.Instance.Spawn(Anchor, spawnPos, amount, isCrit);
    }
}
