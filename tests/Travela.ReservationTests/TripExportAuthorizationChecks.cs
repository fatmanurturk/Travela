using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Travela.Api.Authorization;
using Travela.Api.Controllers;
using Travela.Api.Security;
using Travela.Data;
using Travela.Models;

internal static class TripExportAuthorizationChecks
{
    public static async Task Run(TravelaDbContext db, Action<bool, string> check, User user, bool expectedAllowed)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TravelaDbContext>(options => options.UseSqlServer(db.Database.GetConnectionString()));
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddAuthorization(options => options.AddPolicy("trip.view", policy => {
            policy.RequireAuthenticatedUser();
            policy.Requirements.Add(new PermissionRequirement("trip.view"));
        }));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var policies = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

        // The session's role claim is present and valid, but the identity uses a different
        // RoleClaimType. FindFirst/ForSession succeeds while IsInRole does not.
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("security_version", user.SecurityVersion.ToString())
        ], "Regression", ClaimTypes.Name, "roles");
        var principal = new ClaimsPrincipal(identity);
        check(!principal.IsInRole(user.Role) && await db.Users.AsNoTracking().ForSession(principal, db).AnyAsync(),
            $"{user.Role}: rol eşlemesi farklı olsa da mevcut oturum veritabanında doğrulanır");

        async Task<bool> Allowed(string method)
        {
            var attributes = typeof(TripsController).GetCustomAttributes<AuthorizeAttribute>()
                .Concat(typeof(TripsController).GetMethod(method)!.GetCustomAttributes<AuthorizeAttribute>());
            var policy = await AuthorizationPolicy.CombineAsync(policies, attributes);
            return (await authorization.AuthorizeAsync(principal, null, policy!)).Succeeded;
        }

        var list = await Allowed(nameof(TripsController.GetTrips));
        var summary = await Allowed(nameof(TripsController.GetSummary));
        var export = await Allowed(nameof(TripsController.ExportTrips));
        check(list == expectedAllowed && summary == expectedAllowed,
            $"{user.Role}: liste ve özet güncel trip.view iznini uygular (izin: {expectedAllowed})");
        check(export == list,
            $"{user.Role}: Excel ve liste aynı oturuma aynı yetkilendirme sonucunu verir (izin: {expectedAllowed})");
    }
}
