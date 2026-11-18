namespace AltyaziCevirici.Core;

/// <summary>
/// Drops display updates that are older than the last one painted.
/// A new capture loop starts its numbers over at 1, so the gate must be reset with it.
/// Otherwise every later translation is treated as stale and the overlay stays blank.
/// </summary>
public sealed class GosterKapisi
{
    public long Son { get; private set; } = -1;

    public bool Kabul(long seq)
    {
        if (seq <= Son) return false;
        Son = seq;
        return true;
    }

    public void Sifirla() => Son = -1;
}
