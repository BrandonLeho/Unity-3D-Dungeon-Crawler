using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(+25)] // After EquipmentController (+5), before camera render
public class EquipmentSwitchAnimator : MonoBehaviour
{
    [Header("References")]
    public EquipmentController equipmentController;   // Auto-found if null
    public Transform handRig;                         // Required: pivot for hand/item assembly

    [Header("Motion")]
    public Vector3 downLocalOffset = new Vector3(0f, -0.22f, 0f);   // Local position offset when down
    public Vector3 downLocalEuler = new Vector3(12f, 0f, 0f);       // Local rotation offset when down

    [Header("Timing")]
    public float downDuration = 0.12f;
    public float upDuration = 0.14f;
    public float bottomHold = 0.03f;                               // Delay at bottom

    [Header("Easing")]
    [Range(0f, 1f)] public float easeStrength = 1f;                // 0=linear, 1=smooth step

    public enum RevealTiming { AtBottom, OnRiseHalf, AtTop }
    [Header("Reveal")]
    public RevealTiming reveal = RevealTiming.OnRiseHalf;          // When to show new item

    [Header("During Swap")]
    public bool blockAttackDuringSwap = true;                      // Prevent firing/attacks
    [Range(0f, 1f)] public float swapFingerCurl = 0.1f;            // Temporary reduced finger curl

    [Header("Safety")]
    public bool interruptible = true;                              // Restart mid-animation if needed

    // Internals
    Vector3 _rigPos0;
    Quaternion _rigRot0;
    Coroutine _co;
    Component _renderRootNew;
    List<Renderer> _newItemRenderers = new List<Renderer>();
    bool _isSwapping;
    ProceduralIdleAnimator _idle; // Optional finger curl driver

    void Reset()
    {
        if (!equipmentController) equipmentController = GetComponentInParent<EquipmentController>();
    }

    void Awake()
    {
        if (!equipmentController) equipmentController = GetComponentInParent<EquipmentController>();
        _idle = GetComponent<ProceduralIdleAnimator>();

        if (!handRig)
        {
            Debug.LogWarning("[EquipmentSwitchAnimator] Assign a Hand Rig transform.", this);
            enabled = false;
            return;
        }

        _rigPos0 = handRig.localPosition;
        _rigRot0 = handRig.localRotation;
    }

    void OnEnable()
    {
        if (equipmentController != null)
        {
            equipmentController.OnItemUnequipped += OnUnequipped;
            equipmentController.OnItemEquipped += OnEquipped;
        }
    }

    void OnDisable()
    {
        if (equipmentController != null)
        {
            equipmentController.OnItemUnequipped -= OnUnequipped;
            equipmentController.OnItemEquipped -= OnEquipped;
        }
    }

    void OnUnequipped(int index, HandEquipment item) => StartSwapSequence(true);

    void OnEquipped(int index, HandEquipment item)
    {
        CacheAndHideNewItem(item);
        if (!_isSwapping) StartSwapSequence(true);
    }

    void CacheAndHideNewItem(HandEquipment item)
    {
        _newItemRenderers.Clear();
        _renderRootNew = item as Component;
        if (_renderRootNew != null)
        {
            (_renderRootNew as Component).GetComponentsInChildren(true, _newItemRenderers);
            foreach (var r in _newItemRenderers) r.enabled = false;
        }
    }

    void RevealNewItemIfNeeded(RevealTiming when, RevealTiming now)
    {
        if (when != now) return;
        foreach (var r in _newItemRenderers) if (r) r.enabled = true;
        _newItemRenderers.Clear();
    }

    void StartSwapSequence(bool startGoingDown)
    {
        if (_co != null)
        {
            if (interruptible) StopCoroutine(_co);
            else return;
        }
        _co = StartCoroutine(CoSwap(startGoingDown));
    }

    IEnumerator CoSwap(bool startGoingDown)
    {
        _isSwapping = true;
        float originalCurl = _idle ? _idle.fingerCurlInput : 0f;

        if (blockAttackDuringSwap)
        {
            // Example: controller.CanAttack = false; (if available)
        }

        if (_idle) _idle.fingerCurlInput = Mathf.Min(_idle.fingerCurlInput, swapFingerCurl);

        // Down
        if (startGoingDown)
            yield return AnimateRig(handRig.localPosition, _rigPos0 + downLocalOffset,
                                    handRig.localRotation, _rigRot0 * Quaternion.Euler(downLocalEuler),
                                    downDuration, RevealTiming.AtBottom);

        // Hold
        if (bottomHold > 0f)
        {
            float t = 0f;
            while (t < bottomHold)
            {
                t += Time.deltaTime;
                RevealNewItemIfNeeded(reveal, RevealTiming.AtBottom);
                yield return null;
            }
        }
        else
        {
            RevealNewItemIfNeeded(reveal, RevealTiming.AtBottom);
        }

        // Up
        yield return AnimateRig(_rigPos0 + downLocalOffset, _rigPos0,
                                _rigRot0 * Quaternion.Euler(downLocalEuler), _rigRot0,
                                upDuration, RevealTiming.OnRiseHalf);

        // Final reveal at top if needed
        RevealNewItemIfNeeded(reveal, RevealTiming.AtTop);

        // Restore finger curl & controls
        if (_idle)
            _idle.fingerCurlInput = Mathf.Lerp(_idle.fingerCurlInput, originalCurl, 0.5f);

        if (blockAttackDuringSwap)
        {
            // Example: controller.CanAttack = true;
        }

        _isSwapping = false;
        _co = null;
    }

    IEnumerator AnimateRig(Vector3 fromPos, Vector3 toPos, Quaternion fromRot, Quaternion toRot, float dur, RevealTiming revealPointThisLeg)
    {
        dur = Mathf.Max(0.0001f, dur);
        float t = 0f;
        bool revealedThisLeg = false;

        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / dur);
            float w = Ease(u, easeStrength);

            handRig.localPosition = Vector3.LerpUnclamped(fromPos, toPos, w);
            handRig.localRotation = Quaternion.SlerpUnclamped(fromRot, toRot, w);

            if (reveal == RevealTiming.OnRiseHalf && revealPointThisLeg == RevealTiming.OnRiseHalf && !revealedThisLeg && u >= 0.5f)
            {
                RevealNewItemIfNeeded(RevealTiming.OnRiseHalf, RevealTiming.OnRiseHalf);
                revealedThisLeg = true;
            }

            yield return null;
        }

        handRig.localPosition = toPos;
        handRig.localRotation = toRot;
    }

    static float Ease(float x, float s)
    {
        if (s <= 0f) return x;
        float ss = x * x * (3f - 2f * x); // SmoothStep
        return Mathf.Lerp(x, ss, s);
    }
}
