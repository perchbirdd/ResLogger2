using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using ResLogger2.Common;
using ResLogger2.Common.Api;
using ResLogger2.Common.ServerDatabase;
using ResLogger2.Common.ServerDatabase.Model;

namespace ResLogger2.Web.Services;

public class PathDbService : IPathDbService
{
	private struct ProcessingTotals
	{
		public int ExistPaths;
		public int NewPaths;
		public int PostProcessed;
		public bool WasInIndex1;
		public bool WasInIndex2;
	}

	private readonly ServerHashDatabase _db;
	private readonly IDbLockService _dbLockService;
	private readonly ILogger<PathDbService> _logger;
	private const string PostPrefix = "[p] ";

	public PathDbService(ServerHashDatabase db, IDbLockService dbLockService, ILogger<PathDbService> logger)
	{
		_db = db;
		_logger = logger;
		_dbLockService = dbLockService;
	}

	private void ProcessInternal(ref ProcessingTotals totals, IEnumerable<string> data, bool isPost)
	{
		var newPaths = 0;
		var existPaths = 0;
		var wasInIndex1 = false;
		var wasInIndex2 = false;
		var post = isPost ? PostPrefix : string.Empty;

		foreach (var path in data)
		{
			var hashes = Utils.CalcAllHashes(path.ToLower());
			var index = Utils.GetCategoryIdForPath(path);
			var pathQuery = _db.Paths.FirstOrDefault(p => p.IndexId == index
			                                              && p.FullHash == hashes.fullHash
			                                              && p.FolderHash == hashes.folderHash
			                                              && p.FileHash == hashes.fileHash);

			if (pathQuery != null) // ?
			{
				existPaths++;
				if (pathQuery.Path == null && path != null)
				{
					pathQuery.Path = path;
					_logger.LogInformation("{post}new: {path}", post, path);
					newPaths++;
				}
				continue;
			}

			var index1Query = _db.Index1StagingEntries
				.Include(i => i.FirstSeen)
				.Include(i => i.LastSeen)
				.FirstOrDefault(p => p.IndexId == index
				                     && p.FolderHash == hashes.folderHash
				                     && p.FileHash == hashes.fileHash);
			var index2Query = _db.Index2StagingEntries
				.Include(i => i.FirstSeen)
				.Include(i => i.LastSeen)
				.FirstOrDefault(p => p.IndexId == index
				                     && p.FullHash == hashes.fullHash);

			if (index1Query == null && index2Query == null)
			{
				_logger.LogWarning("{post}nonexistent: {path} {fullhash} {folderhash}/{filehash}", post, path, hashes.fullHash, hashes.folderHash, hashes.fileHash);
				continue;
			}
			existPaths++;

			GameVersion earliest = null;
			GameVersion latest = null;

			if (index1Query != null)
			{
				wasInIndex1 = true;
				earliest = index1Query.FirstSeen;
				latest = index1Query.LastSeen;
				_db.Index1StagingEntries.Remove(index1Query);
			}

			if (index2Query != null)
			{
				wasInIndex2 = true;
				if (earliest == null)
				{
					earliest = index2Query.FirstSeen;
				}
				else
				{
					if (index2Query.FirstSeen < earliest)
						earliest = index2Query.FirstSeen;
				}
				if (latest == null)
				{
					latest = index2Query.LastSeen;
				}
				else
				{
					if (index2Query.LastSeen > latest)
						latest = index2Query.LastSeen;
				}
				_db.Index2StagingEntries.Remove(index2Query);
			}

			if (latest == null || earliest == null)
			{
				_logger.LogError("{post}Latest or earliest is null: {path} {fullhash} {folderhash}/{filehash}", post, path, hashes.fullHash, hashes.folderHash, hashes.fileHash);
				continue;
			}

			_db.Paths.Add(new PathEntry
			{
				Path = path,
				IndexId = index,
				FullHash = hashes.fullHash,
				FolderHash = hashes.folderHash,
				FileHash = hashes.fileHash,
				FirstSeen = earliest,
				LastSeen = latest,
			});
			_logger.LogInformation("{post}new: {path}", post, path);
			newPaths++;
		}

		totals.ExistPaths += existPaths;
		totals.NewPaths += newPaths;
		totals.WasInIndex1 |= wasInIndex1;
		totals.WasInIndex2 |= wasInIndex2;
	}

	private HashSet<string> PostProcess(List<string> entries)
	{
		var toPost = new HashSet<string>();
		foreach (var path in entries)
		foreach (var entry in PathPostProcessor.PostProcess(path))
			toPost.Add(entry);
		return toPost;
	}

	public async Task<bool> ProcessDataAsync(UploadedDbData data)
	{
		var stopwatch = Stopwatch.StartNew();

		var totals = new ProcessingTotals();
		var success = false;

		var loc = await _dbLockService.AcquireLockAsync();
		
		try
		{
			if (loc)
			{
				ProcessInternal(ref totals, data.Entries, false);
				// var postEntries = PostProcess(data.Entries);
				// totals.PostProcessed = postEntries.Count;
				// ProcessInternal(ref totals, postEntries, true);

				await _db.SaveChangesAsync();
				success = true;
			}
			else
			{
				_logger.LogError("Failed to acquire lock");
			}
		}
		catch (Exception e)
		{
			_logger.LogError(e, "Error processing data");
		}
		finally
		{
			if (loc)
				_dbLockService.ReleaseLock();
		}

		var time = stopwatch.ElapsedMilliseconds;
		_logger.LogInformation("request: {exist} exist, {new} new, {post} from post, index1 {index1} index2 {index2} time {time}", totals.ExistPaths, totals.NewPaths, totals.PostProcessed, totals.WasInIndex1, totals.WasInIndex2, time);
		return success;
	}

	public async Task<StatsData> GetStatsAsync()
	{
		var stopwatch = Stopwatch.StartNew();
		var pathsByIndex =
			await _db.Paths
				.AsNoTracking()
				.IgnoreAutoIncludes()
				.GroupBy(p => p.IndexId)
				.Select(g => new
				{
					Index = g.Key,
					Total = g.Count(),
					Count = g.Count(pe => pe.Path != null)
				})
				.ToDictionaryAsync(g => g.Index,
					g =>
						new IndexStats
						{
							TotalPaths = (uint)g.Total,
							Paths = (uint)g.Count,
						});
		var ret = new StatsData
		{
			Totals = pathsByIndex
		};
		_logger.LogInformation("pathsByIndex took {time}ms", stopwatch.ElapsedMilliseconds);
		stopwatch.Restart();

		var pathsByIndexLatest =
			await _db.Paths
				.AsNoTracking()
				.Join(_db.LatestIndexes.AsNoTracking(),
					p => p.IndexId,
					i => i.IndexId,
					(p, i) => new
					{
						PathEntry = p,
						LatestIndex = i
					})
				.Where(p => p.PathEntry.LastSeen.Id == p.LatestIndex.GameVersion.Id)
				.GroupBy(p => p.PathEntry.IndexId)
				.Select(g =>
					new
					{
						Index = g.Key,
						Total = g.Count(),
						Count = g.Count(p => p.PathEntry.Path != null),
					})
				.ToDictionaryAsync(g => g.Index,
					g =>
						new IndexStats
						{
							TotalPaths = (uint)g.Total,
							Paths = (uint)g.Count,
						});
		_logger.LogInformation("pathsByIndexLatestQuery took {time}ms", stopwatch.ElapsedMilliseconds);
		ret.Possible = pathsByIndexLatest;

		return ret;
	}
}