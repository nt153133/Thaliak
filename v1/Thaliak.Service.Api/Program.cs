using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Thaliak.Common.Database;
using Thaliak.Service.Api.Artifacts;
using Thaliak.Service.Api.Endpoints;
using Thaliak.Service.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddDbContext<ThaliakContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("sqlite")
                           ?? "Data Source=/data/thaliak.db;Cache=Shared";

    options
        .UseSqlite(connectionString, sqlite => sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
        .UseSnakeCaseNamingConvention()
        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});

builder.Services.Configure<ArtifactOptions>(builder.Configuration.GetSection(ArtifactOptions.SectionName));
builder.Services.AddHttpClient<ArtifactWebhookService>();
builder.Services.AddSingleton<ArtifactPathService>();
builder.Services.AddScoped<ThaliakReadService>();
builder.Services.AddScoped<CatalogReadService>();
builder.Services.AddScoped<ArtifactReadService>();
builder.Services.AddScoped<ArtifactBuildService>();
builder.Services.AddScoped<PatchArchiveService>();
builder.Services.AddHostedService<ArtifactBuildHostedService>();

var app = builder.Build();

var v2 = app.MapGroup("/api/v2beta");
v2.MapRepositoryEndpoints();
v2.MapServiceEndpoints();
v2.MapArtifactEndpoints();

app.MapGraphQlCompatibilityEndpoint();
app.MapArtifactFileEndpoints();
app.MapPatchArchiveEndpoints();

app.Run();

public partial class Program;
