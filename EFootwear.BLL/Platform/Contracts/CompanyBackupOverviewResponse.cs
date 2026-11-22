namespace EFootwear.BLL.Platform.Contracts;

/// <summary>What a company owner sees on the Backup screen: the platform backup health and the company's own export history.</summary>
public sealed record CompanyBackupOverviewResponse(BackupHealthResponse? Health, IReadOnlyList<CompanyExportResponse> Exports);
