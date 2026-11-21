using ApiUsers.Application.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace ApiUsers.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        return services;
    }
}
