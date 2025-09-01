using System;
using System.Collections.Generic;
using UnityEngine;

[Flags]
public enum LockCategory
{
    None = 0,
    EquipmentSwitch = 1 << 0,   // blocks SetActiveSlot / switching
    AnimationStart = 1 << 1,   // blocks starting new animations
    AttackUse = 1 << 2,   // blocks weapon PrimaryUse (optional)
    Interact = 1 << 3,   // e.g., block world interaction (optional)
    Movement = 1 << 4,   // e.g., block movement (optional)
    All = ~0
}

/// Central lock service. Acquire() returns a handle; dispose/release it to unlock.
/// Locks are additive (reference-counted) per category. You can cancel by tag/priority.
[DefaultExecutionOrder(-10000)]
public sealed class GameplayLock : MonoBehaviour
{
    public static GameplayLock Instance { get; private set; }

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(this); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public static GameplayLock GetOrCreate()
    {
        if (!Instance)
        {
            var go = new GameObject("GameplayLock");
            Instance = go.AddComponent<GameplayLock>();
            DontDestroyOnLoad(go);
        }
        return Instance;
    }

    public static bool Any(LockCategory mask) =>
        Instance && Instance.IsLockedAny(mask);

    public event Action OnLockStateChanged;

    struct Token
    {
        public int id;
        public LockCategory mask;
        public int priority;
        public string tag;
        public UnityEngine.Object owner;
        public bool active;
    }

    readonly Dictionary<int, Token> _tokens = new();
    int _nextId = 1;
    readonly int[] _categoryCounts = new int[32]; // per-bit counts

    public bool IsLockedAny(LockCategory mask) => GetCountForMask(mask) > 0;

    public LockHandle Acquire(LockCategory mask,
                              UnityEngine.Object owner = null,
                              string tag = null,
                              int priority = 0)
    {
        int id = _nextId++;
        var t = new Token
        {
            id = id,
            mask = mask,
            owner = owner,
            tag = tag ?? string.Empty,
            priority = priority,
            active = true
        };
        _tokens[id] = t;
        AddCounts(mask);
        OnLockStateChanged?.Invoke();
        return new LockHandle(this, id);
    }

    internal void Release(int id)
    {
        if (!_tokens.TryGetValue(id, out var t) || !t.active) return;
        t.active = false;
        _tokens[id] = t;
        SubCounts(t.mask);
        OnLockStateChanged?.Invoke();
    }

    public int CancelByTag(string tag, int minPriority = int.MinValue)
    {
        int n = 0;
        foreach (var kv in new List<KeyValuePair<int, Token>>(_tokens))
        {
            var t = kv.Value;
            if (t.active && t.tag == tag && t.priority >= minPriority)
            {
                Release(kv.Key);
                n++;
            }
        }
        return n;
    }

    public int CancelIf(Func<string, int, UnityEngine.Object, bool> predicate)
    {
        int n = 0;
        foreach (var kv in new List<KeyValuePair<int, Token>>(_tokens))
        {
            var t = kv.Value;
            if (t.active && predicate(t.tag, t.priority, t.owner))
            {
                Release(kv.Key);
                n++;
            }
        }
        return n;
    }

    int GetCountForMask(LockCategory mask)
    {
        int cnt = 0;
        int m = (int)mask;
        for (int bit = 0; bit < 32; bit++)
            if ((m & (1 << bit)) != 0) cnt += _categoryCounts[bit];
        return cnt;
    }

    void AddCounts(LockCategory mask)
    {
        int m = (int)mask;
        for (int bit = 0; bit < 32; bit++)
            if ((m & (1 << bit)) != 0) _categoryCounts[bit]++;
    }

    void SubCounts(LockCategory mask)
    {
        int m = (int)mask;
        for (int bit = 0; bit < 32; bit++)
            if ((m & (1 << bit)) != 0) _categoryCounts[bit] = Mathf.Max(0, _categoryCounts[bit] - 1);
    }
}

public readonly struct LockHandle : IDisposable
{
    readonly GameplayLock _mgr;
    readonly int _id;
    internal LockHandle(GameplayLock mgr, int id) { _mgr = mgr; _id = id; }
    public void Dispose() => _mgr?.Release(_id);
    public void Release() => _mgr?.Release(_id);
}
