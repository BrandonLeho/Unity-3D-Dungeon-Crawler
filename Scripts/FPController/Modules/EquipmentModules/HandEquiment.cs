using UnityEngine;

public class HandEquipment : MonoBehaviour
{
    [Header("Info")]
    public string displayName = "Unnamed";
    public Sprite icon;

    [Header("Optional toggles when equipped")]
    [SerializeField] GameObject[] enableOnEquip;

    public virtual void OnEquip()
    {
        gameObject.SetActive(true);
        if (enableOnEquip != null)
            foreach (var go in enableOnEquip) if (go) go.SetActive(true);
    }

    public virtual void OnUnequip()
    {
        if (enableOnEquip != null)
            foreach (var go in enableOnEquip) if (go) go.SetActive(false);
        gameObject.SetActive(false);
    }

    /// Called when `FPController.TryAttack` fires or you manually invoke use.
    public virtual void PrimaryUse() { }
}
