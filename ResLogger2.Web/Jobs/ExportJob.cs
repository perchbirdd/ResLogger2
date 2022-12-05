using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Quartz;
using ResLogger2.Common;
using ResLogger2.Common.ServerDatabase;
using FileInfo = System.IO.FileInfo;

namespace ResLogger2.Web.Jobs;

[DisallowConcurrentExecution]
public class ExportJob : IJob
{
	private readonly ILogger<ExportJob> _logger;
	private readonly ServerHashDatabase _db;
	private readonly DirectoryInfo _exportDirectory;
	private readonly DirectoryInfo _backupDirectory;

	private const string PathListFileNameBase = "PathList";
	private const string PathListWithHashesFileNameBase = "PathListWithHashes";
	private const string CurrentPathListFileNameBase = "CurrentPathList";
	private const string CurrentPathListWithHashesFileNameBase = "CurrentPathListWithHashes";

	private const string PathListTextFileExtension = ".txt";
	private const string PathListTextFile = PathListFileNameBase + PathListTextFileExtension;
	private const string CurrentPathListTextFile = CurrentPathListFileNameBase + PathListTextFileExtension;
	
	private const string PathListCsvFileExtension = ".csv";
	private const string PathListWithHashesCsvFile = PathListWithHashesFileNameBase + PathListCsvFileExtension;
	private const string CurrentPathListWithHashesCsvFile = CurrentPathListWithHashesFileNameBase + PathListCsvFileExtension;
	
	private const string PathListCompressedFileExtension = ".gz";
	private const string PathListCompressedFile = PathListFileNameBase + PathListCompressedFileExtension;
	private const string PathListWithHashesCompressedFile = PathListWithHashesFileNameBase + PathListCompressedFileExtension;
	private const string CurrentPathListCompressedFile = CurrentPathListFileNameBase + PathListCompressedFileExtension;
	private const string CurrentPathListWithHashesCompressedFile = CurrentPathListWithHashesFileNameBase + PathListCompressedFileExtension;
	
	private const string PathListZipFileExtension = ".zip";
	private const string PathListZipFile = PathListFileNameBase + PathListZipFileExtension;
	private const string PathListWithHashesZipFile = PathListWithHashesFileNameBase + PathListZipFileExtension;
	private const string CurrentPathListZipFile = CurrentPathListFileNameBase + PathListZipFileExtension;
	private const string CurrentPathListWithHashesZipFile = CurrentPathListWithHashesFileNameBase + PathListZipFileExtension;

	public ExportJob(IConfiguration configuration, ILogger<ExportJob> logger, ServerHashDatabase db)
	{
		_logger = logger;
		_db = db;
		
		var exportBase = configuration["ExportDirectory"];
		if (exportBase == null)
			throw new Exception("ExportDirectory configuration key must be specified. Export/backup job will not run.");
		_exportDirectory = new DirectoryInfo(Path.Combine(exportBase, "export"));
		if (!_exportDirectory.Exists)
			_exportDirectory.Create();
		var backupBase = configuration["BackupDirectory"];
		if (backupBase == null)
			throw new Exception("BackupDirectory configuration key must be specified. Export/backup job will not run.");
		_backupDirectory = new DirectoryInfo(Path.Combine(backupBase, "backup"));
		if (!_backupDirectory.Exists)
			_backupDirectory.Create();
	}

	public async Task Execute(IJobExecutionContext context)
	{
		_logger.LogInformation("ExportJob started");

		Backup();
		await Export();
		
		_logger.LogInformation("ExportJob finished");
	}
	
	private void Backup()
	{
		// We only copy away the full path list file, as that can recreate the entire db
		_logger.LogInformation("Backup started");
		
		var epoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var existingPathListFile = new FileInfo(Path.Combine(_exportDirectory.FullName, PathListCompressedFile));
		var backupFileName = $"{PathListFileNameBase}-{epoch}{PathListCompressedFileExtension}";
		var pathListBackup = new FileInfo(Path.Combine(_backupDirectory.FullName, backupFileName));
		if (existingPathListFile.Exists)
			existingPathListFile.CopyTo(pathListBackup.FullName);

		_logger.LogInformation("Backup finished");
	}

	private async Task Export()
	{
		var timer = Stopwatch.StartNew();
		_logger.LogInformation("Export started");
		
		// All paths, for all path exports
		var paths = await _db.Paths.AsNoTracking().Where(p => p.Path != null).Select(p => p.Path).ToListAsync();
		
		var pathList = string.Join('\n', paths);
		var pathListWithHashes = CreateListWithHashes(paths);
		
		// Known paths that exist only in the current version of the game
		var currentPaths =
			await _db.Paths
				.AsNoTracking()
				.IgnoreAutoIncludes()
				// .Include(p => p.LastSeen)
				.Join(_db.LatestIndexes.AsNoTracking(),
					p => p.IndexId,
					i => i.IndexId,
					(p, i) => new
					{
						PathEntry = p,
						LatestIndex = i,
					})
				.Where(p => p.PathEntry.LastSeen.Id == p.LatestIndex.GameVersion.Id && p.PathEntry.Path != null)
				.Select(p => p.PathEntry.Path)
				.ToListAsync();
		
		var currentPathList = string.Join('\n', currentPaths);
		var currentPathListWithHashes = CreateListWithHashes(currentPaths);
		_logger.LogInformation("Queries and joins for export took {time}ms", timer.ElapsedMilliseconds);

		var gzOutPath = Path.Combine(_exportDirectory.FullName, PathListCompressedFile);
		var gzHashOutPath = Path.Combine(_exportDirectory.FullName, PathListWithHashesCompressedFile);
		var zipOutPath = Path.Combine(_exportDirectory.FullName, PathListZipFile);
		var zipHashOutPath = Path.Combine(_exportDirectory.FullName, PathListWithHashesZipFile);
		var currentGzOutPath = Path.Combine(_exportDirectory.FullName, CurrentPathListCompressedFile);
		var currentGzHashOutPath = Path.Combine(_exportDirectory.FullName, CurrentPathListWithHashesCompressedFile);
		var currentZipOutPath = Path.Combine(_exportDirectory.FullName, CurrentPathListZipFile);
		var currentZipHashOutPath = Path.Combine(_exportDirectory.FullName, CurrentPathListWithHashesZipFile);

		await WriteGzAsync(gzOutPath, pathList);
		await WriteGzAsync(gzHashOutPath, pathListWithHashes);
		await WriteZipAsync(zipOutPath, PathListTextFile, pathList);
		await WriteZipAsync(zipHashOutPath, PathListWithHashesCsvFile, pathListWithHashes);
		await WriteGzAsync(currentGzOutPath, currentPathList);
		await WriteGzAsync(currentGzHashOutPath, currentPathListWithHashes);
		await WriteZipAsync(currentZipOutPath, CurrentPathListTextFile, currentPathList);
		await WriteZipAsync(currentZipHashOutPath, CurrentPathListWithHashesCsvFile, currentPathListWithHashes);
		
		_logger.LogInformation("Export finished in {time}ms", timer.ElapsedMilliseconds);
	}

	private string CreateListWithHashes(List<string> paths)
	{
		var builder = new StringBuilder();
		builder.Append("indexid,folderhash,filehash,fullhash,path\n");

		foreach (var path in paths)
		{
			var index = Utils.GetCategoryIdForPath(path);
			var hashes = Utils.CalcAllHashes(path);
			builder.Append($"{index},{hashes.folderHash},{hashes.fileHash},{hashes.fullHash},{path}\n");
		}
		return builder.ToString();
	}

	private async Task WriteGzAsync(string path, string contents)
	{
		await using var gzipStream = new GZipStream(new FileStream(path, FileMode.Create), CompressionLevel.SmallestSize);
		await using var writer = new StreamWriter(gzipStream);
		await writer.WriteAsync(contents);	
	}

	private async Task WriteZipAsync(string path, string entryPath, string contents)
	{
		await using var memoryStream = new MemoryStream();
		{
			using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true);
			var demoFile = archive.CreateEntry(entryPath);

			await using var entryStream = demoFile.Open();
			await using var streamWriter = new StreamWriter(entryStream);
			await streamWriter.WriteAsync(contents);
		}

		{
			await using var fileStream = new FileStream(path, FileMode.Create);
			memoryStream.Seek(0, SeekOrigin.Begin);
			await memoryStream.CopyToAsync(fileStream);
		}
	}
}