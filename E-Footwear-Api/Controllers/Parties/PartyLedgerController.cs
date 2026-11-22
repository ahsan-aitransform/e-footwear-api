using E_Footwear_Api.Authorization;
using E_Footwear_Api.Routing;
using EFootwear.BLL.Common;
using EFootwear.BLL.Parties.Contracts;
using EFootwear.BLL.Parties.Services;
using EFootwear.Domain.Security.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Footwear_Api.Controllers.Parties;

/// <summary>Party Account Detail (docx 3.4): the ledger of one party or of all parties. Read only.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]
[Route(ApiRoutes.PartyLedger.Root)]
public sealed class PartyLedgerController(PartyLedgerService partyLedgerService) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.Party.AccountDetail.View)]
    public async Task<ActionResult<PartyLedgerResponse>> GetLedger(
        [FromQuery] PartyLedgerQuery query, [FromQuery] PageRequest pageRequest, CancellationToken cancellationToken) =>
        Ok(await partyLedgerService.GetLedgerAsync(query, pageRequest, cancellationToken));
}
