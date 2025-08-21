using Unity.VisualScripting;
using UnityEngine;

public class FPCameraLand : FPActivity
{
    enum EState
    {
        Land,
        Recovery
    }

    EState state = EState.Land;
    float stateTimer = 0f;

    [SerializeField, Min(0f)] float MaxOffset = 0.5f;
    [SerializeField, Min(1f)] float MaxVerticalSpeed = 15f;

    [Header("Land State")]
    [SerializeField, Min(0.01f)] float LandDuration = 0.15f;
    [SerializeField] AnimationCurve LandCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("RecoveryState")]
    [SerializeField] float RecoverySmoothness = 7f;

    private Vector3 targetOffset;
    private Vector3 curreentOffset;

    protected override void Awake()
    {
        base.Awake();

        Controller.Landed.AddListener(OnLanded);
        Controller.RequestCameraOffset += () =>
        {
            Controller.CameraPositionOffset += curreentOffset;
        };
    }

    private void Update()
    {
        stateTimer += Time.deltaTime;

        if (Active)
        {
            if (state == EState.Land)
            {
                float t = LandCurve.Evaluate(stateTimer / LandDuration);
                curreentOffset = Vector3.LerpUnclamped(Vector3.zero, targetOffset, t);
            }
            else if (state == EState.Recovery)
            {
                curreentOffset = Vector3.Lerp(curreentOffset, Vector3.zero, Time.deltaTime * RecoverySmoothness);
            }


            if (state == EState.Land && stateTimer >= LandDuration)
            {
                ChangeState(EState.Recovery);
            }
            else if (state == EState.Recovery && Vector3.Distance(curreentOffset, Vector3.zero) <= 0.05f)
            {
                TryStop(this);
            }
        }
    }

    protected override void StopActivty()
    {
        //Controller.CameraPositionOffset = new Vector3(0f, 0f, 0f);
    }

    void OnLanded()
    {
        float scale = Mathf.InverseLerp(0f, MaxVerticalSpeed, Mathf.Abs(Controller.verticalVelocity));

        targetOffset = new Vector3(0f, -MaxOffset * scale, 0f);

        TryStart(this);

        ChangeState(EState.Land);
    }

    void ChangeState(EState newState)
    {
        state = newState;
        stateTimer = 0f;
    }
}
