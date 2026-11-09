using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travela.Api.Legal;

namespace Travela.Api.Controllers;

[ApiController]
[Route("api/legal/documents")]
[AllowAnonymous]
public class LegalDocumentsController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(LegalDocuments.Archive);
}
