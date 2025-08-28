using UnityEngine;

/// Anchor that follows a target transform with configurable space, timing, and smoothing.
public class AnchorToTransformPro : MonoBehaviour
{
    public enum SpaceMode { World, Local }
    public enum UpdateMode { Update, LateUpdate, FixedUpdate }
    public enum FollowMode { Transform, RigidbodyMove }

    [Header("Target")]
    public Transform target;

    [Header("Follow What")]
    public bool followPosition = true;
    public bool followRotation = true;
    public bool followScale = false;

    [Header("Offsets")]
    public SpaceMode spaceMode = SpaceMode.Local;
    public Vector3 positionOffset = Vector3.zero;
    public Vector3 rotationOffsetEuler = Vector3.zero;

    [Header("Timing")]
    public UpdateMode updateMode = UpdateMode.LateUpdate;

    [Header("Physics-Aware Follow")]
    public FollowMode followMode = FollowMode.Transform;
    public bool findTargetRigidbody = true;

    [Header("Smoothing (optional)")]
    public bool smooth = false;
    [Range(0.001f, 1f)] public float positionSmoothTime = 0.05f;
    [Range(1f, 40f)] public float rotationLerp = 20f;

    Rigidbody _selfRb;
    Rigidbody _targetRb;
    Vector3 _vel;
    Quaternion _rotOffsetQ;

    void Awake()
    {
        _selfRb = GetComponent<Rigidbody>();
        if (findTargetRigidbody && target)
            _targetRb = target.GetComponent<Rigidbody>();

        _rotOffsetQ = Quaternion.Euler(rotationOffsetEuler);
    }

    void OnValidate()
    {
        _rotOffsetQ = Quaternion.Euler(rotationOffsetEuler);
    }

    void Update()
    {
        if (updateMode == UpdateMode.Update)
            ApplyFollow(Time.deltaTime);
    }

    void LateUpdate()
    {
        if (updateMode == UpdateMode.LateUpdate)
            ApplyFollow(Time.deltaTime);
    }

    void FixedUpdate()
    {
        if (updateMode == UpdateMode.FixedUpdate)
            ApplyFollow(Time.fixedDeltaTime);
    }

    void ApplyFollow(float dt)
    {
        if (!target) return;

        Vector3 targetPos;
        Quaternion targetRot;

        if (spaceMode == SpaceMode.Local)
        {
            targetRot = target.rotation * _rotOffsetQ;
            targetPos = target.TransformPoint(positionOffset);
        }
        else
        {
            targetRot = target.rotation * _rotOffsetQ;
            targetPos = target.position + positionOffset;
        }

        if (followPosition)
        {
            if (smooth && updateMode != UpdateMode.FixedUpdate)
            {
                Vector3 newPos = Vector3.SmoothDamp(transform.position, targetPos, ref _vel, positionSmoothTime);
                WritePosition(newPos);
            }
            else
            {
                WritePosition(targetPos);
            }
        }

        if (followRotation)
        {
            if (smooth && updateMode != UpdateMode.FixedUpdate)
            {
                Quaternion newRot = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-rotationLerp * dt));
                WriteRotation(newRot);
            }
            else
            {
                WriteRotation(targetRot);
            }
        }

        if (followScale)
        {
            transform.localScale = target.lossyScale;
        }
    }

    void WritePosition(Vector3 p)
    {
        if (followMode == FollowMode.RigidbodyMove && _selfRb && _selfRb.interpolation != RigidbodyInterpolation.None)
            _selfRb.MovePosition(p);
        else
            transform.position = p;
    }

    void WriteRotation(Quaternion q)
    {
        if (followMode == FollowMode.RigidbodyMove && _selfRb && _selfRb.interpolation != RigidbodyInterpolation.None)
            _selfRb.MoveRotation(q);
        else
            transform.rotation = q;
    }

    [ContextMenu("Configure: Follow Rigidbody Target")]
    void ConfigureForRigidbodyTarget()
    {
        updateMode = UpdateMode.FixedUpdate;
        followMode = FollowMode.RigidbodyMove;
        smooth = false;
    }

    [ContextMenu("Configure: Follow Non-Physics Target (Camera/Animator)")]
    void ConfigureForAnimatorOrCamera()
    {
        updateMode = UpdateMode.LateUpdate;
        followMode = FollowMode.Transform;
        smooth = true;
        positionSmoothTime = 0.03f;
        rotationLerp = 24f;
    }
}
