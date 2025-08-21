using System;

public struct DiceRollRequest
{
    public string reason;           // optional: "Melee To-Hit", "Lockpick", etc.
    public Action<int> onComplete;  // callback with final value
}

public static class DiceRollEvents
{
    public static event Action<DiceRollRequest> RollRequested;

    public static void Request(DiceRollRequest req) => RollRequested?.Invoke(req);

    // Convenience overload
    public static void Request(Action<int> onComplete, string reason = null) =>
        RollRequested?.Invoke(new DiceRollRequest { reason = reason, onComplete = onComplete });
}
