using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
namespace GEnergy.Api.Authorization;

public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    public long Id => long.Parse(accessor.HttpContext!.User.FindFirstValue("sub")!, System.Globalization.CultureInfo.InvariantCulture);
    public string UserCode => accessor.HttpContext!.User.FindFirstValue("userCode")!;
    public string Role => accessor.HttpContext!.User.FindFirstValue("role")!;
}
public sealed record ResourceOwnerRequirement : IAuthorizationRequirement;
public sealed class ResourceOwnerHandler : AuthorizationHandler<ResourceOwnerRequirement, long>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ResourceOwnerRequirement requirement, long ownerUserId)
    {
        if (context.User.Identity?.IsAuthenticated == true && long.TryParse(context.User.FindFirstValue("sub"), out var id) && id == ownerUserId)
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
