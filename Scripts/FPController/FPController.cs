using Unity.Cinemachine;
using Unity.VisualScripting;
using UnityEditor.ShaderGraph.Internal;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TextCore.Text;

[RequireComponent(typeof(CharacterController))]
public class FPController : MonoBehaviour
{
    public FPControllerPreset Preset;

    float MaxSpeed
    {
        get
        {
            if (Activity.IsActive(Crouch))
            {
                return Preset.CrouchSpeed;
            }
            if (Activity.IsActive(Sprint))
            {
                return Preset.SprintSpeed;
            }
            return Preset.WalkSpeed;
        }
    }

    public bool Sprinting
    {
        get
        {
            return Activity.IsActive(Sprint);
        }
    }


    [SerializeField] float currentPitch = 0f;

    public float CurrentPitch
    {
        get => currentPitch;

        set
        {
            currentPitch = Mathf.Clamp(value, -Preset.PitchLimit, Preset.PitchLimit);
        }
    }

    [Header("Camera Parameter")]
    public Vector3 CurrentCameraPosition { get; private set; } = new Vector3(0f, 1.6f, 0f);

    [Space(10)]
    public Vector3 CameraPositionOffset = Vector3.zero;
    public Quaternion CameraRotationOffset = Quaternion.identity;

    [Header("Physics Parameter")]
    [SerializeField] float GravityScale = 3f;

    public float verticalVelocity = 0f;
    public Vector3 CurrentVelocity { get; private set; }
    public float CurrentSpeed { get; private set; }


    private bool wasGrounded = false;
    public bool Grounded => characterController.isGrounded;


    [Header("Input")]
    public Vector2 MoveInput;
    public Vector2 LookInput;
    public bool SprintInput;


    [Header("Components")]
    [SerializeField] CinemachineCamera fpCamera;
    [SerializeField] CharacterController characterController;

    public CharacterController CharacterController => characterController;
    public CinemachineCamera Camera => fpCamera;
    public Transform CameraTransform => fpCamera.transform;


    [Header("Activities")]
    public FPSprint Sprint;
    public FPCrouch Crouch;


    [Header("Events")]
    public UnityEvent Landed;
    public UnityEvent Jumped;
    public UnityEvent DoubleJumped;

    public UnityAction TryJump;
    public UnityAction RequestCameraOffset;
    public UnityAction TryAttack;

    void OnValidate()
    {
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }
    }

    void Update()
    {
        UpdateCameraOffset();

        MoveUpdate();
        LookUpdate();
        CameraUpdate();

        // Updating Camera position
        Vector3 targetCameraPosition = Vector3.up * 1.6f;

        if (Activity.IsActive(Crouch))
        {
            targetCameraPosition = Vector3.up * 0.9f;

            characterController.height = 1f;
            characterController.center = Vector3.up * 0.5f;
        }
        else
        {
            characterController.height = 2f;
            characterController.center = Vector3.up * 1f;
        }

        CurrentCameraPosition = Vector3.Lerp(CurrentCameraPosition, targetCameraPosition, 7f * Time.deltaTime);

        if (!wasGrounded && Grounded)
        {
            Landed?.Invoke();
        }

        wasGrounded = Grounded;
    }

    #region Controller Methods


    void MoveUpdate()
    {
        Vector3 motion = transform.forward * MoveInput.y + transform.right * MoveInput.x;
        motion.y = 0f;
        motion.Normalize();

        if (motion.sqrMagnitude >= 0.01f)
        {
            CurrentVelocity = Vector3.MoveTowards(CurrentVelocity, motion * MaxSpeed, Preset.Acceleration * Time.deltaTime);
        }
        else
        {
            CurrentVelocity = Vector3.MoveTowards(CurrentVelocity, Vector3.zero, Preset.Acceleration * Time.deltaTime);
        }


        if (Grounded && verticalVelocity <= 0.01f)
        {
            verticalVelocity = -3f;
        }
        else
        {
            verticalVelocity += Physics.gravity.y * GravityScale * Time.deltaTime;
        }


        Vector3 fullVelocity = new Vector3(CurrentVelocity.x, verticalVelocity, CurrentVelocity.z);

        CollisionFlags flags = characterController.Move(fullVelocity * Time.deltaTime);

        if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0.01f)
        {
            verticalVelocity = 0f;
        }

        // updating speed
        CurrentSpeed = CurrentVelocity.magnitude;
    }

    void LookUpdate()
    {
        Vector2 input = new Vector2(LookInput.x * Preset.LookSensitivity.x, LookInput.y * Preset.LookSensitivity.y);

        // looking up and down
        CurrentPitch -= input.y;

        fpCamera.transform.localRotation = Quaternion.Euler(CurrentPitch, 0f, 0f) * CameraRotationOffset;

        // looking left and right
        transform.Rotate(Vector3.up * input.x);
    }

    void UpdateCameraOffset()
    {
        CameraPositionOffset = Vector3.zero;
        CameraRotationOffset = Quaternion.identity;

        RequestCameraOffset?.Invoke();
    }

    void CameraUpdate()
    {
        float targetFOV = Preset.CameraNormalFOV;

        if (Sprinting)
        {
            float speedRatio = CurrentSpeed / Preset.SprintSpeed;

            targetFOV = Mathf.Lerp(Preset.CameraNormalFOV, Preset.CameraSprintFOV, speedRatio);
        }
        fpCamera.Lens.FieldOfView = Mathf.Lerp(fpCamera.Lens.FieldOfView, targetFOV, Preset.CameraFOVSmoothing * Time.deltaTime);

        fpCamera.transform.localPosition = CurrentCameraPosition + CameraPositionOffset;
    }

    #endregion
}