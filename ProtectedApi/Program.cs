using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using ProtectedApi.Authorization;

var builder = WebApplication.CreateBuilder(args);

var authority = builder.Configuration["Keycloak:Authority"]!;
var audience = builder.Configuration["Keycloak:Audience"]!;
var requireHttps = builder.Configuration.GetValue<bool>("Keycloak:RequireHttpsMetadata");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.Audience = audience;
        options.RequireHttpsMetadata = requireHttps;
        options.TokenValidationParameters.ValidateAudience = true;

        // Keep claim types exactly as Keycloak issues them (e.g. "sub"
        // stays "sub") instead of ASP.NET Core's default remapping to
        // long ClaimTypes URIs. Simpler to reason about as this API
        // grows and more claims get read from the token.
        options.MapInboundClaims = false;
    });

builder.Services.AddSingleton<IAuthorizationHandler, ScopeHandler>();

builder.Services.AddAuthorization(options =>
{
    // Satisfied by the Authorization Code + PKCE flow (WebClient).
    options.AddPolicy("ApiRead", policy =>
        policy.Requirements.Add(new ScopeRequirement("api.read")));

    // Satisfied by the Client Credentials flow (MachineClient).
    options.AddPolicy("ApiAdmin", policy =>
        policy.Requirements.Add(new ScopeRequirement("api.admin")));
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();