using HockeyIndex.Api.Features;
using HockeyIndex.Api.Features.Admin;
using HockeyIndex.Api.Features.Auth;
using HockeyIndex.Api.Features.Discovery;
using HockeyIndex.Api.Features.Events;
using HockeyIndex.Api.Features.Jobs;
using HockeyIndex.Api.Features.Safety;
using HockeyIndex.Api.Features.Venues;
using HockeyIndex.Api.Infrastructure.Auth;
using HockeyIndex.Api.Infrastructure.Caching;
using HockeyIndex.Api.Infrastructure.Http;
using HockeyIndex.Api.Infrastructure.Observability;
using HockeyIndex.Api.Infrastructure.Persistence;
using HockeyIndex.Api.Infrastructure.RateLimiting;
using HockeyIndex.Api.Infrastructure.Time;
using HockeyIndex.Api.Integrations;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseHockeyIndexListeners();
builder.AddObservability();

builder.Services.AddProblemDetails();
builder.Services.AddClock();
builder.Services.Configure<ClientIpOptions>(builder.Configuration.GetSection(ClientIpOptions.SectionName));
builder.Services.Configure<EdgeKeyOptions>(builder.Configuration.GetSection(EdgeKeyOptions.SectionName));
builder.Services.Configure<SpaCorsOptions>(builder.Configuration.GetSection(SpaCorsOptions.SectionName));
builder.Services.AddHockeyIndexRateLimiting(builder.Configuration);
builder.Services.AddHostAuthentication();
builder.Services.AddAuthFeature();
builder.Services.AddPersistence();
builder.Services.AddPlatformServices();
builder.Services.AddIntegrations(builder.Configuration);
builder.Services.AddLinkScanning(builder.Configuration);
builder.Services.AddCachePurging();
builder.Services.AddVenuesFeature();
builder.Services.AddEventsFeature();
builder.Services.AddDiscoveryFeature();
builder.Services.AddSafetyFeature();
builder.Services.AddAdminFeature();
builder.Services.AddJobs();

var app = builder.Build();

StartupGuards.EnsureSafeConfiguration(app.Environment, app.Configuration);

app.UseMiddleware<PublicResponseGuard>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<CacheHeadersMiddleware>();
app.UseMiddleware<PublicCorsMiddleware>();
app.UseMiddleware<ClientIpMiddleware>();
app.UseMiddleware<EdgeKeyMiddleware>();
app.UseSerilogRequestLogging();
app.UseRouting();
app.UseHostAuthentication();
app.UseRateLimiter();

app.MapHealthEndpoints();
app.MapFeatureEndpoints();

await app.RunAsync();

public partial class Program;
