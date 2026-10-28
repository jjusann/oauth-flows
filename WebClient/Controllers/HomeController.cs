using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebClient.Controllers;

public class HomeController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;

    public HomeController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public IActionResult Index()
    {
        return View();
    }

    // Triggers the Authorization Code + PKCE redirect to Keycloak
    // if the user isn't already signed in.
    [Authorize]
    public async Task<IActionResult> CallApi()
    {
        var accessToken = await HttpContext.GetTokenAsync("access_token");

        var client = _httpClientFactory.CreateClient("ProtectedApi");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync("/api/user/profile");
        ViewBag.ApiResult = await response.Content.ReadAsStringAsync();
        ViewBag.StatusCode = (int)response.StatusCode;

        return View();
    }
}
