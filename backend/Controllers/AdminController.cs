using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Travela.API.Controllers
{
    [ApiController]
    [Route("api/admin")]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        [HttpGet("dashboard")]
        public IActionResult GetDashboard()
        {
            return Ok(new
            {
                Message = "Travela Admin Paneline Hoş Geldiniz!",
                Role = "Admin",
                Access = "Authorized"
            });
        }
    }
}