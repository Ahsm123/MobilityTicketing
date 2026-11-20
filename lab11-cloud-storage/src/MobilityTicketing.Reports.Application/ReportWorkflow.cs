using System.Security.Cryptography;

namespace MobilityTicketing.Reports.Application;

public sealed class ReportWorkflow(
    IReportObjectStore storage, IReportCatalogue catalogue, IUploadReceipts receipts, ExportLocation destination)
{
    private readonly IReportObjectStore _storage = storage;
    private readonly IReportCatalogue _catalogue = catalogue;
    private readonly IUploadReceipts _receipts = receipts;
    private readonly ExportLocation _destination = destination;

    public async Task<string> StageAsync(string reportId, byte[] bytes)
    {
        _receipts.Reserve(reportId);
        var versionId = await _storage.UploadAsync(_destination, bytes);
        var reportInput = new ReportInput(
            reportId,
            _destination.Bucket,
            _destination.Key,
            versionId,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLower(),
            bytes.Count(x => x == '\n')
        );
        
        await _receipts.WriteAsync(reportInput);

        return versionId;
    }

    public async Task<string> PublishAsync(string reportId, byte[] bytes, string? supersedes)
    {
        await StageAsync(reportId, bytes);
        var result = await RecoverAsync(reportId, supersedes);
        return result;
    }

    public async Task<string> RecoverAsync(string reportId, string? supersedes)
    {
        var receipt = await _receipts.ReadAsync(reportId);
        var report = await _catalogue.FindAsync(reportId);
    }

    public Task<Verification> VerifyAsync(string reportId) =>
        throw new NotImplementedException("Complete VerifyAsync.");

    private async Task<Verification> CheckAsync(ReportInput input)
    {
        var bytes = await _storage.ReadVersionAsync(new ExportLocation(input.Bucket, input.Key), input.VersionId);
        var receipt = _receipts.ReadAsync(input.ReportId);
    }
}
