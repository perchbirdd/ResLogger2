namespace ResLogger2.Common.ServerDatabase.Model;

public class PathCache
{
	public Dictionary<uint, Dictionary<uint, Dictionary<uint, Dictionary<uint, PathEntry>>>> PathEntryCacheByFolder { get; set; }
	public Dictionary<uint, Dictionary<uint, HashSet<PathEntry>>> PathEntryCacheByFull { get; set; }
	public Dictionary<uint, Dictionary<uint, Dictionary<uint, Index1StagingEntry>>> Index1StagingEntryCache { get; set; }
	public Dictionary<uint, Dictionary<uint, Index2StagingEntry>> Index2StagingEntryCache { get; set; }
	public Dictionary<long, GameVersion> GameVersionCache { get; set; }
	public Dictionary<uint, LatestIndex> LatestGameVersionIndexCache { get; set; }
	public Dictionary<string, LatestProcessedVersion> LatestProcessedVersionCache { get; set; }

	private readonly ServerHashDatabase _db;

	public PathCache(ServerHashDatabase db)
	{
		_db = db;
		
		Clear();
		Load();
	}

	public void Reload()
	{
		Clear();
		Load();
	}
	
	public void Clear()
	{
		PathEntryCacheByFolder = new Dictionary<uint, Dictionary<uint, Dictionary<uint, Dictionary<uint, PathEntry>>>>();
		PathEntryCacheByFull = new Dictionary<uint, Dictionary<uint, HashSet<PathEntry>>>();
		Index1StagingEntryCache = new Dictionary<uint, Dictionary<uint, Dictionary<uint, Index1StagingEntry>>>();
		Index2StagingEntryCache = new Dictionary<uint, Dictionary<uint, Index2StagingEntry>>();
		GameVersionCache = new Dictionary<long, GameVersion>();
		LatestGameVersionIndexCache = new Dictionary<uint, LatestIndex>();
		LatestProcessedVersionCache = new Dictionary<string, LatestProcessedVersion>();
	}

	public void Load()
	{
		foreach (var pathEntry in _db.Paths)
			Add(pathEntry, false);

		foreach (var index1StagingEntry in _db.Index1StagingEntries)
			Add(index1StagingEntry, false);

		foreach (var index2StagingEntry in _db.Index2StagingEntries)
			Add(index2StagingEntry, false);

		foreach (var gameVersion in _db.GameVersions)
			Add(gameVersion, false);
		
		foreach (var latestGameVersionIndex in _db.LatestIndexes)
			Add(latestGameVersionIndex, false);

		foreach (var latestProcessedVersion in _db.LatestProcessedVersions)
			Add(latestProcessedVersion, false);
	}

	public void Persist(ServerHashDatabase db)
	{
		var pathEntries = new List<PathEntry>();
		var index1StagingEntries = new List<Index1StagingEntry>();
		var index2StagingEntries = new List<Index2StagingEntry>();
		var latestGameVersions = new List<LatestIndex>();
		var latestProcessedVersions = new List<LatestProcessedVersion>();

		foreach (var pathEntry in PathEntryCacheByFolder.Values)
			foreach (var pathEntryValue in pathEntry.Values)
				foreach (var pathEntryValueValue in pathEntryValue.Values)
					foreach (var value in pathEntryValueValue.Values)
						pathEntries.Add(value);
		
		foreach (var index1StagingEntry in Index1StagingEntryCache.Values)
			foreach (var index1StagingEntryValue in index1StagingEntry.Values)
				foreach (var index1StagingEntryValueValue in index1StagingEntryValue.Values)
					index1StagingEntries.Add(index1StagingEntryValueValue);

		foreach (var index2StagingEntry in Index2StagingEntryCache.Values)
			foreach (var index2StagingEntryValue in index2StagingEntry.Values)
				index2StagingEntries.Add(index2StagingEntryValue);
		
		foreach (var latestGameVersionIndex in LatestGameVersionIndexCache.Values)
			latestGameVersions.Add(latestGameVersionIndex);

		foreach (var latestProcessedVersion in LatestProcessedVersionCache.Values)
			latestProcessedVersions.Add(latestProcessedVersion);

		db.Paths.AddRange(pathEntries);
		db.Index1StagingEntries.AddRange(index1StagingEntries);
		db.Index2StagingEntries.AddRange(index2StagingEntries);
		db.LatestIndexes.AddRange(latestGameVersions);
		db.LatestProcessedVersions.AddRange(latestProcessedVersions);
		db.SaveChanges();
	}

	public void Add(PathEntry pathEntry, bool db = true)
	{
		if (PathEntryCacheByFolder.TryGetValue(pathEntry.IndexId, out var pathEntryIndex))
		{
			if (pathEntryIndex.TryGetValue(pathEntry.FolderHash, out var pathEntryFolder))
			{
				if (pathEntryFolder.TryGetValue(pathEntry.FileHash, out var pathEntryFile))
				{
					if (pathEntryFile.ContainsKey(pathEntry.FullHash))
						throw new Exception($"Duplicate path entry: {pathEntry}");
					pathEntryFile[pathEntry.FullHash] = pathEntry;
				}
				else
				{
					pathEntryFolder[pathEntry.FileHash] = new Dictionary<uint, PathEntry> { { pathEntry.FullHash, pathEntry } };
				}
			}
			else
			{
				pathEntryIndex[pathEntry.FolderHash] = new Dictionary<uint, Dictionary<uint, PathEntry>> { { pathEntry.FileHash, new Dictionary<uint, PathEntry> { { pathEntry.FullHash, pathEntry } } } };
			}
		}
		else
		{
			PathEntryCacheByFolder[pathEntry.IndexId] = new Dictionary<uint, Dictionary<uint, Dictionary<uint, PathEntry>>> { { pathEntry.FolderHash, new Dictionary<uint, Dictionary<uint, PathEntry>> { { pathEntry.FileHash, new Dictionary<uint, PathEntry> { { pathEntry.FullHash, pathEntry } } } } } };
		}
		
		if (PathEntryCacheByFull.TryGetValue(pathEntry.IndexId, out var pathEntryIndex2))
		{
			if (pathEntryIndex2.TryGetValue(pathEntry.FullHash, out var pathEntryFull))
			{
				pathEntryFull.Add(pathEntry);
			}
			else
			{
				pathEntryIndex2[pathEntry.FullHash] = new HashSet<PathEntry> { pathEntry };
			}
		}
		else
		{
			PathEntryCacheByFull[pathEntry.IndexId] = new Dictionary<uint, HashSet<PathEntry>> { { pathEntry.FullHash, new HashSet<PathEntry> { pathEntry } } };
		}
		
		if (db)
			_db.Paths.Add(pathEntry);
	}

	public void Remove(PathEntry pathEntry, bool db = true)
	{
		if (PathEntryCacheByFolder.TryGetValue(pathEntry.IndexId, out var pathEntryIndex))
			if (pathEntryIndex.TryGetValue(pathEntry.FolderHash, out var pathEntryFolder))
				if (pathEntryFolder.TryGetValue(pathEntry.FileHash, out var pathEntryFile))
				{
					pathEntryFile.Remove(pathEntry.FullHash);
					if (db)
						_db.Paths.Remove(pathEntry);
				}
					
	}
	
	public void Add(Index1StagingEntry index1StagingEntry, bool db = true)
	{
		if (Index1StagingEntryCache.TryGetValue(index1StagingEntry.IndexId, out var index1StagingEntryIndex))
		{
			if (index1StagingEntryIndex.TryGetValue(index1StagingEntry.FolderHash, out var index1StagingEntryFolder))
			{
				if (index1StagingEntryFolder.ContainsKey(index1StagingEntry.FileHash))
					throw new Exception($"Duplicate index1 staging entry: {index1StagingEntry}");
				index1StagingEntryFolder[index1StagingEntry.FileHash] = index1StagingEntry;
			}
			else
			{
				index1StagingEntryIndex[index1StagingEntry.FolderHash] = new Dictionary<uint, Index1StagingEntry> { { index1StagingEntry.FileHash, index1StagingEntry } };
			}
		}
		else
		{
			Index1StagingEntryCache[index1StagingEntry.IndexId] = new Dictionary<uint, Dictionary<uint, Index1StagingEntry>> { { index1StagingEntry.FolderHash, new Dictionary<uint, Index1StagingEntry> { { index1StagingEntry.FileHash, index1StagingEntry } } } };
		}
		if (db)
			_db.Index1StagingEntries.Add(index1StagingEntry);
	}
	
	public void Remove(Index1StagingEntry index1StagingEntry, bool db = true)
	{
		if (Index1StagingEntryCache.TryGetValue(index1StagingEntry.IndexId, out var index1StagingEntryIndex))
		{
			if (index1StagingEntryIndex.TryGetValue(index1StagingEntry.FolderHash, out var index1StagingEntryFolder))
			{
				index1StagingEntryFolder.Remove(index1StagingEntry.FileHash);
				if (db)
					_db.Index1StagingEntries.Remove(index1StagingEntry);
			}
		}
	}
	
	public void Add(Index2StagingEntry index2StagingEntry, bool db = true)
	{
		if (Index2StagingEntryCache.TryGetValue(index2StagingEntry.IndexId, out var index2StagingEntryIndex))
		{
			if (index2StagingEntryIndex.ContainsKey(index2StagingEntry.FullHash))
				throw new Exception($"Duplicate index2 staging entry: {index2StagingEntry}");
			index2StagingEntryIndex[index2StagingEntry.FullHash] = index2StagingEntry;
		}
		else
		{
			Index2StagingEntryCache[index2StagingEntry.IndexId] = new Dictionary<uint, Index2StagingEntry> { { index2StagingEntry.FullHash, index2StagingEntry } };
		}
		if (db)
			_db.Index2StagingEntries.Add(index2StagingEntry);
	}
	
	public void Remove(Index2StagingEntry index2StagingEntry, bool db = true)
	{
		if (Index2StagingEntryCache.TryGetValue(index2StagingEntry.IndexId, out var index2StagingEntryIndex))
		{
			index2StagingEntryIndex.Remove(index2StagingEntry.FullHash);
			if (db)
				_db.Index2StagingEntries.Remove(index2StagingEntry);
		}
	}

	public void Add(GameVersion gameVersion, bool db = true)
	{
		if (GameVersionCache.ContainsKey(gameVersion.Id))
			throw new Exception($"Duplicate game version: {gameVersion}");
		GameVersionCache.Add(gameVersion.Id, gameVersion);
		if (db)
			_db.GameVersions.Add(gameVersion);
	}

	public void Add(LatestIndex lgvi, bool db = true)
	{
		if (LatestGameVersionIndexCache.ContainsKey(lgvi.IndexId))
			throw new Exception($"Duplicate latest game version index: {lgvi}");
		LatestGameVersionIndexCache.Add(lgvi.IndexId, lgvi);
		if (db)
			_db.LatestIndexes.Add(lgvi);
	}
	
	public void Add(LatestProcessedVersion lpv, bool db = true)
	{
		if (LatestProcessedVersionCache.ContainsKey(lpv.Repo))
			throw new Exception($"Duplicate latest processed version: {lpv}");
		LatestProcessedVersionCache.Add(lpv.Repo, lpv);
		if (db)
			_db.LatestProcessedVersions.Add(lpv);
	}

	public PathEntry? FindPath(uint indexId, uint folderHash, uint fileHash, uint fullHash)
	{
		if (PathEntryCacheByFolder.TryGetValue(indexId, out var pathEntryIndex))
			if (pathEntryIndex.TryGetValue(folderHash, out var pathEntryFolder))
				if (pathEntryFolder.TryGetValue(fileHash, out var pathEntryFile))
					if (pathEntryFile.TryGetValue(fullHash, out var pathEntry))
						return pathEntry;
		return null;
	}
	
	public PathEntry? FindPath(uint indexId, uint folderHash, uint fileHash)
	{
		if (PathEntryCacheByFolder.TryGetValue(indexId, out var pathEntryIndex))
			if (pathEntryIndex.TryGetValue(folderHash, out var pathEntryFolder))
				if (pathEntryFolder.TryGetValue(fileHash, out var pathEntryFile))
					return pathEntryFile.Values.FirstOrDefault();
		return null;
	}
	
	public PathEntry? FindPath(uint indexId, uint fullHash)
	{
		if (PathEntryCacheByFull.TryGetValue(indexId, out var pathEntryIndex))
			if (pathEntryIndex.TryGetValue(fullHash, out var pathEntry))
				return pathEntry.First();
		return null;
	}
	
	public Index1StagingEntry? FindIndex1(uint indexId, uint folderHash, uint fileHash)
	{
		if (Index1StagingEntryCache.TryGetValue(indexId, out var index1StagingEntryIndex))
			if (index1StagingEntryIndex.TryGetValue(folderHash, out var index1StagingEntryFolder))
				if (index1StagingEntryFolder.TryGetValue(fileHash, out var index1StagingEntry))
					return index1StagingEntry;
		return null;
	}
	
	public Index2StagingEntry? FindIndex2(uint indexId, uint fullHash)
	{
		if (Index2StagingEntryCache.TryGetValue(indexId, out var index2StagingEntryIndex))
			if (index2StagingEntryIndex.TryGetValue(fullHash, out var index2StagingEntry))
				return index2StagingEntry;
		
		return null;
	}
	
	public LatestIndex? FindLatestGameVersionIndex(uint indexId)
	{
		return LatestGameVersionIndexCache.TryGetValue(indexId, out var lgvi) ? lgvi : null;
	}
	
	public LatestProcessedVersion? FindLatestProcessedVersion(string repo)
	{
		return LatestProcessedVersionCache.TryGetValue(repo, out var lpv) ? lpv : null;
	}
}