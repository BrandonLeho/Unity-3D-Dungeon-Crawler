using UnityEngine;
using UnityEngine.InputSystem;


[RequireComponent(typeof(FPController))]
public class Player : MonoBehaviour
{
    [Header("Components")]
    [SerializeField] FPController FPController;
    [SerializeField] EquipmentController equipment;
    [SerializeField] PlayerPickupInteractor pickupInteractor;

    #region Input Handling

    void OnMove(InputValue value)
    {
        FPController.MoveInput = value.Get<Vector2>();
    }

    void OnLook(InputValue value)
    {
        FPController.LookInput = value.Get<Vector2>();
    }

    void OnSprint(InputValue value)
    {
        FPController.SprintInput = value.isPressed;
    }

    void OnJump(InputValue value)
    {
        if (value.isPressed)
        {
            FPController.TryJump?.Invoke();
        }
    }

    void OnCrouch(InputValue value)
    {
        if (value.isPressed)
        {
            Activity.TryToggle(FPController.Crouch);
        }
    }

    void OnAttack(InputValue value)
    {
        if (value.isPressed)
        {
            // If holding an object via right-click, left-click should THROW and consume this click.
            if (pickupInteractor != null)
            {
                if (pickupInteractor.TryConsumeAttackAsThrow())
                    return; // consumed by throw; attacking is re-enabled immediately for next click
                if (pickupInteractor.BlockAttack)
                    return; // still blocking attacks while holding
            }
            FPController.TryAttack?.Invoke();
        }
    }

    void OnScrollWheel(InputValue value)
    {
        if (!equipment) return;
        Vector2 scroll = value.Get<Vector2>();
        equipment.Scroll(scroll.y);  // +y next, -y prev
    }

    void OnPickup(InputValue value)
    {
        if (value.isPressed)
            pickupInteractor.OnPickupPressed();   // start holding (or try to pick)
        else
            pickupInteractor.OnPickupReleased();  // drop on release
    }

    #endregion

    #region Unity Methods

    void OnValidate()
    {
        if (FPController == null) FPController = GetComponent<FPController>();
        if (equipment == null) equipment = GetComponentInChildren<EquipmentController>();
    }

    void Start()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    #endregion
}
