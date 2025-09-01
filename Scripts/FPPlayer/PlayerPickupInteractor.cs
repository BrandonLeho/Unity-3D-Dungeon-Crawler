// PlayerPickupInteractor.cs (differences only)
using UnityEngine;

public class PlayerPickupInteractor : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] FPController fp;
    [SerializeField] TempParent tempParent;

    [Header("Attributes")]
    [SerializeField] MonoBehaviour attributeProvider; // assign your AttributeSet here
    IAttributeProvider attr; // resolved from attributeProvider

    [Header("Pickup Settings")]
    [SerializeField] float maxDistance = 3f;
    [SerializeField] LayerMask interactMask = ~0;
    [SerializeField] float throwImpulsePerStrength = 0.9f; // tweak
    [SerializeField] float baseThrowImpulse = 6f;          // tweak

    Pickup current;
    bool rightHeld;

    void Awake()
    {
        if (!fp) fp = GetComponent<FPController>();
        attr = attributeProvider as IAttributeProvider;     // AttributeSet implements this
    }

    void Start()
    {
        if (!tempParent) tempParent = TempParent.Instance;
    }

    public bool IsHolding => current != null;
    public bool BlockAttack => rightHeld && current != null;

    public void OnPickupPressed()
    {
        rightHeld = true;
        if (current) return;

        var cam = fp ? fp.CameraTransform : Camera.main.transform;
        if (!cam) return;

        if (Physics.Raycast(cam.position, cam.forward, out var hit, maxDistance, interactMask, QueryTriggerInteraction.Ignore))
        {
            var pick = hit.collider.GetComponentInParent<Pickup>() ?? hit.collider.GetComponent<Pickup>();
            if (pick && tempParent)
            {
                // START force-based hold (no teleport)
                if (pick.BeginForceHold(tempParent.transform, attr, maxDistance))
                    current = pick;
            }
        }
    }

    public void OnPickupReleased()
    {
        rightHeld = false;
        if (current)
        {
            current.ForceDrop();
            current = null;
        }
    }

    // Called at the start of OnAttack
    public bool TryConsumeAttackAsThrow()
    {
        if (!BlockAttack) return false;

        var cam = fp ? fp.CameraTransform : Camera.main.transform;
        if (cam && current)
        {
            float strength = attr != null ? attr.Get(AttributeType.Strength) : 10f; // sensible default
            float impulse = baseThrowImpulse + strength * throwImpulsePerStrength;
            current.ForceThrow(cam.forward, impulse);
            current = null;
            rightHeld = false;       // re-enable attacking
            return true;
        }
        return false;
    }
}
