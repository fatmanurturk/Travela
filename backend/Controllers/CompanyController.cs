using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Travela.API.Controllers
{
    [ApiController]
    [Route("api/company")]
    [Authorize(Roles = "Company")]
    public class CompanyController : ControllerBase
    {
        [HttpGet("dashboard")]
        public IActionResult GetDashboard()
        {
            return Ok(new
            {
                Message = "Travela Firma Paneline Hoş Geldiniz!",
                Role = "Company",
                Access = "Authorized"
            });
        }
    }
}