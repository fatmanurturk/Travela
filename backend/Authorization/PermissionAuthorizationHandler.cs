using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Travela.Data;

namespace Travela.Api.Authorization
{
    public class PermissionAuthorizationHandler
        : AuthorizationHandler<PermissionRequirement>
    {
        private readonly TravelaDbContext _context;

        public PermissionAuthorizationHandler(
            TravelaDbContext context)
        {
            _context = context;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement)
        {
            // Kullanıcının kimliğini JWT üzerinden al
            var userIdClaim = context.User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            if (!int.TryParse(userIdClaim, out int userId))
                return;

            // Kullanıcının güncel rolünü veritabanından oku
            var userRole = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.Role)
                .FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(userRole))
                return;

            // Rolün istenen yetkiye sahip olup olmadığını kontrol et
            bool hasPermission = await _context.RolePermissions
                .AsNoTracking()
                .AnyAsync(rp =>
                    rp.Role.Name == userRole &&
                    rp.Permission.Name == requirement.PermissionName);

            if (hasPermission)
            {
                context.Succeed(requirement);
            }
        }
    }
}