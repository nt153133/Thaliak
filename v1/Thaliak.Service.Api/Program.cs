using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Thaliak.Common.Database;
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
                           ?? "Data Source=/data/thaliak.db;Mode=ReadOnly;Cache=Shared";

    options
        .UseSqlite(connectionString, sqlite => sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
        .UseSnakeCaseNamingConvention()
        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});

builder.Services.AddScoped<ThaliakReadService>();

var app = builder.Build();

app.MapGroup("/api/v2beta").MapRepositoryEndpoints();
app.MapGraphQlCompatibilityEndpoint();

app.Run();

public partial class Program;
