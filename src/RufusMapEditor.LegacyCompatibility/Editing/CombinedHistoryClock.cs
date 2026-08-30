namespace RufusMapEditor.LegacyCompatibility.Editing;

/// <summary>Reloj monotónico compartido para ordenar deshacer entre layout de mundo y edición de celdas.</summary>
public static class CombinedHistoryClock
{
    private static long _seq;

    public static long Next() => System.Threading.Interlocked.Increment(ref _seq);
}
