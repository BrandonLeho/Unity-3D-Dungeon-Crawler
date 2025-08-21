using UnityEngine;

public class FPSprint : FPActivity
{
    protected override void Awake()
    {
        base.Awake();

        Controller.Sprint = this;
    }

    public override bool CanStartActivty()
    {
        if (Activity.IsActive(Controller.Crouch))
        {
            Activity.TryStop(Controller.Crouch);
        }
        return true;
    }

    private void Update()
    {
        if (Controller.SprintInput && Controller.CurrentSpeed > 0.1f)
        {
            TryStartActivity();
        }
        if (Controller.SprintInput == false || Controller.CurrentSpeed <= 0.1f)
        {
            TryStopActivity();
        }
    }
}
