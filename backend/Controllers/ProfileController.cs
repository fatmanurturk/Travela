using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Travela.API.Controllers
{
    [ApiController]
    [Route("api/profile")]
    [Authorize]
    public class ProfileController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetProfile()
        {
            return Ok(new
            {
                Id = User.FindFirstValue(ClaimTypes.NameIdentifier),
                FullName = User.FindFirstValue(ClaimTypes.Name),
                Email = User.FindFirstValue(ClaimTypes.Email),
                Role = User.FindFirstValue(ClaimTypes.Role)
            });
        }
    }
}