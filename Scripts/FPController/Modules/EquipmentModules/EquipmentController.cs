using System;
using System.Collections.Generic;
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

        // Equip first non-empty slot
        int first = FindNextOccupied(-1, +1);
        if (first != -1) EquipIndex(first);

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

        if (ActiveIndex != -1 && slots[ActiveIndex] != null)
        {
            var old = slots[ActiveIndex];
            old.OnUnequip();
            OnItemUnequipped?.Invoke(ActiveIndex, old);
        }

        ActiveIndex = index;
        var cur = slots[ActiveIndex];
        if (cur != null)
        {
            cur.OnEquip();
            OnItemEquipped?.Invoke(ActiveIndex, cur);
        }
    }
}
