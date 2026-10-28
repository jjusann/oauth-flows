using Microsoft.AspNetCore.Authorization;

namespace ProtectedApi.Authorization;

// Standard pattern for scope-based authorization against a Keycloak/OAuth2
// access token, where "scope" is a single space-delimited string claim
// rather than one claim per scope.
public class ScopeRequirement : IAuthorizationRequirement
{
    public string RequiredScope { get; }

    public ScopeRequirement(string requiredScope)
    {
        RequiredScope = requiredScope;
    }
}

public class ScopeHandler : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ScopeRequirement requirement)
    {
        var scopeClaim = context.User.FindFirst(c => c.Type == "scope");

        if (scopeClaim is null)
        {
            return Task.CompletedTask;
        }

        var scopes = scopeClaim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (scopes.Contains(requirement.RequiredScope))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
