using Argus.Server.Data;
using Argus.Server.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Argus.Server.Features.Auth;

public static class AuthServiceExtensions
{
    public const string SessionCookieName = "argus_session";

    public static IServiceCollection AddArgusAuth(this IServiceCollection services)
    {
        services.AddValidatedOptions<AuthOptions>(AuthOptions.SectionName);

        services.AddIdentity<ArgusUser, IdentityRole<Guid>>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // User names are email addresses, which may contain characters Identity rejects by default.
                options.User.AllowedUserNameCharacters = "";

                // Length beats composition rules (NIST SP 800-63B).
                options.Password.RequiredLength = 10;
                options.Password.RequiredUniqueChars = 4;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);

                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<ArgusDbContext>()
            .AddSignInManager<ArgusSignInManager>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = SessionCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;

            // The SPA does its own routing: answer with status codes instead of redirecting to a login page.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        // Re-check sessions every minute so password changes and disabled accounts take effect quickly.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(1));

        // Secure by default: every endpoint requires a signed-in user unless it opts out explicitly.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }
}
