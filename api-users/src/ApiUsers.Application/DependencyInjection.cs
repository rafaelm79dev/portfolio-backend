using ApiUsers.Application.Auth;
using ApiUsers.Application.Users;
using Microsoft.Extensions.DependencyInjection;

namespace ApiUsers.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        return services;
    }
}
