// IAttributeProvider.cs
public interface IAttributeProvider
{
    /// Current value including modifiers.
    float Get(AttributeType type);

    /// Base value (no modifiers).
    float GetBase(AttributeType type);

    /// Set base value (triggers change event).
    void SetBase(AttributeType type, float value);

    /// Subscribe to changes (UI bars, AI, etc.)
    event System.Action<AttributeType, float> OnAttributeChanged;
}
