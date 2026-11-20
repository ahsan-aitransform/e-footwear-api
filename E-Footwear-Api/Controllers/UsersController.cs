using EFootwear.BLL.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers;

/// <summary>
/// User management. Who may create which role, and in which stores, is decided by the BLL
/// (UserCreationRules). A platform administrator passes companyId; a company user never does.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/users")]
public sealed class UsersController(UserService users) : ControllerBase
{
    /// <summary>Roles and stores the caller may use when creating a user.</summary>
    [HttpGet("creation-options")]
    public async Task<IActionResult> CreationOptions([FromQuery] long? companyId, CancellationToken ct) =>
        Ok(await users.GetCreationOptionsAsync(companyId, ct));

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] long? companyId, CancellationToken ct) =>
        Ok(await users.ListAsync(companyId, ct));

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest body, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new { id = await users.CreateAsync(body, ct) });
}
