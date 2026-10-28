using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

var authority = builder.Configuration["Keycloak:Authority"]!;
var clientId = builder.Configuration["Keycloak:ClientId"]!;
var requireHttps = builder.Configuration.GetValue<bool>("Keycloak:RequireHttpsMetadata");

builder.Services.AddControllersWithViews();

builder.Services.AddHttpClient("ProtectedApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ProtectedApi:BaseUrl"]!);
})
// DEV-ONLY: ProtectedApi runs on a self-signed ASP.NET Core dev
// certificate, and `dotnet dev-certs https --trust` does not reliably
// register it with the OS trust store on Linux. Skipping certificate
// validation here is acceptable for localhost-to-localhost traffic in
// this demo, and would NEVER be acceptable in production — a real
// deployment would use certificates issued by a trusted CA instead.
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie()
.AddOpenIdConnect(options =>
{
    options.Authority = authority;
    options.ClientId = clientId;
    options.RequireHttpsMetadata = requireHttps;

    // This is a public client (no client secret) — the Authorization
    // Code flow is protected by PKCE instead of a client secret.
    options.ResponseType = "code";
    options.UsePkce = true;

    options.Scope.Clear();
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("api.read");

    options.SaveTokens = true;
    options.GetClaimsFromUserInfoEndpoint = true;
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultControllerRoute();

app.Run();
