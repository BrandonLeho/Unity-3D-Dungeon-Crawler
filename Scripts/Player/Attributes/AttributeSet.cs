// AttributeSet.cs
using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-50)]
public class AttributeSet : MonoBehaviour, IAttributeProvider
{
    [Serializable]
    public struct Entry
    {
        public AttributeType Type;
        public float BaseValue;
    }

    [Header("Base Attributes")]
    [SerializeField]
    private List<Entry> _base = new List<Entry>
    {
        new Entry{ Type = AttributeType.Strength,  BaseValue = 10 },
        new Entry{ Type = AttributeType.Dexterity, BaseValue = 10 },
        new Entry{ Type = AttributeType.Intellect, BaseValue = 10 },
        new Entry{ Type = AttributeType.Vitality,  BaseValue = 10 },
        new Entry{ Type = AttributeType.Luck,      BaseValue = 5  },
    };

    public event Action<AttributeType, float> OnAttributeChanged;

    readonly Dictionary<AttributeType, float> _baseMap = new();
    readonly Dictionary<AttributeType, float> _cached = new();              // last computed
    readonly List<AttributeModifier> _mods = new();
    readonly List<int> _toRemove = new();

    void Awake()
    {
        _baseMap.Clear();
        foreach (var e in _base) _baseMap[e.Type] = e.BaseValue;
        RecomputeAll();
    }

    void Update()
    {
        // Expire duration-based modifiers
        if (_mods.Count == 0) return;

        for (int i = 0; i < _mods.Count; i++)
        {
            if (_mods[i].Duration > 0)
            {
                var m = _mods[i];
                m.Duration -= Time.deltaTime;
                _mods[i] = m;
                if (m.Duration <= 0) _toRemove.Add(i);
            }
        }
        if (_toRemove.Count > 0)
        {
            _toRemove.Sort(); _toRemove.Reverse();
            foreach (var idx in _toRemove) _mods.RemoveAt(idx);
            _toRemove.Clear();
            RecomputeAll();
        }
    }

    public float Get(AttributeType type) => _cached.TryGetValue(type, out var v) ? v : GetAndCache(type);

    public float GetBase(AttributeType type) => _baseMap.TryGetValue(type, out var v) ? v : 0f;

    public void SetBase(AttributeType type, float value)
    {
        _baseMap[type] = value;
        Recompute(type);
    }

    public void AddModifier(in AttributeModifier mod)
    {
        _mods.Add(mod);
        Recompute(mod.Type);
    }

    public void RemoveModifiersBySource(UnityEngine.Object source)
    {
        if (source == null) return;
        _mods.RemoveAll(m => m.Source == source);
        RecomputeAll();
    }

    public IReadOnlyList<AttributeModifier> GetAllModifiers() => _mods;

    float GetAndCache(AttributeType type)
    {
        var v = Compute(type);
        _cached[type] = v;
        return v;
    }

    float Compute(AttributeType type)
    {
        var baseVal = GetBase(type);
        float add = 0f;
        float mult = 0f;
        for (int i = 0; i < _mods.Count; i++)
        {
            if (_mods[i].Type != type) continue;
            add += _mods[i].Add;
            mult += _mods[i].Mult;
        }
        return (baseVal + add) * (1f + mult);
    }

    void Recompute(AttributeType type)
    {
        var old = _cached.TryGetValue(type, out var prev) ? prev : float.NaN;
        var now = Compute(type);
        _cached[type] = now;
        if (!Mathf.Approximately(old, now))
            OnAttributeChanged?.Invoke(type, now);
    }

    void RecomputeAll()
    {
        foreach (AttributeType t in Enum.GetValues(typeof(AttributeType)))
        {
            Recompute(t);
        }
    }
}
