using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProtectedApi.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    // Reachable only with a token carrying the "api.admin" scope,
    // i.e. the one issued via the Client Credentials flow to the
    // MachineClient service account — no human user involved.
    [HttpGet("system-status")]
    [Authorize(Policy = "ApiAdmin")]
    public IActionResult GetSystemStatus()
    {
        var clientId = User.FindFirst("azp")?.Value ?? "unknown";
        return Ok(new
        {
            message = "Machine-to-machine data, accessible via Client Credentials token.",
            clientId
        });
    }
}
