using Unity.Multiplayer.Center.Common;
using UnityEngine;

public class FPSounds : FPControllerModule
{
    [SerializeField] AudioSource AudioSource;

    [SerializeField] AudioClip[] JumpSounds;
    [SerializeField] AudioClip[] DoubleJumpSounds;
    [SerializeField] AudioClip[] FootStepSounds;
    [SerializeField] AudioClip[] LandSounds;



    float footstepTimer = 0f;

    private void Start()
    {
        Controller.Jumped.AddListener(PlayJumpSound);
        Controller.DoubleJumped.AddListener(PlayDoubleJumpSound);
        Controller.Landed.AddListener(OnLanded);
    }

    void Update()
    {
        UpdateFootstepSounds();
    }

    void UpdateFootstepSounds()
    {
        if (Controller.Grounded && Controller.CurrentSpeed >= 0.2f)
        {
            float rate = Preset.FootstepWalkRate;
            float volume = Preset.FootstepWalkVolume;

            float t = Mathf.InverseLerp(Preset.WalkSpeed, Preset.SprintSpeed, Controller.CurrentSpeed);

            rate = Mathf.Lerp(Preset.FootstepWalkRate, Preset.FootstepSprintRate, t);
            volume = Mathf.Lerp(Preset.FootstepWalkVolume, Preset.FootstepSprintVolume, t);

            if (Time.time >= footstepTimer)
            {
                PlaySound(FootStepSounds, volume);
                footstepTimer = Time.time + rate;
            }
        }
    }

    void OnLanded()
    {
        float scale = Mathf.InverseLerp(0f, -20f, Controller.verticalVelocity);
        scale *= Preset.MaxLandSoundVolume;
        PlaySound(LandSounds, scale);
    }



    void PlaySound(AudioClip[] clips, float volumeScale = 1f)
    {
        try
        {
            int index = Random.Range(0, clips.Length);

            AudioClip clip = clips[index];

            AudioSource.PlayOneShot(clip, volumeScale);
        }
        catch { }
    }

    void PlayJumpSound()
    {
        PlaySound(JumpSounds, Preset.JumpSoundVolume);
    }

    void PlayDoubleJumpSound()
    {
        PlaySound(DoubleJumpSounds, Preset.JumpSoundVolume);
    }
}
