using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Common.Caching;
using DataAccessLayer;
using DataAccessLayer.Entities;
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
using WebApi.ModelBinding;

namespace WebApi.Extensions;

public static class ServiceCollectionExtensions
{
    public static WebApplicationBuilder AddApiServices(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddControllers(options =>
        {
            options.ModelBinderProviders.Insert(0, new DateOnlyModelBinderProvider());
            options.ModelBinderProviders.Insert(0, new UtcDateTimeModelBinderProvider());
        });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
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

        services.AddProblemDetails();
        services.AddHealthChecks();
        return builder;
    }

    public static WebApplicationBuilder AddApiRateLimiting(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        services.AddOptions<ApiRateLimitOptions>()
            .Bind(configuration.GetSection(ApiRateLimitOptions.SectionName))
            .Validate(options => options.RequestsPerMinute > 0, "RequestsPerMinute must be greater than zero.")
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userId is null)
                {
                    return RateLimitPartition.GetNoLimiter<string>("anonymous");
                }

                var requestsPerMinute = context.RequestServices
                    .GetRequiredService<IOptions<ApiRateLimitOptions>>()
                    .Value.RequestsPerMinute;

                return RateLimitPartition.GetFixedWindowLimiter($"user:{userId}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = requestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
            });
        });

        return builder;
    }

    public static WebApplicationBuilder AddDataAccess(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        services.AddDbContext<ClubDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres")));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        return builder;
    }

    public static WebApplicationBuilder AddCache(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(configuration.GetConnectionString("Redis")!));
        services.AddSingleton<ICacheService>(provider =>
            new RedisCacheService(provider.GetRequiredService<IConnectionMultiplexer>(), TimeSpan.FromMinutes(5)));
        return builder;
    }

    public static WebApplicationBuilder AddServiceLayer(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<IDateTimeService, DateTimeService>();
        services.AddSingleton<IIdempotencyKeyHasher, IdempotencyKeyHasher>();

        services.AddScoped<IPassValidationService, PassValidationService>();
        services.AddScoped<IClassValidationService, ClassValidationService>();
        services.AddScoped<IEnrollmentValidationService, EnrollmentValidationService>();
        services.AddScoped<ICreateEnrollmentService, CreateEnrollmentService>();
        services.AddScoped<IJoinWaitlistService, JoinWaitlistService>();
        services.AddScoped<ICancelWaitlistService, CancelWaitlistService>();
        services.AddScoped<IUpdateEnrollmentSeatsService, UpdateEnrollmentSeatsService>();
        services.AddScoped<ICancelEnrollmentService, CancelEnrollmentService>();

        services.AddOptions<EnrollmentCancellationOptions>()
            .Bind(configuration.GetSection(EnrollmentCancellationOptions.SectionName))
            .Validate(options => options.RefundCutoff >= TimeSpan.Zero, "RefundCutoff must be zero or greater.")
            .ValidateOnStart();
        services.Configure<EnrollmentCardCacheOptions>(configuration.GetSection("EnrollmentCardCache"));

        services.AddScoped<IEnrollmentCardCacheService, EnrollmentCardCacheService>();
        services.AddScoped<IEnrollmentCardService, EnrollmentCardService>();
        services.AddScoped<IScheduleService, ScheduleService>();
        services.AddScoped<ILoadService, LoadService>();
        services.AddScoped<IScheduleContinuationTokenService, ScheduleContinuationTokenService>();

        return builder;
    }

    public static WebApplicationBuilder AddJwtAuthentication(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        var jwtOptions = configuration.GetSection("Jwt").Get<JwtOptions>()!;

        services.AddSingleton(jwtOptions);
        services.AddSingleton<AccessTokenService>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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

        services.AddAuthorization();
        return builder;
    }
}
