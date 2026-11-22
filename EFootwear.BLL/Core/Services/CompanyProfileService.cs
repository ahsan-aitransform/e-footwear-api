using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Common;
using EFootwear.BLL.Core.Abstractions;
using EFootwear.BLL.Core.Contracts;
using EFootwear.Domain.Common;
using EFootwear.Domain.Platform;
using Microsoft.Extensions.Logging;

namespace EFootwear.BLL.Core.Services;

/// <summary>
/// The legal and regional part of the company profile (legal name, NTN, STRN, time zone, currency, rounding, price band basis).
/// The business name and invoice text are edited on the Business Info tab. A company changes only its own profile.
/// </summary>
public sealed class CompanyProfileService(
    ICompanyProfileRepository companyProfileRepository,
    RequestContext requestContext,
    IUnitOfWork unitOfWork,
    ILogger<CompanyProfileService> logger)
{
    public async Task<CompanyProfileResponse> GetAsync(CancellationToken cancellationToken) =>
        await companyProfileRepository.FindAsync(requestContext.CompanyId, cancellationToken)
        ?? throw new DomainException(ErrorKind.NotFound, "Company not found.");

    public async Task<CompanyProfileResponse> UpdateAsync(UpdateCompanyProfileRequest request, CancellationToken cancellationToken)
    {
        var patch = new Dictionary<string, string?>
        {
            ["legal_name"] = CompanyProfileRules.NormalizeLegalName(request.LegalName),
            ["ntn"] = CompanyProfileRules.NormalizeTaxNumber(request.Ntn, "NTN"),
            ["strn"] = CompanyProfileRules.NormalizeTaxNumber(request.Strn, "STRN"),
            ["timezone"] = CompanyProfileRules.NormalizeTimezone(request.Timezone),
            ["currency_code"] = CompanyProfileRules.NormalizeCurrencyCode(request.CurrencyCode),
            ["rounding_mode"] = CompanyProfileRules.NormalizeRoundingMode(request.RoundingMode),
            ["price_band_basis"] = CompanyProfileRules.NormalizePriceBandBasis(request.PriceBandBasis),
        };
        await companyProfileRepository.UpdateAsync(requestContext.CompanyId, patch, request.ExpectedRowVersion, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Company profile of {CompanyId} updated by user {UserId}", requestContext.CompanyId, requestContext.UserId);
        return await GetAsync(cancellationToken);
    }
}
