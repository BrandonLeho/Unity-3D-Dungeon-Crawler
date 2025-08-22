using System;
using UnityEngine;

namespace AIToolkit
{
    public struct Noise
    {
        public Vector3 Position;
        public float Loudness;      // 0..1
        public Transform Source;
        public float Time;
        public Noise(Vector3 pos, float loudness, Transform src = null)
        { Position = pos; Loudness = Mathf.Clamp01(loudness); Source = src; Time = UnityEngine.Time.time; }
    }

    public static class NoiseEvents
    {
        public static event Action<Noise> Emitted;

        /// Call this from weapons, footsteps, doors, etc.
        public static void Emit(Vector3 position, float loudness, Transform source = null)
            => Emitted?.Invoke(new Noise(position, loudness, source));
    }
}
