using UnityEngine;

namespace AIToolkit
{
    public enum SignalType { Vision, Hearing }

    /// A unified perception signal with strength and time.
    public struct AISignal
    {
        public SignalType Type;
        public Transform Source;     // who produced it (player etc.) - may be null for anonymous noises
        public Vector3 Position;     // where it came from
        public Vector3 Velocity;     // optional (vision fills this), Vector3.zero if unknown
        public float Strength;       // 0..1 normalized (brain maps to suspicion)
        public float Time;           // Time.time when produced

        public AISignal(SignalType type, Transform src, Vector3 pos, Vector3 vel, float strength, float time)
        {
            Type = type; Source = src; Position = pos; Velocity = vel; Strength = Mathf.Clamp01(strength); Time = time;
        }
    }
}
