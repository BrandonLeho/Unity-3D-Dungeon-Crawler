using UnityEngine;

public interface IBreakable
{
    bool IsBroken { get; }
    void Break(Vector3 hitPoint, Vector3 hitNormal, object source = null);
}
