using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

// Client Credentials flow: no user, no browser redirect — the
// MachineClient authenticates itself directly with its client_id
// and client_secret and gets back a token scoped to "api.admin".

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

const string tokenEndpoint = "http://localhost:8080/realms/oauth-flows/protocol/openid-connect/token";
const string apiBaseUrl = "https://localhost:5101";

var clientId = configuration["Keycloak:ClientId"]!;
var clientSecret = configuration["Keycloak:ClientSecret"]!;

// DEV-ONLY: ProtectedApi runs on a self-signed ASP.NET Core dev
// certificate, and `dotnet dev-certs https --trust` does not reliably
// register it with the OS trust store on Linux. Skipping certificate
// validation here is acceptable for localhost-to-localhost traffic in
// this demo, and would NEVER be acceptable in production.
using var httpClient = new HttpClient(new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
});

Console.WriteLine("Requesting a Client Credentials token from Keycloak...");

var tokenResponse = await httpClient.PostAsync(tokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
{
    ["grant_type"] = "client_credentials",
    ["client_id"] = clientId,
    ["client_secret"] = clientSecret,
    ["scope"] = "api.admin"
}));

if (!tokenResponse.IsSuccessStatusCode)
{
    Console.WriteLine($"Token request failed: {tokenResponse.StatusCode}");
    Console.WriteLine(await tokenResponse.Content.ReadAsStringAsync());
    return;
}

var tokenPayload = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
var accessToken = tokenPayload.GetProperty("access_token").GetString();

Console.WriteLine("Token acquired. Calling the protected API...");

httpClient.DefaultRequestHeaders.Authorization =
    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

var apiResponse = await httpClient.GetAsync($"{apiBaseUrl}/api/admin/system-status");
var body = await apiResponse.Content.ReadAsStringAsync();

Console.WriteLine($"API responded with {(int)apiResponse.StatusCode} {apiResponse.StatusCode}");
Console.WriteLine(body);
