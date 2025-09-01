using System;
using System.Collections.Generic;
using UnityEngine;

/// Author attack keyframes per item. Put this under your HandEquipment prefab.
/// Each clip can drive Right arm, Left arm, or Both.
public class AttackPoseTimeline : MonoBehaviour
{
    [Serializable]
    public class ArmKey
    {
        [Min(0f)] public float time = 0f;                // seconds from clip start
        public Vector3 upperEulerDeg;                    // local additive rotation (deg)
        public Vector3 lowerEulerDeg;
        public Vector3 handEulerDeg;
        public Vector3 handLocalOffset;                  // optional tiny local pos offset
        public AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Range(0f, 1f)] public float fingerCurl = -1f;   // -1 = ignore (leave as-is)
        [Range(0f, 1f)] public float fingerSpread = -1f; // -1 = ignore
    }

    public enum UseArms { RightOnly, LeftOnly, Both }

    [Serializable]
    public class AttackClip
    {
        public string name = "Primary";
        public UseArms arms = UseArms.RightOnly;
        [Min(0.05f)] public float duration = 0.50f;
        public bool lockOtherArmAnimations = true; // disable idle/procedural on involved arm(s) while playing
        public bool affectFingers = true;          // write finger inputs during clip

        [Header("Right Arm Keys (if used)")]
        public List<ArmKey> right = new List<ArmKey>();

        [Header("Left Arm Keys (if used)")]
        public List<ArmKey> left = new List<ArmKey>();
    }

    [Tooltip("Clips available for this equipment. First entry is used if PlayActive(name) not found.")]
    public List<AttackClip> clips = new List<AttackClip>()
    {
        new AttackClip{
            name = "Primary",
            arms = UseArms.RightOnly,
            duration = 0.5f,
            right = new List<ArmKey>{
                new ArmKey{ time=0.00f, upperEulerDeg=new Vector3(10,-8,0),  lowerEulerDeg=new Vector3(5,0,0),  handEulerDeg=new Vector3(0,0,0) },
                new ArmKey{ time=0.10f, upperEulerDeg=new Vector3(30,-25,-5),lowerEulerDeg=new Vector3(20,0,0), handEulerDeg=new Vector3(0,8,0) },
                new ArmKey{ time=0.25f, upperEulerDeg=new Vector3(5,10,0),   lowerEulerDeg=new Vector3(10,0,0), handEulerDeg=new Vector3(0,-6,0) },
                new ArmKey{ time=0.50f, upperEulerDeg=Vector3.zero,          lowerEulerDeg=Vector3.zero,        handEulerDeg=Vector3.zero }
            }
        }
    };

    public AttackClip Find(string clipName)
    {
        if (string.IsNullOrEmpty(clipName)) return clips.Count > 0 ? clips[0] : null;
        var c = clips.Find(x => string.Equals(x.name, clipName, StringComparison.OrdinalIgnoreCase));
        return c ?? (clips.Count > 0 ? clips[0] : null);
    }
}
