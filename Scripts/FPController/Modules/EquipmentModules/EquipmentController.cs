using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DefaultExecutionOrder(+5)]
public class EquipmentController : MonoBehaviour
{
    [Header("Auto-wiring")]
    [SerializeField] FPController controller;   // auto-filled
    [SerializeField] Transform handSocket;      // created under camera if missing

    [Header("Slots")]
    [SerializeField, Min(1)] int maxSlots = 6;
    [SerializeField] bool wrapAround = true;
    [SerializeField] float swapCooldown = 0.12f;

    [Header("HUD")]
    [SerializeField] TMP_Text sharedAmmoText;

    [Header("Starting Items (children allowed)")]
    [SerializeField] List<HandEquipment> startingItems = new();

    public int ActiveIndex { get; private set; } = -1;
    public HandEquipment ActiveItem => (ActiveIndex >= 0 && ActiveIndex < slots.Count) ? slots[ActiveIndex] : null;

    public event Action<int, HandEquipment> OnItemEquipped;
    public event Action<int, HandEquipment> OnItemUnequipped;

    readonly List<HandEquipment> slots = new();
    float nextSwapTime;

    void Reset() => AutoWire();
    void OnValidate() => AutoWire();

    void AutoWire()
    {
        if (!controller) controller = GetComponentInParent<FPController>();
        if (!controller) return;

        if (!handSocket && controller.CameraTransform)
        {
            var hs = controller.CameraTransform.Find("HandSocket");
            if (!hs)
            {
                var go = new GameObject("HandSocket");
                hs = go.transform;
                hs.SetParent(controller.CameraTransform, false);
                // place slightly forward/right/down to taste:
                hs.localPosition = new Vector3(0.25f, -0.25f, 0.6f);
                hs.localRotation = Quaternion.identity;
            }
            handSocket = hs;
        }
    }

    void Awake()
    {
        AutoWire();

        // make sure we have capacity for starting items
        int capacity = Mathf.Max(maxSlots, startingItems.Count);
        for (int i = 0; i < capacity; i++) slots.Add(null);

        // Parent & disable starting items
        foreach (var item in startingItems)
        {
            if (!item) continue;
            if (handSocket && item.transform.parent != handSocket)
                item.transform.SetParent(handSocket, false);
            item.OnUnequip();
            AddOrStack(item);
        }

        int first = FindNextOccupied(-1, +1);
        if (first != -1)
        {
            EquipIndex(first);
            UpdateAmmoHudForCurrent();   // <--- ADD THIS
        }
        else
        {
            UpdateAmmoHudForNone();      // <--- and this fallback
        }

        // Hook attack so items can respond to your player’s TryAttack
        if (controller != null)
            controller.TryAttack += OnTryAttack;  // FPController exposes this UnityAction
    }

    void OnDestroy()
    {
        if (controller != null)
            controller.TryAttack -= OnTryAttack;
    }

    void OnTryAttack()
    {
        ActiveItem?.PrimaryUse();
    }

    // ---------------- Public API ----------------

    /// Scroll delta: positive=next, negative=prev
    public void Scroll(float delta)
    {
        if (Mathf.Approximately(delta, 0f)) return;
        if (Time.time < nextSwapTime) return;

        int dir = delta > 0f ? +1 : -1;
        int next = FindNextOccupied(ActiveIndex, dir);
        if (next != -1) EquipIndex(next);
        nextSwapTime = Time.time + swapCooldown;
    }

    public void EquipSlot(int index)
    {
        if (Time.time < nextSwapTime) return;
        if (index < 0 || index >= slots.Count) return;
        if (slots[index] == null || index == ActiveIndex) return;
        EquipIndex(index);
        nextSwapTime = Time.time + swapCooldown;
    }

    public bool AddOrStack(HandEquipment item)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] == null)
            {
                slots[i] = item;
                PrepareItem(item);
                if (ActiveIndex == -1) EquipIndex(i);
                return true;
            }
        }
        return false;
    }

    public void RemoveAt(int index, bool destroy = false)
    {
        if (index < 0 || index >= slots.Count) return;
        var item = slots[index];
        if (!item) return;

        if (index == ActiveIndex)
        {
            item.OnUnequip();
            OnItemUnequipped?.Invoke(index, item);
            ActiveIndex = -1;
        }

        slots[index] = null;
        if (destroy && item) Destroy(item.gameObject);

        int next = FindNextOccupied(index, +1);
        if (next == -1) next = FindNextOccupied(index, -1);
        if (next != -1) EquipIndex(next);
    }

    // ---------------- Internals ----------------

    void PrepareItem(HandEquipment item)
    {
        if (!item) return;
        if (handSocket && item.transform.parent != handSocket)
            item.transform.SetParent(handSocket, false);
        item.OnUnequip();
    }

    int FindNextOccupied(int from, int dir)
    {
        if (slots.Count == 0) return -1;
        int steps = 0, i = from;

        while (steps < slots.Count)
        {
            i += dir;
            if (wrapAround)
            {
                if (i < 0) i = slots.Count - 1;
                if (i >= slots.Count) i = 0;
            }
            else if (i < 0 || i >= slots.Count) return -1;

            if (slots[i] != null) return i;
            steps++;
        }
        return -1;
    }

    void EquipIndex(int index)
    {
        if (index == ActiveIndex) return;

        // Unbind old
        if (ActiveIndex != -1 && slots[ActiveIndex] != null)
        {
            var oldComp = slots[ActiveIndex] as Component;
            var oldAmmo = (oldComp?.GetComponentInChildren<IAmmoUser>(true));
            if (oldAmmo != null) oldAmmo.SetAmmoText(null);

            var old = slots[ActiveIndex];
            old.OnUnequip();
            OnItemUnequipped?.Invoke(ActiveIndex, old);
        }

        // Equip new
        ActiveIndex = index;
        var cur = slots[ActiveIndex];
        if (cur != null)
        {
            cur.OnEquip();
            OnItemEquipped?.Invoke(ActiveIndex, cur);
            UpdateAmmoHudForCurrent();      // <- bind to the new item now
        }
        else
        {
            UpdateAmmoHudForNone();
        }
    }




    void UpdateAmmoHudForCurrent()
    {
        if (!sharedAmmoText) return;

        // Active item might be a parent. Search the hierarchy.
        IAmmoUser ammoUser = null;
        if (ActiveItem != null)
        {
            // Try directly on the equipped component
            ammoUser = ActiveItem as IAmmoUser;

            // If not found, try on the same GameObject
            if (ammoUser == null)
                ammoUser = (ActiveItem as Component)?.GetComponent<IAmmoUser>();

            // If still not found, try children (inactive included in case you enable during OnEquip)
            if (ammoUser == null && ActiveItem is Component comp)
                ammoUser = comp.GetComponentInChildren<IAmmoUser>(true);
        }

        if (ammoUser != null && ammoUser.WantsAmmoUI)
        {
            sharedAmmoText.gameObject.SetActive(true);
            ammoUser.SetAmmoText(sharedAmmoText);
            // Debug.Log($"[HUD] Bound ammo HUD to {ActiveItem.GetType().Name}");
        }
        else
        {
            UpdateAmmoHudForNone();
            // Debug.Log("[HUD] Item has no ammo UI; hiding label.");
        }
    }

    void UpdateAmmoHudForNone()
    {
        if (!sharedAmmoText) return;
        sharedAmmoText.text = string.Empty;
        sharedAmmoText.gameObject.SetActive(false);
    }


}
