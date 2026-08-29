namespace RufusMapEditor.App.Licensing;

/// <summary>LIC.7 — USER build: one Editor instance per machine.</summary>
public static class SingleInstanceGuard
{
    private const string MutexName = @"Global\RufusMapEditor.User.SingleInstance.v1";
    private static Mutex? _mutex;

    public static bool TryAcquire()
    {
#if RUFUS_USER
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out var created);
        return created;
#else
        return true;
#endif
    }

    public static void Release()
    {
#if RUFUS_USER
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch
        {
            // ignore
        }

        _mutex?.Dispose();
        _mutex = null;
#endif
    }
}
