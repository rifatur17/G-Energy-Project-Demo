using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace GEnergy.IntegrationTests;
// Test-only endpoints: the production assembly never discovers these controllers.
[ApiController, Route("test/policies")]
public sealed class PolicyProbeController(IAuthorizationService authorization) : ControllerBase
{
    [Authorize(Policy = "Admin"), HttpGet("admin")] public IActionResult Admin() => Ok();
    [Authorize(Policy = "CarOwner"), HttpGet("owner")] public IActionResult Owner() => Ok();
    [Authorize(Policy = "CarDriver"), HttpGet("driver")] public IActionResult Driver() => Ok();
    [Authorize, HttpGet("resource/{ownerId:long}")]
    public async Task<IActionResult> Resource(long ownerId) => (await authorization.AuthorizeAsync(User, ownerId, "ResourceOwner")).Succeeded ? Ok() : Forbid();
}
