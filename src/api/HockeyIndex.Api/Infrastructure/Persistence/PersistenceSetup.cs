using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HockeyIndex.Api.Infrastructure.Persistence;

public static class PersistenceSetup
{
    public const string ConnectionStringName = "Default";
    public const string DataProtectionApplicationName = "HockeyIndex";

    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var connectionString = serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} is not configured.");
            }

            options.UseAppDatabase(connectionString).AddInterceptors(serviceProvider.GetServices<IInterceptor>());
        });

        services.AddDataProtection()
            .SetApplicationName(DataProtectionApplicationName)
            .PersistKeysToDbContext<AppDbContext>();

        return services;
    }

    public static TBuilder UseAppDatabase<TBuilder>(this TBuilder options, string connectionString)
        where TBuilder : DbContextOptionsBuilder =>
        (TBuilder)options
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention();
}
