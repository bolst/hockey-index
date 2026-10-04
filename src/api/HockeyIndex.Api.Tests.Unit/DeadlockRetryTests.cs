using HockeyIndex.Api.Infrastructure.Persistence;
using Npgsql;

namespace HockeyIndex.Api.Tests.Unit;

public sealed class DeadlockRetryTests
{
    private static PostgresException Deadlock() =>
        new("deadlock detected", "ERROR", "ERROR", PostgresErrorCodes.DeadlockDetected);

    [Fact]
    public async Task Deadlocked_transaction_is_retried_until_it_succeeds()
    {
        var attempts = 0;

        var result = await DeadlockRetry.RunAsync(_ =>
            ++attempts == 1 ? throw Deadlock() : Task.FromResult(attempts), TestContext.Current.CancellationToken);

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task Gives_up_after_max_retries()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<PostgresException>(() => DeadlockRetry.RunAsync<int>(_ =>
        {
            attempts++;
            throw Deadlock();
        }, TestContext.Current.CancellationToken));

        Assert.Equal(DeadlockRetry.MaxRetries + 1, attempts);
    }

    [Fact]
    public async Task Other_failures_are_not_retried()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<PostgresException>(() => DeadlockRetry.RunAsync<int>(_ =>
        {
            attempts++;
            throw new PostgresException("unique", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);
        }, TestContext.Current.CancellationToken));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public void Detects_a_deadlock_wrapped_in_another_exception()
    {
        Assert.True(DeadlockRetry.IsDeadlock(new InvalidOperationException("save failed", Deadlock())));
        Assert.False(DeadlockRetry.IsDeadlock(new InvalidOperationException("other")));
        Assert.False(DeadlockRetry.IsDeadlock(null));
    }
}
