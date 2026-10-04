using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HockeyIndex.Api.Infrastructure.Persistence;

/// <summary>
/// Used by dotnet-ef (migrations add, has-pending-model-changes, bundle). Model diffing needs no live database;
/// commands that touch the database read MIGRATOR_CONNECTION or take --connection (the migrator bundle does).
/// </summary>
public sealed class DesignTimeAppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string FallbackConnectionString = "Host=localhost;Port=5432;Database=hockeyindex;Username=hi_migrator;Password=dev-migrator";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("MIGRATOR_CONNECTION") ?? FallbackConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseAppDatabase(connectionString).Options;
        return new AppDbContext(options);
    }
}
