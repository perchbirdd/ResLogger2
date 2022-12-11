using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Dalamud.Logging;
using ResLogger2.Common;

namespace ResLogger2.Plugin;

public class IndexRepository
{
    private readonly Dictionary<uint, Dictionary<uint, HashSet<uint>>> _indexesByFolderPath;
    private readonly Dictionary<uint, HashSet<uint>> _indexesByFullPath;

    public IndexRepository(string gamePath)
    {
        Initialize(gamePath);
    }

    public IndexRepository()
    {
        _indexesByFolderPath = new Dictionary<uint, Dictionary<uint, HashSet<uint>>>();
        _indexesByFullPath = new Dictionary<uint, HashSet<uint>>();
        var gamePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(gamePath)) return;
        Initialize(gamePath);
    }

    private void Initialize(string gamePath)
    {
        gamePath = Path.GetDirectoryName(gamePath);
        gamePath = Path.Combine(gamePath, "sqpack");

        List<CompositeIndexInfo> indexData = null;
        try
        {
            indexData = PatchIndexHolder.LoadAllIndexData(gamePath).ToList();
        }
        catch (Exception e)
        {
            PluginLog.Error(e, "An error occurred initializing the index repository.");
        }
        
        foreach (var comp in indexData)
        {
            var indexId = comp.IndexId;

            if (!_indexesByFolderPath.ContainsKey(indexId))
                _indexesByFolderPath.Add(indexId, new Dictionary<uint, HashSet<uint>>());
            
            if (!_indexesByFullPath.ContainsKey(indexId))
                _indexesByFullPath.Add(indexId, new HashSet<uint>());

            foreach (var combinedEntry in comp.CombinedIndexEntries.Values)
            {
                var folderHash = combinedEntry.FolderHash;
                var fileHash = combinedEntry.FileHash;
                var fullHash = combinedEntry.FullHash;
                    
                if (!_indexesByFolderPath[indexId].TryGetValue(folderHash, out var fileIndexes))
                    _indexesByFolderPath[indexId].Add(folderHash, new HashSet<uint> { fileHash});
                else
                    fileIndexes.Add(fileHash);
                    
                _indexesByFullPath[indexId].Add(fullHash);
            }

            foreach (var index1Entry in comp.Index1Entries.Values)
            {
                var folderHash = index1Entry.FolderHash;
                var fileHash = index1Entry.FileHash;
                    
                if (!_indexesByFolderPath[indexId].TryGetValue(folderHash, out var fileIndexes))
                    _indexesByFolderPath[indexId].Add(folderHash, new HashSet<uint> { fileHash});
                else
                    fileIndexes.Add(fileHash);
            }

            foreach (var index2Entry in comp.Index2Entries.Values)
            {
                var fullHash = index2Entry.FullHash;
                _indexesByFullPath[indexId].Add(fullHash);
            }
        }
    }

    public ExistsResult Exists(string gamePath)
    {
        try
        {
            var lowerPath = gamePath.ToLower();
            var indexId = Utils.GetCategoryIdForPath(lowerPath);
            var hashes = Utils.CalcAllHashes(lowerPath);

            var result = new ExistsResult
            {
                FullText = gamePath,
                IndexId = indexId,
            };

            result.Exists1 = CheckExists(indexId, hashes.folderHash, hashes.fileHash);
            result.Exists2 = CheckExists(indexId, hashes.fullHash);

            return result;
        }
        catch (Exception e)
        {
            PluginLog.Error(e, "An error occurred in ResLogger2.");
        }

        return default;
    }

    private bool CheckExists(uint indexId, uint folderHash, uint fileHash)
    {
        if (!_indexesByFolderPath.TryGetValue(indexId, out var folderIndexes))
            return false;
        if (!folderIndexes.TryGetValue(folderHash, out var fileIndexes))
            return false;
        return fileIndexes.Contains(fileHash);
    }

    private bool CheckExists(uint indexId, uint fullHash)
    {
        if (!_indexesByFullPath.TryGetValue(indexId, out var fullIndexes))
            return false;
        return fullIndexes.Contains(fullHash);
    }
}