using ResLogger2.Common.ServerDatabase.Model;

namespace ResLogger2.Web.Services;

public interface IThaliakService
{
	public Task<List<LatestProcessedVersion>> GetLatestVersions();
	public Task<List<string>> GetPatchUrls();
}