using System.Security.Cryptography;

namespace MobilityTicketing.Reports.Application;

public sealed class ReportWorkflow(
    IReportObjectStore storage,
    IReportCatalogue catalogue,
    IUploadReceipts receipts,
    ExportLocation destination)
{
    private readonly IReportObjectStore _storage = storage;
    private readonly IReportCatalogue _catalogue = catalogue;
    private readonly IUploadReceipts _receipts = receipts;
    private readonly ExportLocation _destination = destination;

    public async Task<string> StageAsync(string reportId, byte[] bytes)
    {
        _receipts.Reserve(reportId);
        var versionId = await _storage.UploadAsync(_destination, bytes);
        if (string.IsNullOrWhiteSpace(versionId) || versionId is "null" or "None")
            throw new InvalidOperationException("MissingVersionId");
        var reportInput = new ReportInput(
            reportId,
            _destination.Bucket,
            _destination.Key,
            versionId,
            Hash(bytes),
            RowCount(bytes)
        );

        await _receipts.WriteAsync(reportInput);

        return versionId;
    }

    public async Task<string> PublishAsync(string reportId, byte[] bytes, string? supersedes)
    {
        await StageAsync(reportId, bytes);
        return await RecoverAsync(reportId, supersedes);
    }

    public async Task<string> RecoverAsync(string reportId, string? supersedes)
    {
        var receipt = await _receipts.ReadAsync(reportId);
        var result = await CheckAsync(receipt);
        if (result != Verification.Matches)
            throw new InvalidOperationException(result.ToString());
        await _catalogue.SaveAsync(receipt, supersedes);
        return receipt.VersionId;
    }

    public async Task<Verification> VerifyAsync(string reportId)
    {
        ReportInput? report;
        try
        {
            report = await _catalogue.FindAsync(reportId);
        }
        catch (CatalogueException)
        {
            return Verification.CatalogueReadFailed;
        }

        return report is null ? Verification.UnknownReport : await CheckAsync(report);
    }

    private async Task<Verification> CheckAsync(ReportInput input)
    {
        byte[] bytes;
        try
        {
            bytes = await _storage.ReadVersionAsync(new ExportLocation(input.Bucket, input.Key), input.VersionId);
        }
        catch (ObjectStoreException)
        {
            return Verification.ReadFailed;
        }

        return Hash(bytes) == input.Sha256 && RowCount(bytes) == input.RowCount
            ? Verification.Matches
            : Verification.Mismatch;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static int RowCount(byte[] bytes) => bytes.Count(b => b == '\n');
}