using System.Text;
using ApiUsers.Application.Auth;
using ApiUsers.Infrastructure.Auth;
using ApiUsers.Infrastructure.Data;
using ApiUsers.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ApiUsers.Infrastructure;

public static class DependencyInjection
{
    private const int MinSigningKeyBytes = 32; // HS256 needs a 256-bit key

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddAppDatabase();
        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer), "Jwt:Issuer is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Audience), "Jwt:Audience is required.")
            .Validate(o => Encoding.UTF8.GetByteCount(o.SigningKey) >= MinSigningKeyBytes,
                $"Jwt:SigningKey must be at least {MinSigningKeyBytes} bytes.")
            .Validate(o => o.AccessTokenMinutes > 0 && o.RefreshTokenDays > 0,
                "Jwt token lifetimes must be positive.")
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IIdentityService, IdentityService>();
        return services;
    }
}
