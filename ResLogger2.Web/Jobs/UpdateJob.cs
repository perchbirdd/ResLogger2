using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Quartz;
using ResLogger2.Common;
using ResLogger2.Common.ServerDatabase;
using ResLogger2.Common.ServerDatabase.Model;
using ResLogger2.Web.Services;
using FileInfo = System.IO.FileInfo;
// ReSharper disable LogMessageIsSentenceProblem

namespace ResLogger2.Web.Jobs;

[DisallowConcurrentExecution]
public class UpdateJob : IJob
{
	private static readonly HttpClient _httpClient = new();

	static UpdateJob()
	{
		_httpClient.DefaultRequestHeaders.Add("User-Agent", "FFXIV PATCH CLIENT");
		_httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
		_httpClient.Timeout = TimeSpan.FromSeconds(10);
	}
	
	private readonly ILogger<UpdateJob> _logger;
	private readonly ServerHashDatabase _db;
	private readonly IDbLockService _dbLockService;
	private readonly IThaliakService _thaliakService;

	public UpdateJob(ILogger<UpdateJob> logger, ServerHashDatabase db, IDbLockService dbLockService, IThaliakService thaliakService)
	{
		_logger = logger;
		_db = db;
		_dbLockService = dbLockService;
		_thaliakService = thaliakService;

		_logger.LogInformation("UpdateJob Created");
	}

	public async Task Execute(IJobExecutionContext context)
	{
		var jobTimer = Stopwatch.StartNew();
		_logger.LogInformation("Begin update!");
		
		_logger.LogInformation("Determining repo versions...");
		var currentRepoVersions = 
			await _db.LatestProcessedVersions
				.Include(lpv => lpv.Version)
				.ToDictionaryAsync(lpv => lpv.Repo, lpv => lpv);
		_logger.LogInformation("Got repo versions.");
		
		_logger.LogInformation("Getting latest versions...");
		var updatesNeeded = new Dictionary<string, (GameVersion current, GameVersion latest)>();
		var latestVersions = await _thaliakService.GetLatestVersions();
		foreach (var latestVersion in latestVersions)
		{
			if (currentRepoVersions.TryGetValue(latestVersion.Repo, out var currentVersion))
			{
				if (currentVersion.Version != latestVersion.Version)
					updatesNeeded.Add(latestVersion.Repo, (currentVersion.Version, latestVersion.Version));
			}
			else
			{
				_logger.LogInformation("unknown repo {repo}", latestVersion.Repo);
			}
		}
		
		_logger.LogInformation("Got latest versions.");
		_logger.LogInformation("Determining updates.");
		
		if (updatesNeeded.Count == 0)
		{
			_logger.LogInformation("No updates detected.");
			return;
		}
		
		_logger.LogInformation("Updates needed: {count}", updatesNeeded.Count);
		
		foreach (var kv in updatesNeeded)
		{
			_logger.LogInformation("Update available for {repo} from {version} to {version2}", kv.Key, kv.Value.current, kv.Value.latest);	
		}
		
		_logger.LogInformation("Determined updates.");
		_logger.LogInformation("Getting patch URLs...");
		
		var patchFiles = await _thaliakService.GetPatchUrls();
		var patchFilesNeeded = new Dictionary<string, List<string>>();
		foreach (var patchFile in patchFiles)
		{
			if (Path.GetFileNameWithoutExtension(patchFile).StartsWith("H")) continue;
			
			var versionInfo = GetRepoAndVersion(patchFile);

			if (!updatesNeeded.TryGetValue(versionInfo.Repo, out var update))
			{
				_logger.LogTrace("No updates needed for patch file {patchFile} (repo: {repo}, version: {version})", patchFile, versionInfo.Repo, versionInfo.Version);
				continue;
			}

			if (versionInfo.Version <= update.current) continue;
			
			if (patchFilesNeeded.TryGetValue(versionInfo.Repo, out var list))
				list.Add(patchFile);
			else
				patchFilesNeeded.Add(versionInfo.Repo, new List<string> {patchFile});

			_logger.LogInformation("Adding patch file {patchFile} for update (repo: {repo}, version: {version})", patchFile, versionInfo.Repo, versionInfo.Version);
		}
		
		_logger.LogInformation("Got patch URLs.");
		_logger.LogInformation("Creating root directory...");

		var patchDownloadDir = Path.GetTempFileName();
		File.Delete(patchDownloadDir);
		Directory.CreateDirectory(patchDownloadDir);
		
		_logger.LogInformation("Created root: {patchdir}", patchDownloadDir);
		_logger.LogInformation("Downloading patch files...");
		
		var downloadTasks = new List<Task>();
		foreach (var kv in patchFilesNeeded)
		{
			var repo = kv.Key;
			var files = kv.Value;

			foreach (var file in files)
			{
				var fileName = Path.GetFileName(file);
				var repoDir = Path.Combine(patchDownloadDir, repo);
				Directory.CreateDirectory(repoDir);
				var filePath = new FileInfo(Path.Combine(repoDir, fileName));
				var downloadTask = DownloadFileAsync(file, filePath);
				_logger.LogInformation("Adding task to download {file} to {path}", file, filePath);
				downloadTasks.Add(downloadTask.ContinueWith(task =>
				{
					if (task.IsFaulted)
						_logger.LogError(task.Exception, "Error downloading {file}.", file);
					else
						_logger.LogInformation("Downloaded {file} successfully.", file);
				}));
			}
		}
		_logger.LogInformation("Awaiting downloads...");
		await Task.WhenAll(downloadTasks);
		_logger.LogInformation("Patch file downloads complete.");
		_logger.LogInformation("Beginning patch processing.");

		var timer = Stopwatch.StartNew();
		var holders = new List<PatchIndexHolder>();

		try
		{
			foreach (var file in Directory.GetFiles(patchDownloadDir, "*.patch", SearchOption.AllDirectories))
			{
				var repo = Path.GetFileName(Path.GetDirectoryName(file));

				_logger.LogInformation("Processing {file}... ", file);
				var holder = PatchIndexHolder.FromPatch(file, repo);
				holders.Add(holder);
			}
		}
		catch (Exception e)
		{
			_logger.LogError(e, "Error processing patch files.");
		}
		finally
		{
			Directory.Delete(patchDownloadDir, true);
		}
		
		_logger.LogInformation("Patch processing complete.");
		_logger.LogInformation("Beginning patch import.");
		
		var loc = await _dbLockService.AcquireLockAsync(TimeSpan.FromSeconds(30));
		
		try
		{
			if (!loc)
			{
				_logger.LogError("Could not acquire db lock.");
				return;
			}
			_db.ReloadCache();
			foreach (var patchIndexHolder in holders)
				_db.ProcessInMemory(patchIndexHolder);
			await _db.SaveChangesAsync();
			_logger.LogInformation("Processing and importing took {elapsed:c}.", TimeSpan.FromMilliseconds(timer.ElapsedMilliseconds));
		}
		catch (Exception e)
		{
			_logger.LogError(e, "An error occurred while importing patches.");
		}
		finally
		{
			if (loc)
				_dbLockService.ReleaseLock();
		}
		
		_logger.LogInformation("Patch import complete.");
		_logger.LogInformation("Update complete! Took {Elapsed:c}.", TimeSpan.FromMilliseconds(jobTimer.ElapsedMilliseconds));
	}
	
	private static LatestProcessedVersion GetRepoAndVersion(string pUrl)
	{
		var url = pUrl.AsSpan();
		var gameIndex = url.LastIndexOf("game/", StringComparison.Ordinal);
		var gameEnd = gameIndex + 5;
		if (url[gameEnd] == 'e')
			gameEnd += 4;
		var repo = url[gameEnd.. (gameEnd + 8)];
		var fileName = Path.GetFileNameWithoutExtension(url);
		var isHist = fileName.StartsWith("H");
		var length = fileName.Length - (isHist ? 1 : 0);
		var version = fileName[1.. length];
		return new LatestProcessedVersion{ Repo = new string(repo), Version = GameVersion.Parse(new string(version))};
	}
	
	private static async Task DownloadFileAsync(string url, FileInfo targetFile)
	{
		using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
		if (!response.IsSuccessStatusCode) throw new Exception($"Download failed with response code {response.StatusCode}");

		await using var streamToReadFrom = await response.Content.ReadAsStreamAsync();
		await using Stream streamToWriteTo = targetFile.Create();
		await streamToReadFrom.CopyToAsync(streamToWriteTo);
	}

	private static async Task DownloadFileAsync2(string url, FileInfo targetFile)
	{
		using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
		if (!response.IsSuccessStatusCode) throw new Exception($"Download failed with response code {response.StatusCode}");
		await using var streamToReadFrom = await response.Content.ReadAsStreamAsync();
		await using Stream streamToWriteTo = targetFile.Create();
		await streamToReadFrom.CopyToAsync(streamToWriteTo);
		await streamToWriteTo.FlushAsync();
		await streamToWriteTo.DisposeAsync();
		streamToWriteTo.Close();
	}
}