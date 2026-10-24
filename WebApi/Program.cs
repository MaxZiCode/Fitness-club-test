using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Common.Caching;
using DataAccessLayer;
using DataAccessLayer.Entities;
using DataAccessLayer.Seeding;
using DataAccessLayer.UnitOfWork;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ServiceLayer.Analytics;
using ServiceLayer.Enrollments;
using ServiceLayer.Enrollments.Waitlist;
using ServiceLayer.Idempotency;
using ServiceLayer.Schedule;
using ServiceLayer.Shared;
using StackExchange.Redis;
using WebApi.Auth;
using WebApi.Configuration;
using WebApi.Middleware;
using WebApi.ModelBinding;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

builder.Services.AddControllers(options =>
{
    options.ModelBinderProviders.Insert(0, new DateOnlyModelBinderProvider());
    options.ModelBinderProviders.Insert(0, new UtcDateTimeModelBinderProvider());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = JwtBearerDefaults.AuthenticationScheme,
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddOptions<ApiRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(ApiRateLimitOptions.SectionName))
    .Validate(options => options.RequestsPerMinute > 0, "RequestsPerMinute must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return RateLimitPartition.GetNoLimiter<string>("anonymous");
        }

        var rpm = context.RequestServices
            .GetRequiredService<IOptions<ApiRateLimitOptions>>()
            .Value.RequestsPerMinute;

        return RateLimitPartition.GetFixedWindowLimiter($"user:{userId}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rpm,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

builder.Services.AddDbContext<ClubDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

builder.Services.AddSingleton<ICacheService>(provider =>
    new RedisCacheService(provider.GetRequiredService<IConnectionMultiplexer>(), TimeSpan.FromMinutes(5)));

builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<IDateTimeService, DateTimeService>();
builder.Services.AddSingleton<IIdempotencyKeyHasher, IdempotencyKeyHasher>();
builder.Services.AddScoped<IPassValidationService, PassValidationService>();
builder.Services.AddScoped<IClassValidationService, ClassValidationService>();
builder.Services.AddScoped<IEnrollmentValidationService, EnrollmentValidationService>();
builder.Services.AddScoped<ICreateEnrollmentService, CreateEnrollmentService>();
builder.Services.AddScoped<IJoinWaitlistService, JoinWaitlistService>();
builder.Services.AddScoped<ICancelWaitlistService, CancelWaitlistService>();
builder.Services.AddScoped<IUpdateEnrollmentSeatsService, UpdateEnrollmentSeatsService>();
builder.Services.AddScoped<ICancelEnrollmentService, CancelEnrollmentService>();
builder.Services.Configure<EnrollmentCardCacheOptions>(
    builder.Configuration.GetSection("EnrollmentCardCache"));
builder.Services.AddScoped<IEnrollmentCardCacheService, EnrollmentCardCacheService>();
builder.Services.AddScoped<IEnrollmentCardService, EnrollmentCardService>();
builder.Services.AddScoped<IScheduleService, ScheduleService>();
builder.Services.AddScoped<ILoadService, LoadService>();
builder.Services.AddScoped<IScheduleContinuationTokenService, ScheduleContinuationTokenService>();

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()!;

builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<AccessTokenService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

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
