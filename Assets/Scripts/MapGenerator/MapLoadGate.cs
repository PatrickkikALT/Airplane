using System;

public static class MapLoadGate
{
    public static bool IsHeld { get; private set; }
    public static bool LocalReady { get; private set; } = true;
    public static event Action Ready;

    public static void Hold()
    {
        IsHeld = true;
        LocalReady = false;
    }

    public static void Release()
    {
        if (LocalReady)
            return;

        LocalReady = true;
        Ready?.Invoke();
    }
}
