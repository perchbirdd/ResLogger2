using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Logging;
using Microsoft.Data.Sqlite;
using ResLogger2.Common;
using ResLogger2.Common.Api;

namespace ResLogger2.Plugin.Database;

public class LocalHashDatabase
{
	private const int Version = 3;

	private const string PathsCreateText = 
		"BEGIN TRANSACTION;" +
		"CREATE TABLE DbInfo (Key string NOT NULL, Value string NOT NULL);" +
		"CREATE TABLE Paths (IndexId INTEGER NOT NULL, FolderHash INTEGER NOT NULL, FileHash INTEGER NOT NULL, FullHash INTEGER NOT NULL, Path TEXT NOT NULL UNIQUE, Uploaded BOOLEAN NOT NULL, PRIMARY KEY (IndexId, FolderHash, FileHash, FullHash));" +
		"CREATE INDEX 'Paths_FullHash_Index' ON Paths (FullHash);" +
		"COMMIT TRANSACTION;";

	private readonly ResLogger2 _plugin;
	private readonly string _hashDbPath;
	private readonly SqliteConnection _connection;
	private readonly TaskScheduler _scheduler;
	private readonly TaskFactory _factory;
	private readonly CancellationTokenSource _tokenSource;
	private DbTransaction _transaction;

	private ConcurrentDictionary<uint, ConcurrentDictionary<uint, HashSet<uint>>> _hashesByFolderPath;
	private ConcurrentDictionary<uint, HashSet<uint>> _hashesByFullPath;

	public LocalHashDatabase(ResLogger2 plugin, string path)
	{
		_plugin = plugin;
		_hashDbPath = path;
		_scheduler = new LimitedConcurrencyLevelTaskScheduler(1);
		_factory = new TaskFactory(_scheduler);
		_tokenSource = new CancellationTokenSource();
		
		_hashesByFolderPath = new ConcurrentDictionary<uint, ConcurrentDictionary<uint, HashSet<uint>>>();
		_hashesByFullPath = new ConcurrentDictionary<uint, HashSet<uint>>();

		CreateOrDoMigrations();
		_connection = new SqliteConnection($@"Data Source={_hashDbPath}");
		_connection.Open();
		Initialize();
	}

	public void Dispose()
	{
		_tokenSource.Cancel();
		_transaction?.Commit();
		_transaction?.Dispose();
		_connection?.Close();
		_connection?.Dispose();
		SqliteConnection.ClearAllPools();
		GC.WaitForPendingFinalizers();
	}

	private void Initialize()
	{
		var s = Stopwatch.StartNew();
		using var command = _connection.CreateCommand();
		command.CommandText = @"SELECT IndexId, FolderHash, FileHash, FullHash FROM Paths";
		using var reader = command.ExecuteReader();
		while (reader.Read())
		{
			if (reader.GetValue(0) == DBNull.Value) continue;
			if (reader.GetValue(1) == DBNull.Value) continue;
			if (reader.GetValue(2) == DBNull.Value) continue;
			if (reader.GetValue(3) == DBNull.Value) continue;

			var index = (uint)reader.GetInt64(0);

			var folderHash64 = reader.GetInt64(1);
			if (folderHash64 > uint.MaxValue || folderHash64 < uint.MinValue) continue;
			var folderHash = (uint)folderHash64;
			
			var fileHash64 = reader.GetInt64(2);
			if (fileHash64 > uint.MaxValue || fileHash64 < uint.MinValue) continue;
			var fileHash = (uint)fileHash64;
			
			var fullHash64 = reader.GetInt64(3);
			if (fullHash64 > uint.MaxValue || fullHash64 < uint.MinValue) continue;
			var fullHash = (uint)fullHash64;

			AddToCache(index, folderHash, fileHash, fullHash);
		}

		s.Stop();
		DalamudApi.PluginLog.Information($"Hash database loaded in {s.ElapsedMilliseconds}ms.");
		Begin();
	}

	public void AddPath(ExistsResult result)
	{
		_factory.StartNew(() => AddFullPath(result));
	}

	public void AddPostProcessedPaths(string path)
	{
		_factory
			.StartNew(() => 
				PathPostProcessor
					.PostProcess(path)
					.Select(x => _plugin.Repository.Exists(x)), _tokenSource.Token)
			.ContinueWith(res =>
			{
				foreach (var result in res.Result)
				{
					if (!result.Exists) continue;
					AddFullPath(result);
				}
			}, _tokenSource.Token, TaskContinuationOptions.None, _scheduler)
			.ContinueWith(res =>
			{
				if (res.IsFaulted)
				{
					DalamudApi.PluginLog.Error(res.Exception, $"Error processing path post-processing!");
				}
			}, _tokenSource.Token, TaskContinuationOptions.OnlyOnFaulted, _scheduler);
	}

	private void CreateOrDoMigrations()
	{
		if (File.Exists(_hashDbPath))
		{
			var version = GetDbVersion();
			
			if (version == Version) return;

			DalamudApi.PluginLog.Debug($"Attempting to migrate {version} to {Version}");
			switch (version)
			{
				case 1:
					Migrate1To2();
					break;
				case 2:
					Migrate2To3();
					break;
				default:
					throw new Exception("Unknown database version.");
			}

			return;
		}

		using var connection = new SqliteConnection($@"Data Source={_hashDbPath}");
		connection.Open();

		int result = 0;
		try
		{
			var createCmd = connection.CreateCommand();
			createCmd.CommandText = PathsCreateText;
			result = createCmd.ExecuteNonQuery();
			var transaction = connection.BeginTransaction();
			createCmd = connection.CreateCommand();
			createCmd.CommandText = @"INSERT INTO DbInfo VALUES('Version', @Version)";
			createCmd.Parameters.AddWithValue("@Version", Version.ToString());
			createCmd.ExecuteNonQuery();
			transaction.Commit();
		}
		catch (SqliteException e)
		{
			DalamudApi.PluginLog.Error(e, "[CreateIfNotExists] Failed to create db.");
			DalamudApi.PluginLog.Error($"[CreateIfNotExists] Result was {result}!");
		}

		connection.Close();
		connection.Dispose();
		DalamudApi.PluginLog.Information($"[CreateIfNotExists] Created database.");
	}

	private long GetDbVersion()
	{
		long version = 0;

		try
		{
			using var checkConnection = new SqliteConnection($@"Data Source={_hashDbPath}");
			checkConnection.Open();
			using var checkCommand = checkConnection.CreateCommand();
			checkCommand.CommandText = @"SELECT value FROM dbinfo WHERE type = 'version'";
			using var checkReader = checkCommand.ExecuteReader();
			if (checkReader.Read())
				version = checkReader.GetInt64(0);
		} catch (SqliteException e) {
			DalamudApi.PluginLog.Verbose("[GetDbVersion] Failed to get db version for version 1 and 2. Trying 3.");
			
			using var checkConnection = new SqliteConnection($@"Data Source={_hashDbPath}");
			checkConnection.Open();
			using var checkCommand = checkConnection.CreateCommand();
			checkCommand.CommandText = @"SELECT Value FROM DbInfo WHERE Key = 'Version'";
			using var checkReader = checkCommand.ExecuteReader();
			if (checkReader.Read())
				version = checkReader.GetInt64(0);
		}
		SqliteConnection.ClearAllPools();
		GC.WaitForPendingFinalizers();

		return version;
	}

	public async Task<UploadedDbData> GetUploadableData(int limit)
	{
		return await _factory.StartNew(() =>
		{
			RestartTransaction();
			var data = new UploadedDbData();
			try
			{
				using var cmd = _connection.CreateCommand();
				cmd.Parameters.Add(new SqliteParameter("@Limit", SqliteType.Integer));
				cmd.CommandText = @"SELECT Path FROM Paths WHERE Uploaded = 0 LIMIT @Limit";
				cmd.Parameters["@Limit"].Value = limit;
				using var reader = cmd.ExecuteReader();

				while (reader.Read())
				{
					var path = reader.GetString(0);
					data.Entries.Add(path);
				}
			}
			catch (Exception e)
			{
				DalamudApi.PluginLog.Error(e, "[GetUploadableData] oopsie!");
			}

			RestartTransaction();
			return data;
		}, _tokenSource.Token);
		// 	.ContinueWith((task, result) =>
		// {
		// 	DalamudApi.PluginLog.Error(task.Exception, "[GetUploadableData] oopsie!");
		// 	return (UploadedDbData)result;
		// }, _tokenSource.Token, TaskContinuationOptions.OnlyOnFaulted);
	}

	public void SetUploaded(UploadedDbData data)
	{
		_factory.StartNew(() =>
		{
			RestartTransaction();

			using (var cmd = _connection.CreateCommand())
			{
				cmd.CommandText = @"UPDATE Paths SET Uploaded = 1 WHERE Path = @Path";
				cmd.Parameters.Add(new SqliteParameter("@Path", SqliteType.Text));

				var affectedRowCount = 0;
				foreach (var fullPath in data.Entries)
				{
					cmd.Parameters["@Path"].Value = fullPath;
					affectedRowCount += cmd.ExecuteNonQuery();
				}
			}
			
			RestartTransaction();
		}, _tokenSource.Token);
	}

	public void SetAllUploaded(bool state)
	{
		_factory.StartNew(() =>
		{
			RestartTransaction();

			using var cmd = _connection.CreateCommand();
			cmd.CommandText = @"UPDATE Paths SET Uploaded = @Uploaded WHERE Uploaded != @Uploaded";
			cmd.Parameters.AddWithValue("@Uploaded", state ? 1 : 0);
			cmd.ExecuteNonQuery();

			RestartTransaction();
		}, _tokenSource.Token);
	}

	private void Begin()
	{
		_transaction = _connection?.BeginTransaction();
	}

	private void End()
	{
		_transaction?.Commit();
	}

	private void RestartTransaction()
	{
		try
		{
			End();
			Begin();
		}
		catch (SqliteException e)
		{
			DalamudApi.PluginLog.Error(e, "[RestartTransaction] Failed to restart transaction.");	
		}
	}

	public void SubmitRestartTransaction()
	{
		_factory.StartNew(RestartTransaction, _tokenSource.Token);
	}

	private void AddFullPath(ExistsResult existsResult)
	{
		if (!existsResult.Exists) return;
		if (!AddToCache(existsResult)) return;

		using var command = _connection.CreateCommand();
		command.CommandText = @"INSERT INTO Paths VALUES(@IndexId, @FolderHash, @FileHash, @FullHash, @Path, 0)";
		command.Parameters.AddWithValue("@IndexId", existsResult.IndexId);
		command.Parameters.AddWithValue("@FolderHash", existsResult.FolderHash);
		command.Parameters.AddWithValue("@FileHash", existsResult.FileHash);
		command.Parameters.AddWithValue("@FullHash", existsResult.FullHash);
		command.Parameters.AddWithValue("@Path", existsResult.FullText);
		command.ExecuteNonQuery();
	}

	private bool AddToCache(ExistsResult result)
	{
		return AddToCache(result.IndexId, result.FolderHash, result.FileHash, result.FullHash);
	}
	
	private bool AddToCache(uint indexId, uint folderHash, uint fileHash, uint fullHash)
	{
		if (!_hashesByFullPath.TryGetValue(indexId, out var fullHashes))
			_hashesByFullPath.TryAdd(indexId, new HashSet<uint> { fullHash });
		else
		{
			if (fullHashes.Contains(fullHash))
				return false;
			fullHashes.Add(fullHash);
		}
		
		if (!_hashesByFolderPath.ContainsKey(indexId))
			_hashesByFolderPath.TryAdd(indexId, new ConcurrentDictionary<uint, HashSet<uint>>());
		if (!_hashesByFolderPath[indexId].TryGetValue(folderHash, out var fileHashes))
			_hashesByFolderPath[indexId].TryAdd(folderHash, new HashSet<uint> { fileHash });
		else
		{
			if (fileHashes.Contains(fileHash))
				return false;
			fileHashes.Add(fileHash);
		}

		return true;
	}

	public async Task<List<string>> GetPathList()
	{
		return await _factory.StartNew(() =>
		{
			RestartTransaction();
			var data = new List<string>();
			try
			{
				using var cmd = _connection.CreateCommand();
				cmd.CommandText = @"SELECT Path FROM Paths";
				using var reader = cmd.ExecuteReader();

				while (reader.Read())
				{
					var path = reader.GetString(0);
					data.Add(path);
				}
			}
			catch (Exception e)
			{
				DalamudApi.PluginLog.Error(e, "[GetPathList] oopsie!");
			}

			RestartTransaction();
			return data;
		}, _tokenSource.Token);
	}

	private void Migrate1To2()
	{
		Dispose();
		File.Delete(_hashDbPath);
		CreateOrDoMigrations();
	}

	private void Migrate2To3()
	{
		var paths = new List<string>();
		{
			using var tmpConnection = new SqliteConnection($@"Data Source={_hashDbPath}");
			tmpConnection.Open();
			using (var checkCommand = tmpConnection.CreateCommand())
			{
				checkCommand.CommandText = @"SELECT path FROM fullpaths";
				using var reader = checkCommand.ExecuteReader();
				while (reader.Read())
				{
					if (reader.GetValue(0) == DBNull.Value) continue;

					var path = reader.GetString(0);
					if (string.IsNullOrEmpty(path)) continue;
					paths.Add(path);
				}	
			}
			
			using (var dropCmd = tmpConnection.CreateCommand())
			{
				dropCmd.CommandText = "DROP TABLE dbinfo";
				dropCmd.ExecuteNonQuery();	
			}
			
			using (var createCmd = tmpConnection.CreateCommand())
			{
				createCmd.CommandText = PathsCreateText;
				createCmd.ExecuteNonQuery();	
			}
			
			using var transaction = tmpConnection.BeginTransaction();

			using (var insertCmd = tmpConnection.CreateCommand())
			{
				insertCmd.CommandText = @"INSERT INTO DbInfo (Key, Value) VALUES (@Key, @Value)";
				insertCmd.Parameters.AddWithValue("@Key", "Version");
				insertCmd.Parameters.AddWithValue("@Value", "3");
				insertCmd.ExecuteNonQuery();	
			}

			using (var insertCmd = tmpConnection.CreateCommand())
			{
				insertCmd.CommandText = @"INSERT INTO Paths (IndexId, FolderHash, FileHash, FullHash, Path, Uploaded) VALUES (@IndexId, @FolderHash, @FileHash, @FullHash, @Path, @Uploaded)";
				insertCmd.Parameters.Add("@IndexId", SqliteType.Integer);
				insertCmd.Parameters.Add("@FolderHash", SqliteType.Integer);
				insertCmd.Parameters.Add("@FileHash", SqliteType.Integer);
				insertCmd.Parameters.Add("@FullHash", SqliteType.Integer);
				insertCmd.Parameters.Add("@Path", SqliteType.Text);
				insertCmd.Parameters.Add("@Uploaded", SqliteType.Integer);	

				foreach (var path in paths)
				{
					var existsResult = _plugin.Repository.Exists(path);
					if (!existsResult.Exists) continue;
					insertCmd.Parameters["@IndexId"].Value = existsResult.IndexId;
					insertCmd.Parameters["@FolderHash"].Value = existsResult.FolderHash;
					insertCmd.Parameters["@FileHash"].Value = existsResult.FileHash;
					insertCmd.Parameters["@FullHash"].Value = existsResult.FullHash;
					insertCmd.Parameters["@Path"].Value = existsResult.FullText;
					insertCmd.Parameters["@Uploaded"].Value = 1;
					insertCmd.ExecuteNonQuery();
				}	
			}

			using (var dropCmd = tmpConnection.CreateCommand())
			{
				dropCmd.CommandText = "DROP TABLE fullpaths;";
				dropCmd.ExecuteNonQuery();
			}

			transaction.Commit();

			using (var vacuumCmd = tmpConnection.CreateCommand())
			{
				vacuumCmd.CommandText = "VACUUM";
				vacuumCmd.ExecuteNonQuery();
			}
			
			tmpConnection.Close();
		}
		SqliteConnection.ClearAllPools();
		GC.WaitForPendingFinalizers();
	}

	// public string GetFullPath(uint fullHash)
	// {
	//     if (!_fullPaths.TryGetValue(fullHash, out var ret))
	//         ret = $"~{fullHash:X}";
	//     return ret;
	// }
	//
	// public bool TryGetFullPath(uint fullHash, out string fullPath)
	// {
	//     fullPath = string.Empty;
	//     if (!_fullPaths.TryGetValue(fullHash, out var ret))
	//         return false;
	//     fullPath = ret;
	//     return true;
	// }
}