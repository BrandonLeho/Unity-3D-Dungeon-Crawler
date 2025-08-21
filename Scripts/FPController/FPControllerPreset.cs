using UnityEditor;
using UnityEngine;

[CreateAssetMenu(menuName = "ArtemisFPS/FPSControllerPreset")]
public class FPControllerPreset : ScriptableObject
{
    [Header("Movement Parameters")]
    public float Acceleration = 100f;

    [Space(15)]
    public float CrouchSpeed = 2f;
    public float WalkSpeed = 3.5f;
    public float SprintSpeed = 10f;

    [Header("Jumping Parameters")]
    [Tooltip("This is how high the character can jump")]
    public float JumpHeight = 2f;
    public bool CanDoubleJump = true;
    public float CoyoteTime = 0.15f;

    [Header("Looking Parameters")]
    public Vector2 LookSensitivity = new Vector2(0.1f, 0.1f);
    public float PitchLimit = 85f;

    [Header("Camera Parameters")]
    public float CameraNormalFOV = 60f;
    public float CameraSprintFOV = 67.5f;
    public float CameraFOVSmoothing = 5f;

    [Header("Head Bobbing")]
    public HeadBobPreset HeadBobWalk;
    public HeadBobPreset HeadBobCrouch;
    public HeadBobPreset HeadBobSprint;

    [Header("Physics Parameter")]
    public float GravityScale = 2f;
    public LayerMask ObstacleLayerMask = Physics.DefaultRaycastLayers;

    [Header("Sounds & Footstep Parameters")]
    public float FootstepWalkRate = 0.55f;
    public float FootstepSprintRate = 0.35f;

    [Space(15)]
    public float FootstepWalkVolume = 0.2f;
    public float FootstepSprintVolume = 0.4f;

    [Space(15)]
    public float MaxLandSoundVolume = 0.7f;
    public float JumpSoundVolume = 0.5f;


}
