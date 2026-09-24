namespace Example05.NonHttpPipelines;

public sealed record Order(int Id, string Customer, decimal Total);

/// <summary>Stands in for a database: transient errors, replica lag (null reads) and slow queries on demand.</summary>
public sealed class FakeOrdersRepository
{
    private int _saveFailuresRemaining;
    private int _readMissesRemaining;
    private int _activeQueries;

    public void ArmSaveFailures(int count) => _saveFailuresRemaining = count;

    public void ArmReadMisses(int count) => _readMissesRemaining = count;

    public async Task SaveAsync(Order order, CancellationToken ct)
    {
        await Task.Delay(50, ct);
        if (Interlocked.Decrement(ref _saveFailuresRemaining) >= 0)
        {
            throw new TimeoutException("Simulated deadlock / transient database error");
        }
    }

    public async Task<Order?> GetAsync(int id, CancellationToken ct)
    {
        await Task.Delay(50, ct);
        return Interlocked.Decrement(ref _readMissesRemaining) >= 0
            ? null // replica lag: the row is not visible yet
            : new Order(id, "ACME", 99.90m);
    }

    /// <summary>A slow query; reports how many were running at the same time.</summary>
    public async Task<int> CountAsync(CancellationToken ct)
    {
        var concurrent = Interlocked.Increment(ref _activeQueries);
        try
        {
            await Task.Delay(500, ct);
            return concurrent;
        }
        finally
        {
            Interlocked.Decrement(ref _activeQueries);
        }
    }
}
