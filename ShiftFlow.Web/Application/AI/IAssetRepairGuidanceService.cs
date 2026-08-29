namespace ShiftFlow.Application.AI;

/// <summary>Looks up a repair/replacement video for one specific tracked asset, with asset
/// metadata always returned so the caller can fall back to text guidance. See
/// AssetRepairGuidanceService for the anti-misuse constraints this deliberately enforces.</summary>
public interface IAssetRepairGuidanceService
{
    Task<object> GetRepairGuidanceAsync(int assetId, string userId, CancellationToken ct);
}
