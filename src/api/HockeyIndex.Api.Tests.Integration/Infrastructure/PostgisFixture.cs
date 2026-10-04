using HockeyIndex.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;

namespace HockeyIndex.Api.Tests.Integration.Infrastructure;

/// <summary>
/// One PostGIS container per test run, initialised by the same deploy/postgres/init scripts as compose,
/// migrated as hi_migrator. The API under test connects as hi_app, so missing grants fail tests.
/// </summary>
public sealed class PostgisFixture : IAsyncLifetime
{
    public const string Image = "postgis/postgis:17-3.5";
    public const string DatabaseName = "hockeyindex";
    private const string MigratorPassword = "test-migrator";
    private const string AppPassword = "test-app";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder(Image)
        .WithDatabase(DatabaseName)
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithEnvironment("HI_MIGRATOR_PASSWORD", MigratorPassword)
        .WithEnvironment("HI_APP_PASSWORD", AppPassword)
        .WithResourceMapping(new DirectoryInfo(FindInitScriptsDirectory()), "/docker-entrypoint-initdb.d/")
        .Build();

    private Respawner? respawner;

    public string SuperuserConnectionString => container.GetConnectionString();

    public string MigratorConnectionString => WithCredentials("hi_migrator", MigratorPassword);

    public string AppConnectionString => WithCredentials("hi_app", AppPassword);

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync();

        await using (var db = CreateDbContext(MigratorConnectionString))
        {
            await db.Database.MigrateAsync();
        }

        await using var connection = new NpgsqlConnection(SuperuserConnectionString);
        await connection.OpenAsync();
        respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = [new Table("__EFMigrationsHistory"), new Table("spatial_ref_sys")],
        });
    }

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(SuperuserConnectionString);
        await connection.OpenAsync();
        await respawner!.ResetAsync(connection);
    }

    public AppDbContext CreateDbContext(string? connectionString = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseAppDatabase(connectionString ?? AppConnectionString)
            .Options);

    public async ValueTask DisposeAsync() => await container.DisposeAsync();

    private string WithCredentials(string username, string password) =>
        new NpgsqlConnectionStringBuilder(SuperuserConnectionString) { Username = username, Password = password }.ConnectionString;

    private static string FindInitScriptsDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "deploy", "postgres", "init");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("deploy/postgres/init not found above " + AppContext.BaseDirectory);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgisTestGroup : ICollectionFixture<PostgisFixture>
{
    public const string Name = "postgis";
}
