using Microsoft.EntityFrameworkCore;
using ResLogger2.Common.ServerDatabase.Model;

namespace ResLogger2.Common.ServerDatabase;

public class ServerHashDatabase : DbContext
{
	public DbSet<PathEntry> Paths { get; set; }
	public DbSet<Index1StagingEntry> Index1StagingEntries { get; set; }
	public DbSet<Index2StagingEntry> Index2StagingEntries { get; set; }
	public DbSet<GameVersion> GameVersions { get; set; }
	public DbSet<LatestIndex> LatestIndexes { get; set; }
	public DbSet<LatestProcessedVersion> LatestProcessedVersions { get; set; }

	private PathCache? _cache;

	public ServerHashDatabase()
	{
		Database.EnsureCreated();
		// _cache = new PathCache(this);
	}
	
	public ServerHashDatabase(DbContextOptions<ServerHashDatabase> options) : base(options)
	{
		Database.EnsureCreated();
		// _cache = new PathCache(this);
	}

	public override void Dispose()
	{
		// var connection = Database.GetDbConnection();
		base.Dispose();
		// SqliteConnectionPool.ReleaseConnection(connection);
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<PathEntry>()
			.HasKey(p => new { p.IndexId, p.FolderHash, p.FileHash, p.FullHash });
		modelBuilder.Entity<PathEntry>()
			.HasIndex(p => new { p.IndexId, p.FolderHash, p.FileHash, p.FullHash });
		modelBuilder.Entity<PathEntry>()
			.HasIndex(p => new { p.IndexId, p.FolderHash, p.FileHash });
		modelBuilder.Entity<PathEntry>()
			.HasIndex(p => new { p.IndexId, p.FullHash });
		modelBuilder.Entity<PathEntry>()
			.HasIndex(p => p.IndexId);
		modelBuilder.Entity<PathEntry>()
			.HasIndex(p => p.Path);

		modelBuilder.Entity<Index1StagingEntry>()
			.HasKey(p => new { p.IndexId, p.FolderHash, p.FileHash });
		modelBuilder.Entity<Index1StagingEntry>()
			.HasIndex(p => new { p.IndexId, p.FolderHash, p.FileHash });

		modelBuilder.Entity<Index2StagingEntry>()
			.HasKey(p => new { p.IndexId, p.FullHash });
		modelBuilder.Entity<Index2StagingEntry>()
			.HasIndex(p => new { p.IndexId, p.FullHash });

		modelBuilder.Entity<GameVersion>()
			.HasKey(p => p.Id);
		modelBuilder.Entity<GameVersion>()
			.HasIndex(p => p.Id);
		
		modelBuilder.Entity<LatestIndex>()
			.HasKey(p => p.IndexId);
		modelBuilder.Entity<LatestIndex>()
			.Property(p => p.IndexId)
			.ValueGeneratedNever();

		modelBuilder.Entity<LatestProcessedVersion>()
			.HasKey(p => p.Repo);
		modelBuilder.Entity<LatestProcessedVersion>()
			.Property(p => p.Repo)
			.ValueGeneratedNever();
	}

	public void ReloadCache()
	{
		_cache ??= new PathCache(this);
		_cache.Reload();
	}
	
	private void HandlePathEntry(GameVersion gv, uint indexId, CompositeIndexInfo.CombinedIndexEntry combinedEntry)
	{
		var pathQuery =
			Paths
				.Include(p => p.FirstSeen)
				.Include(p => p.LastSeen)
				.FirstOrDefault(p =>
					p.IndexId == indexId &&
					p.FolderHash == combinedEntry.FolderHash &&
					p.FileHash == combinedEntry.FileHash &&
					p.FullHash == combinedEntry.FullHash);

		if (pathQuery != null)
		{
			if (pathQuery.Path == null && combinedEntry.Path != null)
				pathQuery.Path = combinedEntry.Path;
			pathQuery.UpdateSeen(gv);
			return;
		}
		
		var index1Query =
			Index1StagingEntries
				.Include(p => p.FirstSeen)
				.Include(p => p.LastSeen)
				.FirstOrDefault(p => 
					p.IndexId == indexId &&
					p.FolderHash == combinedEntry.FolderHash &&
					p.FileHash == combinedEntry.FileHash);
		var index2Query =
			Index2StagingEntries
				.Include(p => p.FirstSeen)
				.Include(p => p.LastSeen)
				.FirstOrDefault(p =>
					p.IndexId == indexId &&
					p.FullHash == combinedEntry.FullHash);

		var earliestFirstSeen = gv;
		var latestLastSeen = gv;
				
		if (index1Query != null)
		{
			if (index1Query.FirstSeen < earliestFirstSeen)
				earliestFirstSeen = index1Query.FirstSeen;
			if (index1Query.LastSeen > latestLastSeen)
				latestLastSeen = index1Query.LastSeen;
			Index1StagingEntries.Remove(index1Query);
		}
		
		if (index2Query != null)
		{
			if (index2Query.FirstSeen < earliestFirstSeen)
				earliestFirstSeen = index2Query.FirstSeen;
			if (index2Query.LastSeen > latestLastSeen)
				latestLastSeen = index2Query.LastSeen;
			Index2StagingEntries.Remove(index2Query);
		}
				
		var entry = new PathEntry
		{
			IndexId = indexId,
			FolderHash = combinedEntry.FolderHash,
			FileHash = combinedEntry.FileHash,
			FullHash = combinedEntry.FullHash,
			Path = combinedEntry.Path,
			FirstSeen = earliestFirstSeen,
			LastSeen = latestLastSeen,
		};
				
		Paths.Add(entry);
	}

	private void HandleIndex1Entry(GameVersion gv, uint indexId, CompositeIndexInfo.BasicIndex1Entry entry)
	{
		var pathQuery =
			Paths
				.Include(p => p.FirstSeen)
				.Include(p => p.LastSeen)
				.FirstOrDefault(p =>
					p.IndexId == indexId &&
					p.FolderHash == entry.FolderHash &&
					p.FileHash == entry.FileHash);

		if (pathQuery != null)
		{
			pathQuery.UpdateSeen(gv);
			return;
		}
		
		var index1Query =
			Index1StagingEntries
				.Include(p => p.FirstSeen)
				.Include(p => p.LastSeen)
				.FirstOrDefault(p => 
					p.IndexId == indexId &&
					p.FolderHash == entry.FolderHash &&
					p.FileHash == entry.FileHash);

		if (index1Query != null)
		{
			index1Query.UpdateSeen(gv);
		}
		else
		{
			var newEntry = new Index1StagingEntry
			{
				IndexId = indexId,
				FolderHash = entry.FolderHash,
				FileHash = entry.FileHash,
				FirstSeen = gv,
				LastSeen = gv,
			};
			
			Index1StagingEntries.Add(newEntry);
		}
	}
	
	private void HandleIndex2Entry(GameVersion gv, uint indexId, CompositeIndexInfo.BasicIndex2Entry entry)
	{
		var pathQuery =
			Paths
				.Include(p => p.FirstSeen)
				.Include(p => p.LastSeen)
				.FirstOrDefault(p =>
					p.IndexId == indexId &&
					p.FullHash == entry.FullHash);

		if (pathQuery != null)
		{
			pathQuery.UpdateSeen(gv);
			return;
		}
		
		var index2Query =
			Index2StagingEntries
				.Include(p => p.FirstSeen)
				.Include(p => p.LastSeen)
				.FirstOrDefault(p => 
					p.IndexId == indexId &&
					p.FullHash == entry.FullHash);

		if (index2Query != null)
		{
			index2Query.UpdateSeen(gv);
		}
		else
		{
			var newEntry = new Index2StagingEntry
			{
				IndexId = indexId,
				FullHash = entry.FullHash,
				FirstSeen = gv,
				LastSeen = gv,
			};
			
			Index2StagingEntries.Add(newEntry);
		}
	}

	public void ProcessInMemory(PatchIndexHolder holder)
	{
		var gameVersion = holder.GameVersion;

		var thisRepo = holder.Repo;
		var repo = _cache.FindLatestProcessedVersion(thisRepo);
		if (repo == null)
		{
			_cache.Add(new LatestProcessedVersion
			{
				Repo = thisRepo,
				Version = gameVersion,
			});
		}
		else
		{
			if (repo.Version < gameVersion)
				repo.Version = gameVersion;
		}

		foreach (var compositeIndexInfo in holder.Indexes)
		{
			var indexId = compositeIndexInfo.IndexId;

			var lgvi = _cache.FindLatestGameVersionIndex(indexId);
			if (lgvi == null)
				_cache.Add(new LatestIndex {IndexId = indexId, GameVersion = gameVersion});
			else
			{
				if (lgvi.GameVersion < gameVersion)
					lgvi.GameVersion = gameVersion;
			}
			
			foreach (var combinedEntry in compositeIndexInfo.CombinedIndexEntries.Values)
			{
				var pathQuery = _cache.FindPath(indexId, combinedEntry.FolderHash, combinedEntry.FileHash, combinedEntry.FullHash);

				if (pathQuery != null)
				{
					if (pathQuery.Path == null && combinedEntry.Path != null)
						pathQuery.Path = combinedEntry.Path;
					pathQuery.UpdateSeen(gameVersion);
					continue;
				}
	
				var index1Query = _cache.FindIndex1(indexId, combinedEntry.FolderHash, combinedEntry.FileHash);
				var index2Query = _cache.FindIndex2(indexId, combinedEntry.FullHash);

				var earliestFirstSeen = gameVersion;
				var latestLastSeen = gameVersion;
			
				if (index1Query != null)
				{
					if (index1Query.FirstSeen < earliestFirstSeen)
						earliestFirstSeen = index1Query.FirstSeen;
					if (index1Query.LastSeen > latestLastSeen)
						latestLastSeen = index1Query.LastSeen;
					_cache.Remove(index1Query);
				}
	
				if (index2Query != null)
				{
					if (index2Query.FirstSeen < earliestFirstSeen)
						earliestFirstSeen = index2Query.FirstSeen;
					if (index2Query.LastSeen > latestLastSeen)
						latestLastSeen = index2Query.LastSeen;
					_cache.Remove(index2Query);
				}
			
				var entry = new PathEntry
				{
					IndexId = indexId,
					FolderHash = combinedEntry.FolderHash,
					FileHash = combinedEntry.FileHash,
					FullHash = combinedEntry.FullHash,
					Path = combinedEntry.Path,
					FirstSeen = earliestFirstSeen,
					LastSeen = latestLastSeen,
				};
			
				_cache.Add(entry);
			}

			foreach (var index1Entry in compositeIndexInfo.Index1Entries.Values)
			{
				var pathQuery = _cache.FindPath(indexId, index1Entry.FolderHash, index1Entry.FileHash);

				if (pathQuery != null)
				{
					pathQuery.UpdateSeen(gameVersion);
					continue;
				}
				
				var index1Query = _cache.FindIndex1(indexId, index1Entry.FolderHash, index1Entry.FileHash);

				if (index1Query != null)
				{
					index1Query.UpdateSeen(gameVersion);
				}
				else
				{
					var newEntry = new Index1StagingEntry
					{
						IndexId = indexId,
						FolderHash = index1Entry.FolderHash,
						FileHash = index1Entry.FileHash,
						FirstSeen = gameVersion,
						LastSeen = gameVersion,
					};
		
					_cache.Add(newEntry);
				}
			}
		
			foreach (var index2Entry in compositeIndexInfo.Index2Entries.Values)
			{
				var pathQuery = _cache.FindPath(indexId, index2Entry.FullHash);

				if (pathQuery != null)
				{
					pathQuery.UpdateSeen(gameVersion);
					continue;
				}
				
				var index2Query = _cache.FindIndex2(indexId, index2Entry.FullHash);

				if (index2Query != null)
				{
					index2Query.UpdateSeen(gameVersion);
				}
				else
				{
					var newEntry = new Index2StagingEntry
					{
						IndexId = indexId,
						FullHash = index2Entry.FullHash,
						FirstSeen = gameVersion,
						LastSeen = gameVersion,
					};
		
					_cache.Add(newEntry);
				}
			}
		}
	}
	
	public void ProcessInMemory(List<PatchIndexHolder> holders)
	{
		foreach (var holder in holders)
		{
			
		}

		SaveChanges();
	}

	private void ProcessInternal(CompositeIndexInfo info, GameVersion gv)
	{
		foreach (var combinedEntry in info.CombinedIndexEntries.Values)
		{
			HandlePathEntry(gv, info.IndexId, combinedEntry);
		}
		SaveChanges();

		foreach (var index1Entry in info.Index1Entries.Values)
		{
			HandleIndex1Entry(gv, info.IndexId, index1Entry);
		}
		SaveChanges();
		
		foreach (var index2Entry in info.Index2Entries.Values)
		{
			HandleIndex2Entry(gv, info.IndexId, index2Entry);
		}
		SaveChanges();
	}

	public void Process(PatchIndexHolder holder)
	{
		var thisGameVersion = holder.GameVersion;

		var thisRepo = holder.Repo;

		var latests = LatestProcessedVersions.ToList().ToDictionary(lpv => lpv.Repo, lpv => lpv);
		
		if (latests.TryGetValue(thisRepo, out var latest))
		{
			if (latest.Version < thisGameVersion)
				latest.Version = thisGameVersion;
		}
		else
		{
			LatestProcessedVersions.Add(new LatestProcessedVersion
			{
				Repo = thisRepo,
				Version = thisGameVersion,
			});
		}

		var latestVersionDict = LatestIndexes.ToList().ToDictionary(gvi => gvi.IndexId, gvi => gvi);

		foreach (var comp in holder.Indexes)
		{
			ProcessInternal(comp, thisGameVersion);

			if (latestVersionDict.TryGetValue(comp.IndexId, out var lgvi))
			{
				if (lgvi.GameVersion >= thisGameVersion)
					continue;
				lgvi.GameVersion = thisGameVersion;
			}
			else
			{
				var newLgvi = new LatestIndex
				{
					GameVersion = thisGameVersion,
					IndexId = comp.IndexId,
				};
				latestVersionDict[comp.IndexId] = newLgvi;
				LatestIndexes.Add(newLgvi);
			}
			
			SaveChanges();
		}
	}

	public void Process(List<PatchIndexHolder> holders)
	{
		foreach (var holder in holders)
		{
			Process(holder);
		}
	}
}
