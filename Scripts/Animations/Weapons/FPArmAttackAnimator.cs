using System;
using System.Collections.Generic;
using UnityEngine;

/// First-person, keyframe-style attack animator.
/// - Plays per-item AttackPoseTimeline for the ACTIVE item from EquipmentController.
/// - Adds rotations on top of whatever your Pose/Grip systems set at play time.
/// - Temporarily disables idle/procedural animation on involved arm(s) to avoid conflicts.
/// - Does NOT use IK for movement. Runs after other animators so its pose wins.
[DefaultExecutionOrder(+120)]
public class FPArmAttackAnimator : MonoBehaviour
{
    [Header("Rig References")]
    public EquipmentController equipmentController;              // to find active item & its timeline
    public ProceduralIdleAnimator idleAnimator;                  // to toggle arm idle anim on/off
    public ProceduralFingerAnimator fingerAnimator;              // optional: adjust grip during swing

    [Header("Right Arm Bones")]
    public Transform upperArmR;
    public Transform lowerArmR;
    public Transform handR;

    [Header("Left Arm Bones")]
    public Transform upperArmL;
    public Transform lowerArmL;
    public Transform handL;

    [Header("Defaults")]
    [Range(1f, 40f)] public float blendLerp = 24f;               // how quickly we blend ON/OFF our offsets
    public bool autoRestoreArms = true;

    [Header("Smoothing")]
    public bool smoothDuringAttack = false; // default off so easing is fully visible



    // re-enable idle arms when a clip ends
    [Header("Gameplay Locks")]
    public LockCategory lockCategories = LockCategory.EquipmentSwitch | LockCategory.AnimationStart;
    public string lockTag = "Attack";
    public int lockPriority = 10;

    LockHandle _lockHandle;   // acquired while playing

    // -------- runtime state --------
    AttackPoseTimeline _activeTimeline;
    AttackPoseTimeline.AttackClip _playing;
    float _t;                                                    // elapsed time in current clip
    bool _wasIdleL, _wasIdleR;                                   // remember idle toggles to restore
    bool _hadFinger;                                             // whether we touched fingers

    // baselines captured at attack start (so per-weapon poses work)
    Quaternion _uR0, _lR0, _hR0, _uL0, _lL0, _hL0;
    Vector3 _handRPos0Local, _handLPos0Local;
    float _leftCurl0, _leftSpread0, _rightCurl0, _rightSpread0;

    void Reset()
    {
        if (!equipmentController) equipmentController = GetComponentInParent<EquipmentController>();
        if (!idleAnimator) idleAnimator = GetComponent<ProceduralIdleAnimator>();
        if (!fingerAnimator) fingerAnimator = GetComponent<ProceduralFingerAnimator>();
    }

    void OnEnable()
    {
        if (equipmentController)
        {
            equipmentController.OnItemEquipped += OnEquipped;
            equipmentController.OnItemUnequipped += OnUnequipped;
        }
        ResolveActiveTimeline();
    }

    void OnDisable()
    {
        if (equipmentController)
        {
            equipmentController.OnItemEquipped -= OnEquipped;
            equipmentController.OnItemUnequipped -= OnUnequipped;
        }
    }

    void OnEquipped(int slot, HandEquipment item) => ResolveActiveTimeline();
    void OnUnequipped(int slot, HandEquipment item) { if (item && _activeTimeline && item.transform.IsChildOf(_activeTimeline.transform)) _activeTimeline = null; }

    void ResolveActiveTimeline()
    {
        var item = equipmentController ? equipmentController.ActiveItem : null;
        var comp = item as Component;
        _activeTimeline = comp ? comp.GetComponentInChildren<AttackPoseTimeline>(true) : null;
    }

    // ---- Public API -------------------------------------------------------

    /// Play the named clip on the ACTIVE item (fall back to first clip).
    public void PlayActive(string clipName = "Primary")
    {
        // block starting a new animation if locked elsewhere
        if (GameplayLock.Any(LockCategory.AnimationStart)) return;

        if (!_activeTimeline) ResolveActiveTimeline();
        if (!_activeTimeline) return;

        var clip = _activeTimeline.Find(clipName);
        if (clip != null) PlayClip(clip);
    }

    /// Play a specific clip (advanced).
    public void PlayClip(AttackPoseTimeline.AttackClip clip)
    {
        if (clip == null) return;

        _lockHandle = GameplayLock.GetOrCreate().Acquire(lockCategories, this, lockTag, lockPriority);
        _playing = clip;
        _t = 0f;
        _hadFinger = false;

        // Capture baselines at the moment of attack so current item pose is respected
        if (upperArmR) _uR0 = upperArmR.localRotation;
        if (lowerArmR) _lR0 = lowerArmR.localRotation;
        if (handR) { _hR0 = handR.localRotation; _handRPos0Local = handR.localPosition; }
        if (upperArmL) _uL0 = upperArmL.localRotation;
        if (lowerArmL) _lL0 = lowerArmL.localRotation;
        if (handL) { _hL0 = handL.localRotation; _handLPos0Local = handL.localPosition; }

        if (fingerAnimator)
        {
            _leftCurl0 = fingerAnimator.leftCurl;
            _leftSpread0 = fingerAnimator.leftSpread;
            _rightCurl0 = fingerAnimator.rightCurl;
            _rightSpread0 = fingerAnimator.rightSpread;
        }

        // Lock out idle/procedural on involved arm(s) while playing
        if (idleAnimator && clip.lockOtherArmAnimations)
        {
            _wasIdleL = idleAnimator.enableLeftArm;
            _wasIdleR = idleAnimator.enableRightArm;

            switch (clip.arms)
            {
                case AttackPoseTimeline.UseArms.RightOnly:
                    idleAnimator.enableRightArm = false;
                    break;
                case AttackPoseTimeline.UseArms.LeftOnly:
                    idleAnimator.enableLeftArm = false;
                    break;
                case AttackPoseTimeline.UseArms.Both:
                    idleAnimator.enableLeftArm = false;
                    idleAnimator.enableRightArm = false;
                    break;
            }
        }
    }

    // ---- Runtime ----------------------------------------------------------

    void LateUpdate()
    {
        if (_playing == null) return;

        _t += Time.deltaTime;
        float norm = Mathf.Clamp01(_t / Mathf.Max(0.0001f, _playing.duration));

        // apply right/left tracks
        if (_playing.arms != AttackPoseTimeline.UseArms.LeftOnly)
            ApplyTrack(_playing.right, upperArmR, lowerArmR, handR, _uR0, _lR0, _hR0, _handRPos0Local, right: true, norm);

        if (_playing.arms != AttackPoseTimeline.UseArms.RightOnly)
            ApplyTrack(_playing.left, upperArmL, lowerArmL, handL, _uL0, _lL0, _hL0, _handLPos0Local, right: false, norm);

        if (_t >= _playing.duration)
            EndClip(); // centralize release & restore
    }

    // Add this field near the other runtime state:
    readonly List<AttackPoseTimeline.ArmKey> _scratch = new List<AttackPoseTimeline.ArmKey>(8);

    // Replace your existing ApplyTrack with this:
    void ApplyTrack(
        List<AttackPoseTimeline.ArmKey> keys,
        Transform uArm, Transform lArm, Transform hand,
        Quaternion u0, Quaternion l0, Quaternion h0,
        Vector3 handPos0Local,
        bool right, float norm)
    {
        if (keys == null || keys.Count == 0 || uArm == null || lArm == null || hand == null) return;

        float clipDur = Mathf.Max(0.0001f, _playing.duration);
        float tSec = Mathf.Clamp01(norm) * clipDur;

        // Build a safe, sorted working list without mutating the original
        _scratch.Clear();
        _scratch.AddRange(keys);
        _scratch.Sort((a, b) => a.time.CompareTo(b.time));

        // Implicit "pre" zero-offset key at t=0 if first authored key is later than 0
        if (_scratch[0].time > 0f)
        {
            _scratch.Insert(0, new AttackPoseTimeline.ArmKey
            {
                time = 0f,
                upperEulerDeg = Vector3.zero,
                lowerEulerDeg = Vector3.zero,
                handEulerDeg = Vector3.zero,
                handLocalOffset = Vector3.zero,
                fingerCurl = -1f,     // -1 = ignore
                fingerSpread = -1f,
                ease = AnimationCurve.Linear(0, 0, 1, 1)
            });
        }

        // Implicit "post" return-to-idle at t=clip duration if last authored key is earlier
        if (_scratch[_scratch.Count - 1].time < clipDur)
        {
            _scratch.Add(new AttackPoseTimeline.ArmKey
            {
                time = clipDur,
                upperEulerDeg = Vector3.zero,
                lowerEulerDeg = Vector3.zero,
                handEulerDeg = Vector3.zero,
                handLocalOffset = Vector3.zero,
                fingerCurl = -1f,
                fingerSpread = -1f,
                ease = AnimationCurve.Linear(0, 0, 1, 1)
            });
        }

        // Find the active segment [a, b] for current time (handles pre/post ranges too)
        int aIdx = 0;
        for (int i = 0; i < _scratch.Count - 1; i++)
        {
            if (tSec >= _scratch[i].time && tSec <= _scratch[i + 1].time)
            {
                aIdx = i;
                break;
            }
            if (tSec > _scratch[i].time) aIdx = i; // keep last valid
        }

        var a = _scratch[aIdx];
        var b = _scratch[Mathf.Min(aIdx + 1, _scratch.Count - 1)];

        // Normalize within the segment and apply easing from the START key (a)
        float rawU = (b.time > a.time) ? Mathf.InverseLerp(a.time, b.time, tSec) : 1f;
        var segEase = (a.ease != null && a.ease.keys != null && a.ease.keys.Length > 0)
            ? a.ease
            : AnimationCurve.Linear(0, 0, 1, 1);
        float u = Mathf.Clamp01(segEase.Evaluate(rawU));

        // Interpolate additive rotations/offsets
        Quaternion uAdd = Quaternion.Slerp(Quaternion.Euler(a.upperEulerDeg), Quaternion.Euler(b.upperEulerDeg), u);
        Quaternion lAdd = Quaternion.Slerp(Quaternion.Euler(a.lowerEulerDeg), Quaternion.Euler(b.lowerEulerDeg), u);
        Quaternion hAdd = Quaternion.Slerp(Quaternion.Euler(a.handEulerDeg), Quaternion.Euler(b.handEulerDeg), u);

        // Optional frame smoothing (set smoothDuringAttack = true if you want damping)
        float alpha = smoothDuringAttack ? (1f - Mathf.Exp(-blendLerp * Time.deltaTime)) : 1f;

        uArm.localRotation = Quaternion.Slerp(uArm.localRotation, u0 * uAdd, alpha);
        lArm.localRotation = Quaternion.Slerp(lArm.localRotation, l0 * lAdd, alpha);
        hand.localRotation = Quaternion.Slerp(hand.localRotation, h0 * hAdd, alpha);

        // Tiny local hand positional offsets (authoring convenience)
        Vector3 ofs = Vector3.Lerp(a.handLocalOffset, b.handLocalOffset, u);
        hand.localPosition = Vector3.Lerp(hand.localPosition, handPos0Local + ofs, alpha);

        // Optional finger shaping
        if (_playing.affectFingers && fingerAnimator)
        {
            float curlA = a.fingerCurl, curlB = b.fingerCurl;
            float sprA = a.fingerSpread, sprB = b.fingerSpread;
            bool changeCurl = curlA >= 0f || curlB >= 0f;
            bool changeSpread = sprA >= 0f || sprB >= 0f;
            if (changeCurl || changeSpread) _hadFinger = true;

            if (changeCurl)
            {
                float baseCurl = right ? _rightCurl0 : _leftCurl0;
                float curl = Mathf.Lerp(curlA < 0 ? baseCurl : curlA,
                                        curlB < 0 ? baseCurl : curlB, u);
                if (right) fingerAnimator.rightCurl = curl; else fingerAnimator.leftCurl = curl;
            }
            if (changeSpread)
            {
                float baseSpr = right ? _rightSpread0 : _leftSpread0;
                float spr = Mathf.Lerp(sprA < 0 ? baseSpr : sprA,
                                    sprB < 0 ? baseSpr : sprB, u);
                if (right) fingerAnimator.rightSpread = spr; else fingerAnimator.leftSpread = spr;
            }
        }
    }

    // centralized end/cleanup (also used by CancelActiveClip) ======
    void EndClip()
    {
        // restore idle toggles, hand positions, finger values (same as your previous end)
        if (autoRestoreArms && idleAnimator)
        {
            idleAnimator.enableLeftArm = _wasIdleL;
            idleAnimator.enableRightArm = _wasIdleR;
        }
        if (handR) handR.localPosition = _handRPos0Local;
        if (handL) handL.localPosition = _handLPos0Local;

        if (_hadFinger && fingerAnimator)
        {
            fingerAnimator.leftCurl = _leftCurl0;
            fingerAnimator.leftSpread = _leftSpread0;
            fingerAnimator.rightCurl = _rightCurl0;
            fingerAnimator.rightSpread = _rightSpread0;
        }

        _playing = null;

        // Release gameplay locks
        _lockHandle.Release();
    }

    // Interrupt this animation (e.g., heavy stagger). Uses same cleanup path.
    public void CancelActiveClip() { if (_playing != null) EndClip(); }
}
