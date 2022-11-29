using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ResLogger2.Common.Api;
using ResLogger2.Common.LocalDatabase.Model;

namespace ResLogger2.Common.LocalDatabase;

public class LocalHashDatabase : DbContext
{
	public DbSet<LocalPathEntry> PathEntries { get; set; }
	// public DbSet<DbInfo> DbInfos { get; set; }
	
	// private Dictionary<uint, Dictionary<uint, HashSet<uint>>> _hashesByFolderPath;
	// private Dictionary<uint, HashSet<uint>> _hashesByFullPath;

	private readonly string _dbPath;

	public static LocalHashDatabase Create() => new LocalHashDatabase("hashdb.db");
	
	public LocalHashDatabase() { }

	public LocalHashDatabase(string loc)
	{
		_dbPath = loc;
		Database.EnsureCreated();
		
		// _hashesByFolderPath = new Dictionary<uint, Dictionary<uint, HashSet<uint>>>();
		// _hashesByFullPath = new Dictionary<uint, HashSet<uint>>();
		//
		// foreach (var pathEntry in PathEntries.AsNoTracking().ToList())
		// {
		// 	var indexId = pathEntry.IndexId;
		// 	
		// 	if (!_hashesByFolderPath.ContainsKey(indexId))
		// 		_hashesByFolderPath.Add(indexId, new Dictionary<uint, HashSet<uint>>());
		// 	
		// 	if (!_hashesByFullPath.ContainsKey(indexId))
		// 		_hashesByFullPath.Add(indexId, new HashSet<uint>());
		// 	
		// 	var folderHash = pathEntry.FolderHash;
		// 	var fileHash = pathEntry.FileHash;
		// 	var fullHash = pathEntry.FullHash;
  //               
		// 	if (!_hashesByFolderPath[indexId].TryGetValue(folderHash, out var fileIndexes))
		// 		_hashesByFolderPath[indexId].Add(folderHash, new HashSet<uint> { fileHash});
		// 	else
		// 		fileIndexes.Add(fileHash);
  //           
		// 	_hashesByFullPath[indexId].Add(fullHash);
		// }
	}

	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
	{
		optionsBuilder.UseSqlite(@$"Data Source={_dbPath}");
		// optionsBuilder.UseLoggerFactory(LoggerFactory.Create(builder => builder.AddConsole()));
	}
	
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<LocalPathEntry>()
			.HasKey(p => new { p.IndexId, p.FolderHash, p.FileHash, p.FullHash });

		modelBuilder.Entity<DbInfo>()
			.HasKey(p => p.Key);
		modelBuilder.Entity<DbInfo>()
			.Property(p => p.Key)
			.ValueGeneratedNever();
	}

	public static void Release()
	{
		SqliteConnection.ClearAllPools();
		GC.WaitForPendingFinalizers();
	}

	public async Task SetUploadStateAll(bool state)
	{
		foreach (var pathEntry in PathEntries.Where(p => p.Uploaded != state))
			pathEntry.Uploaded = state;
		await SaveChangesAsync();
	}

	public async Task<UploadedDbData> GetUploadableData(int limit)
	{
		var data = await PathEntries.AsNoTracking().Where(p => p.Uploaded == false).Select(p => p.Path).Take(limit).ToListAsync();
		return new UploadedDbData(data);
	}

	public async Task SetDataUploaded(UploadedDbData data)
	{
		foreach (var entry in data.Entries)
		{
			var pathEntry = await PathEntries.FirstOrDefaultAsync(p => p.Path == entry);
			if (pathEntry != null)
				pathEntry.Uploaded = true;
		}
		await SaveChangesAsync();
	}

	public async Task AddFullPathRange(List<ExistsResult> existsResult)
	{
		foreach (var result in existsResult)
		{
			await AddFullPathWithoutSaving(result);
		}
		await SaveChangesAsync();
	}

	public async Task AddFullPath(ExistsResult existsResult)
	{
		if (!existsResult.Exists) return;
		await AddFullPathWithoutSaving(existsResult);
	}

	private async Task AddFullPathWithoutSaving(ExistsResult existsResult)
	{
		if (!existsResult.Exists) return;

		// if (!_hashesByFullPath.TryGetValue(existsResult.IndexId, out var fullHashes))
		// 	_hashesByFullPath.Add(existsResult.IndexId, new HashSet<uint> { existsResult.FullHash });
		// else
		// {
		// 	if (fullHashes.Contains(existsResult.FullHash))
		// 		return;
		// 	fullHashes.Add(existsResult.FullHash);
		// }
		//
		// if (!_hashesByFolderPath.ContainsKey(existsResult.IndexId))
		// 	_hashesByFolderPath.Add(existsResult.IndexId, new Dictionary<uint, HashSet<uint>>());
		// if (!_hashesByFolderPath[existsResult.IndexId].TryGetValue(existsResult.FolderHash, out var fileHashes))
		// 	_hashesByFolderPath[existsResult.IndexId].Add(existsResult.FolderHash, new HashSet<uint> { existsResult.FileHash });
		// else
		// {
		// 	if (fileHashes.Contains(existsResult.FileHash))
		// 		return;
		// 	fileHashes.Add(existsResult.FileHash);
		// }

		var find = await PathEntries.AsNoTracking().AnyAsync(p => p.IndexId == existsResult.IndexId && p.FolderHash == existsResult.FolderHash && p.FileHash == existsResult.FileHash && p.FullHash == existsResult.FullHash);
		if (find)
			return;
		
		PathEntries.Add(new LocalPathEntry
		{
			IndexId = existsResult.IndexId,
			FolderHash = existsResult.FolderHash,
			FileHash = existsResult.FileHash,
			FullHash = existsResult.FullHash,
			Path = existsResult.FullText,
			Uploaded = false,
		});
	}

	public async Task<List<string>> GetPathList()
	{
		return await PathEntries.AsNoTracking().Select(p => p.Path).ToListAsync();
	}
}