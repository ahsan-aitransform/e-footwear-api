using EFootwear.BLL.Core.Contracts;

namespace EFootwear.BLL.Platform.Contracts;

public sealed record CompanyDetailResponse(CompanyProfileResponse Profile, PlanUsageResponse Usage);
