using UnityEngine;

public class FPJump : FPControllerModule
{
    [SerializeField] bool hasDoubleJumped = false;
    [SerializeField] float coyoteTimer = 0f;
    private void Start()
    {
        Controller.TryJump += OnTryJump;

        Controller.Landed.AddListener(OnLanded);
    }

    private void Update()
    {
        if (Controller.Grounded)
        {
            coyoteTimer = 0f;
        }
        else
        {
            coyoteTimer += Time.deltaTime;
        }
    }

    void OnLanded()
    {
        hasDoubleJumped = false;
    }


    void OnTryJump()
    {
        // if the player is crouching and they jump, uncrouch the player
        if (Activity.IsActive(Controller.Crouch))
        {
            Activity.TryStop(Controller.Crouch);
        }

        // jumping
        if (coyoteTimer <= Preset.CoyoteTime)
        {
            Jump();
            Controller.Jumped?.Invoke();
            return;
        }

        // double jumping
        if (Preset.CanDoubleJump == false)
        {
            return;
        }



        if (!hasDoubleJumped)
        {
            Jump();
            hasDoubleJumped = true;
            Controller.DoubleJumped?.Invoke();
        }
    }

    void Jump()
    {
        Controller.verticalVelocity = Mathf.Sqrt(Preset.JumpHeight * -2f * Physics.gravity.y * Preset.GravityScale);
    }
}
