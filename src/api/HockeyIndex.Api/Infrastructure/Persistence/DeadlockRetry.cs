using Npgsql;

namespace HockeyIndex.Api.Infrastructure.Persistence;

/// <summary>Re-runs a whole transaction when Postgres aborts it with SQLSTATE 40P01 (deadlock detected).</summary>
public static class DeadlockRetry
{
    public const int MaxRetries = 3;

    public static async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        for (var retry = 0; ; retry++)
        {
            try
            {
                return await transaction(cancellationToken);
            }
            catch (Exception exception) when (retry < MaxRetries && IsDeadlock(exception))
            {
            }
        }
    }

    public static bool IsDeadlock(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected })
            {
                return true;
            }
        }

        return false;
    }
}
