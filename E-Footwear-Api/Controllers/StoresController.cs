using EFootwear.BLL.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/stores")]
public sealed class StoresController(StoreService stores) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<StoreDto>> List(CancellationToken ct) => await stores.ListAsync(ct);
}
