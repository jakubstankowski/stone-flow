using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StoneFlow.Access.Application.Abstractions;
using StoneFlow.Access.Domain.Accounts;
using StoneFlow.Access.Domain.Sessions;
using StoneFlow.Access.Infrastructure.Email;
using StoneFlow.Access.Infrastructure.Identity;
using StoneFlow.Access.Infrastructure.Logging;
using StoneFlow.Access.Infrastructure.Options;
using StoneFlow.Access.Infrastructure.Persistence;
using StoneFlow.Shared.Infrastructure.Cqrs;
using StoneFlow.Shared.Infrastructure.Persistence;

namespace StoneFlow.Access.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Nazwa ciasteczka sesji poza Development (prefiks <c>__Host-</c>: Secure, ścieżka /, bez domeny).</summary>
    public const string SessionCookieName = "__Host-sf_session";

    public const string DevelopmentSessionCookieName = "sf_session";

    public const string DataProtectionApplicationName = "StoneFlow";

    public static IServiceCollection AddAccessInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<AccessDbContext>((serviceProvider, options) => options.UseModuleSqlServer<AccessDbContext>(
            serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString(ModuleDbContextOptions.ConnectionStringName),
            AccessDbContext.Schema));

        services.TryAddSingleton(TimeProvider.System);
        AddOptions(services);
        AddIdentity(services);

        services.AddDataProtection()
            .SetApplicationName(DataProtectionApplicationName)
            .PersistKeysToDbContext<AccessDbContext>();

        services.AddScoped<IAccountService, IdentityAccountService>();
        services.AddScoped<ISessionManager, IdentitySessionManager>();
        services.AddScoped<IPasswordResetTokenStore, PasswordResetTokenStore>();
        services.AddScoped<IAccessUnitOfWork, AccessUnitOfWork>();
        services.AddSingleton<ISecurityEvents, SecurityEventLog>();
        services.AddSingleton<SmtpEmailSender>();
        services.AddSingleton<LogEmailSender>();
        services.AddSingleton<IEmailSender>(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<EmailOptions>>().Value.Mode == EmailMode.Log
                ? serviceProvider.GetRequiredService<LogEmailSender>()
                : serviceProvider.GetRequiredService<SmtpEmailSender>());

        services.AddHandlersFromAssembly(Application.AssemblyReference.Assembly);

        return services;
    }

    private static void AddOptions(IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<OwnerOptions>, OwnerOptionsValidator>();
        services.AddOptions<OwnerOptions>().BindConfiguration(OwnerOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<AppOptions>, AppOptionsValidator>();
        services.AddOptions<AppOptions>().BindConfiguration(AppOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>();
        services.AddOptions<EmailOptions>().BindConfiguration(EmailOptions.SectionName).ValidateOnStart();

        services.AddSingleton(serviceProvider =>
        {
            var owner = serviceProvider.GetRequiredService<IOptions<OwnerOptions>>().Value;
            var app = serviceProvider.GetRequiredService<IOptions<AppOptions>>().Value;
            AppOptionsValidator.TryParse(app.BaseUrl, out var baseUrl);
            return new AccessSettings(owner.Email ?? string.Empty, baseUrl);
        });
    }

    private static void AddIdentity(IServiceCollection services)
    {
        services.AddIdentityCore<AccessUser>(options =>
            {
                options.Lockout.MaxFailedAccessAttempts = LockoutPolicy.MaxFailedAttempts;
                options.Lockout.DefaultLockoutTimeSpan = LockoutPolicy.Duration;
                options.Lockout.AllowedForNewUsers = true;
                options.Password.RequiredLength = PasswordPolicy.MinLength;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
                options.User.RequireUniqueEmail = true;
                options.User.AllowedUserNameCharacters = string.Empty;
            })
            .AddUserStore<AccessUserStore>()
            .AddSignInManager();

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme);

        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<TimeProvider, IHostEnvironment>((options, timeProvider, environment) =>
            {
                var development = environment.IsDevelopment();
                options.Cookie.Name = development ? DevelopmentSessionCookieName : SessionCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = SessionPolicy.IdleTimeout;
                options.SlidingExpiration = true;
                options.TimeProvider = timeProvider;
                options.Events = SessionCookieEvents.Create();
            });
    }
}
