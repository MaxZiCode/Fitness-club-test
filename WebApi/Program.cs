using DataAccessLayer;
using DataAccessLayer.Seeding;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using WebApi.Extensions;
using WebApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

builder
    .AddApiServices()
    .AddApiRateLimiting()
    .AddDataAccess()
    .AddCache()
    .AddServiceLayer()
    .AddJwtAuthentication();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseSwagger();
app.UseSwaggerUI();

app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Services.GetRequiredService<IConnectionMultiplexer>();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ClubDbContext>();

    await dbContext.Database.MigrateAsync();
    await DatabaseSeeder.SeedAsync(dbContext);
}

app.Run();

public partial class Program;
