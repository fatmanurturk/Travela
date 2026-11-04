using System.Security.Claims;
using Travela.Models;

namespace Travela.Api.Security;

public static class AccountAccess
{
    // IsActive is solely the administrator's account switch. Credential setup never changes it.
    public static IQueryable<User> EnabledAccounts(this IQueryable<User> users) => users.Where(u => u.IsActive &&
        (u.Role == "Passenger" || u.Role == "Admin" || u.Role == "Company"));

    public static IQueryable<User> WithActiveCompany(this IQueryable<User> users, Travela.Data.TravelaDbContext db) =>
        users.Where(u => u.Role != "Company" || (u.CompanyId != null && db.Companies.Any(c => c.Id == u.CompanyId && c.IsActive)));

    public static IQueryable<User> PasswordLoginAccounts(this IQueryable<User> users, Travela.Data.TravelaDbContext db) =>
        users.EnabledAccounts().WithActiveCompany(db).Where(u => !u.ActivationPending && !u.PasswordResetRequired);

    public static IQueryable<User> SessionAccounts(this IQueryable<User> users, Travela.Data.TravelaDbContext db) =>
        users.PasswordLoginAccounts(db).Where(u => !u.MustChangePassword);

    public static IQueryable<User> ForSession(this IQueryable<User> users, ClaimsPrincipal principal, Travela.Data.TravelaDbContext db)
    {
        if (principal.Identity?.IsAuthenticated != true ||
            !int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ||
            !int.TryParse(principal.FindFirstValue("security_version"), out var version))
            return users.Where(_ => false);
        var role = principal.FindFirstValue(ClaimTypes.Role);
        return users.SessionAccounts(db).Where(u => u.Id == id && u.Role == role && u.SecurityVersion == version);
    }
}
