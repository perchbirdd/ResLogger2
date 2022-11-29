using ResLogger2.Common.Api;

namespace ResLogger2.Web.Services;

public interface IPathDbService
{
	public Task<bool> ProcessDataAsync(UploadedDbData data);
	public Task<StatsData> GetStatsAsync();
}