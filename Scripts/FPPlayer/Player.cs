using UnityEngine;
using UnityEngine.InputSystem;


[RequireComponent(typeof(FPController))]
public class Player : MonoBehaviour
{
    [Header("Components")]
    [SerializeField] FPController FPController;
    [SerializeField] EquipmentController equipment;

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
            FPController.TryAttack?.Invoke();
        }

    }

    void OnScrollWheel(InputValue value)
    {
        if (!equipment) return;
        Vector2 scroll = value.Get<Vector2>();
        equipment.Scroll(scroll.y);  // +y next, -y prev
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
