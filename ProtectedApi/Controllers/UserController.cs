using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProtectedApi.Controllers;

[ApiController]
[Route("api/user")]
public class UserController : ControllerBase
{
    // Reachable only with a token carrying the "api.read" scope,
    // i.e. the one issued via the Authorization Code + PKCE flow
    // to a logged-in human user.
    [HttpGet("profile")]
    [Authorize(Policy = "ApiRead")]
    public IActionResult GetProfile()
    {
        var subject = User.FindFirst("sub")?.Value ?? "unknown";
        return Ok(new
        {
            message = "User-context data, accessible via Authorization Code + PKCE token.",
            subject
        });
    }
}