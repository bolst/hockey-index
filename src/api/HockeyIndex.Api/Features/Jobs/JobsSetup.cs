namespace HockeyIndex.Api.Features.Jobs;

public static class JobsSetup
{
    /// <summary>Registers each job as a singleton (tests call <see cref="PeriodicJob.RunOnceAsync"/>) and as a hosted service.</summary>
    public static IServiceCollection AddJobs(this IServiceCollection services)
    {
        services.AddOptions<JobOptions>().BindConfiguration(JobOptions.SectionName);
        AddJob<ArchiveSweepJob>(services);
        AddJob<PurgeJob>(services);
        AddJob<PendingScanJob>(services);
        AddJob<TzdbRederiveJob>(services);
        AddJob<HousekeepingJob>(services);
        AddJob<LinkRescanJob>(services);
        AddJob<DomainBlocklistSweepJob>(services);
        return services;
    }

    private static void AddJob<TJob>(IServiceCollection services)
        where TJob : PeriodicJob
    {
        services.AddSingleton<TJob>();
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<TJob>());
    }
}
