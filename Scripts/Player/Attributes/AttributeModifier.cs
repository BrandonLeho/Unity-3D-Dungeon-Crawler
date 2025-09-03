// AttributeModifier.cs
using System;

[Serializable]
public struct AttributeModifier
{
    public AttributeType Type;

    // Additive applied after base: base + Add
    public float Add;

    // Multiplicative as a factor on top: (base + Add) * (1 + Mult)
    public float Mult;

    // Optional lifetime (<=0 means permanent until removed)
    public float Duration;

    // Optional tag to remove by source (e.g., item instance, effect id)
    public UnityEngine.Object Source;

    public AttributeModifier(AttributeType type, float add = 0, float mult = 0, float duration = 0, UnityEngine.Object source = null)
    {
        Type = type; Add = add; Mult = mult; Duration = duration; Source = source;
    }
}
