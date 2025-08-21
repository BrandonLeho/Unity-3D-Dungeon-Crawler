using UnityEngine;

[System.Serializable]
public class HeadBobPreset
{
    public float Amplitude = 0.5f;
    public float Frequency = 5f;

    [Space(15)]
    public float NoiseScale = 1f;
    public float MaxRotation = 30f;
}

public class FPHeadBob : FPControllerModule
{
    float Frequency = 5f;
    float Amplitude = 0.5f;

    float NoiseScale = 1f;
    float MaxRotation = 30f;

    [SerializeField] bool ApplyPositionOffset = true;
    [SerializeField] bool ApplyRotationOffset = true;
    [SerializeField] bool ApplyRotationNoiseOffset = true;


    Vector3 PositionOffset;
    Quaternion RotationOffset = Quaternion.identity;

    // angle of the sine waves, incremented in the update method
    float angle = 0f;

    float SpeedRatio = 0f;

    public void SetValues(HeadBobPreset preset)
    {
        Amplitude = preset.Amplitude;
        Frequency = preset.Frequency;
        NoiseScale = preset.NoiseScale;
        MaxRotation = preset.MaxRotation;
    }

    protected override void Awake()
    {
        base.Awake();

        Controller.RequestCameraOffset += () =>
        {
            Controller.CameraPositionOffset += PositionOffset;
            Controller.CameraRotationOffset *= RotationOffset;
        };
    }

    private void Update()
    {
        if (Controller.Grounded)
        {
            UpdateValues();
            UpdateHeadBobbing();
        }
        else
        {
            PositionOffset = Vector3.Lerp(PositionOffset, Vector3.zero, 10f * Time.deltaTime);
            RotationOffset = Quaternion.Lerp(RotationOffset, Quaternion.identity, 10f * Time.deltaTime);
        }
    }

    void UpdateValues()
    {
        if (Activity.IsActive(Controller.Sprint))
        {
            SetValues(Preset.HeadBobSprint);
            SpeedRatio = Mathf.InverseLerp(Preset.WalkSpeed, Preset.SprintSpeed, Controller.CurrentSpeed);
        }
        else if (Activity.IsActive(Controller.Crouch))
        {
            SetValues(Preset.HeadBobCrouch);
            SpeedRatio = Mathf.InverseLerp(0f, Preset.CrouchSpeed, Controller.CurrentSpeed);
        }
        else
        {
            SetValues(Preset.HeadBobWalk);
            SpeedRatio = Mathf.InverseLerp(0f, Preset.WalkSpeed, Controller.CurrentSpeed);
        }
    }

    void UpdateHeadBobbing()
    {
        angle += Time.deltaTime * Frequency * SpeedRatio;

        float bobX = Mathf.Sin(angle) * Amplitude * SpeedRatio;
        //float bobY = Mathf.Cos(angle) * Mathf.Sin(angle) * Amplitude * SpeedRatio; // Use if you want head bobbing in figure infinity motion
        float bobY = Mathf.Cos(angle * 2f) * Amplitude * SpeedRatio; // Use if you want head bobbing in a bowditch curve motion

        if (ApplyPositionOffset)
        {
            Vector3 targetOffset = new Vector3(bobX, bobY, 0f);
            PositionOffset = Vector3.Lerp(PositionOffset, targetOffset, 10f * Time.deltaTime);
        }

        float bobAngle = MaxRotation * Mathf.Sin(angle);
        float noise = Mathf.PerlinNoise1D(angle * NoiseScale);

        if (ApplyRotationOffset == false)
        {
            bobAngle = 0f;
        }

        if (ApplyRotationNoiseOffset == false)
        {
            noise = 0f;
        }

        if (ApplyRotationNoiseOffset)
        {
            bobAngle *= noise;
        }

        Quaternion stabilizationRot = Quaternion.LookRotation(Vector3.forward * 30f - PositionOffset);

        Quaternion TargetRotation = stabilizationRot * Quaternion.Euler(0f, 0f, bobAngle * SpeedRatio);

        RotationOffset = Quaternion.Lerp(RotationOffset, TargetRotation, 10f * Time.deltaTime);
    }
}
