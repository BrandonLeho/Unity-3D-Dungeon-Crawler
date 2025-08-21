using UnityEngine;

public class Activity : MonoBehaviour
{
    public virtual bool Active { get; protected set; } = false;

    #region Static Methods

    public static bool IsActive(Activity activity)
    {
        return activity != null && activity.Active;
    }

    public static bool TryStart(Activity activity)
    {
        return activity != null && activity.TryStartActivity();
    }

    public static bool TryStop(Activity activity)
    {
        return activity != null && activity.TryStopActivity();
    }

    public static bool TryToggle(Activity activity)
    {
        return activity != null && activity.TryToggleActivity();
    }

    #endregion

    public virtual bool CanStartActivty()
    {
        return true;
    }

    protected virtual void StartActivty()
    {
        Active = true;
    }

    public virtual bool CanStopActivty()
    {
        return true;
    }

    protected virtual void StopActivty()
    {
        Active = false;
    }

    public bool TryStartActivity()
    {
        if (!CanStartActivty())
        {
            return false;
        }

        StartActivty();
        return true;
    }

    public bool TryStopActivity()
    {
        if (!CanStopActivty())
        {
            return false;
        }

        StopActivty();
        return true;
    }

    public bool TryToggleActivity()
    {
        if (Active)
        {
            return TryStopActivity();
        }

        return TryStartActivity();
    }
}
