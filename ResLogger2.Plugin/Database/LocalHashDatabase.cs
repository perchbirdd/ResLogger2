using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Logging;
using Microsoft.Data.Sqlite;

namespace ResLogger2.Plugin.Database;

public class LocalHashDatabase
{
    private const int Version = 2;

    private readonly string _hashDbPath;
    private readonly SqliteConnection _connection;
    private readonly TaskScheduler _scheduler;
    private readonly TaskFactory _factory;
    private readonly CancellationTokenSource _tokenSource;
    private DbTransaction _transaction;
    
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<uint, string>> _fullPaths;

    public LocalHashDatabase(string path)
    {
        _hashDbPath = path;
        _fullPaths = new ConcurrentDictionary<uint, ConcurrentDictionary<uint, string>>();
        _scheduler = new LimitedConcurrencyLevelTaskScheduler(1);
        _factory = new TaskFactory(_scheduler);
        _tokenSource = new CancellationTokenSource();

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
        command.CommandText = @"SELECT hash, `index`, path FROM fullpaths";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetValue(0) == DBNull.Value) continue;
            if (reader.GetValue(1) == DBNull.Value) continue;
            if (reader.GetValue(2) == DBNull.Value) continue;
            
            var hash64 = reader.GetInt64(0);
            var index = (uint) reader.GetInt64(2);

            if (hash64 > uint.MaxValue || hash64 < uint.MinValue) continue;
            var hash = (uint) hash64;

            var path = reader.GetString(1);
            if (string.IsNullOrEmpty(path))
                continue;
            if (!_fullPaths.TryGetValue(index, out var paths))
            {
                paths = new ConcurrentDictionary<uint, string>();
                paths.TryAdd(hash, path);
                _fullPaths.TryAdd(index, paths);
            }
            else
            {
                paths.TryAdd(hash, path);    
            }
        }

        s.Stop();
        PluginLog.Information($"Hash database loaded in {s.ElapsedMilliseconds}ms.");
        Begin();
    }

    public void AddPath(ExistsResult result)
    {
        AddFullPath(result);
    }

    private void CreateOrDoMigrations()
    {
        if (File.Exists(_hashDbPath))
        {
            long version = 0;

            {
                using var checkConnection = new SqliteConnection($@"Data Source={_hashDbPath}");
                checkConnection.Open();
                using var checkCommand = checkConnection.CreateCommand();
                checkCommand.CommandText = @"SELECT value FROM dbinfo WHERE type = 'version'";
                using var checkReader = checkCommand.ExecuteReader();
                if (checkReader.Read())
                {
                    version = checkReader.GetInt64(0);
                    if (version == Version)
                        return;
                }    
            }
            SqliteConnection.ClearAllPools();
            GC.WaitForPendingFinalizers();

            PluginLog.Debug($"Attempting to migrate {version} to {Version}");
            switch (version)
            {
                case 1:
                    Migrate1To2();
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
            createCmd.CommandText =
                "BEGIN TRANSACTION;" +
                "CREATE TABLE dbinfo (type string NOT NULL, value string NOT NULL);" +
                "CREATE TABLE fullpaths (hash INTEGER NOT NULL, `index` INTEGER NOT NULL, path TEXT NOT NULL, uploaded BOOLEAN NOT NULL, PRIMARY KEY (hash, `index`));" +
                "COMMIT TRANSACTION;";
            result = createCmd.ExecuteNonQuery();
            var transaction = connection.BeginTransaction();
            createCmd = connection.CreateCommand();
            createCmd.CommandText = @"INSERT INTO dbinfo VALUES('version', @Version)";
            createCmd.Parameters.AddWithValue("@Version", Version.ToString());
            createCmd.ExecuteNonQuery();
            transaction.Commit();
        }
        catch (SqliteException e)
        {
            PluginLog.Error(e, "[CreateIfNotExists] Failed to create db.");
            PluginLog.Error($"[CreateIfNotExists] Result was {result}!");
        }

        connection.Close();
        connection.Dispose();
        PluginLog.Information($"[CreateIfNotExists] Created database.");
    }

    public async Task<UploadedDbData> GetUploadableData()
    {
        return await _factory.StartNew(() =>
        {
            RestartTransaction();
            var data = new UploadedDbData();
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"SELECT path FROM fullpaths WHERE fullpaths.uploaded = 0 LIMIT 500";
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    var path = reader.GetString(0);
                    data.Entries.Add(path);
                }
            }
            catch (Exception e)
            {
                PluginLog.Error(e, "[GetUploadableData] oopsie!");
            }

            RestartTransaction();
            return data;
        }, _tokenSource.Token);
    }

    public void SetUploaded(UploadedDbData data)
    {
        _factory.StartNew(() =>
        {
            RestartTransaction();

            using var cmd = _connection.CreateCommand();
            cmd.Parameters.Add(new SqliteParameter("@Hash", SqliteType.Integer));

            foreach (var fullPath in data.Entries)
            {
                cmd.CommandText = @"UPDATE fullpaths SET uploaded = 1 WHERE hash = @Hash";
                cmd.Parameters["@Hash"].Value = Lumina.Misc.Crc32.Get(fullPath);
                cmd.ExecuteNonQuery();
            }

            RestartTransaction();
        }, _tokenSource.Token);
    }

    public void ResetUploaded()
    {
        _factory.StartNew(() =>
        {
            RestartTransaction();

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"UPDATE fullpaths SET uploaded = 0";
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
        End();
        Begin();
    }

    public void SubmitRestartTransaction()
    {
        _factory.StartNew(RestartTransaction, _tokenSource.Token);
    }

    private void AddFullPath(ExistsResult result)
    {
        _factory.StartNew(() =>
        {
            if (!result.FullExists) return;
            if (!_fullPaths.TryGetValue(result.IndexId, out var index))
            {
                index = new ConcurrentDictionary<uint, string>();
                _fullPaths.TryAdd(result.IndexId, index);
            }
            else if (index.ContainsKey(result.FullHash)) return;
            
            index.TryAdd(result.FullHash, result.FullText);
            using var command = _connection.CreateCommand();
            command.CommandText = @"INSERT OR IGNORE INTO fullpaths values(@Hash, @Index, @Path, 0)";
            command.Parameters.AddWithValue("@Hash", result.FullHash);
            command.Parameters.AddWithValue("@Index", result.IndexId);
            command.Parameters.AddWithValue("@Path", result.FullText);
            command.ExecuteNonQuery();
        }, _tokenSource.Token);
    }

    public bool ExportFileList()
    {
        var path = _hashDbPath + ".export";
        var paths = new List<string>();
        foreach (var index in _fullPaths)
            paths.AddRange(index.Value.Values);
        File.WriteAllLines(path, paths);
        return File.Exists(path);
    }

    private void Migrate1To2()
    {
        Dispose();
        File.Delete(_hashDbPath);
        CreateOrDoMigrations();
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