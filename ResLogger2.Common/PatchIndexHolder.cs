using ResLogger2.Common.ServerDatabase.Model;
using ZiPatchLib;
using ZiPatchLib.Chunk.SqpkCommand;
using ZiPatchLib.Util;

namespace ResLogger2.Common;

public class PatchIndexHolder
{
	public GameVersion GameVersion { get; private set; }
	public List<CompositeIndexInfo> Indexes { get; private set; }
	
	public string Repo { get; private set; }

	private PatchIndexHolder() {}
	
	private PatchIndexHolder(string gameVersion, string directory, string repo = "N/A", string? comment = null)
	{
		GameVersion = GameVersion.Parse(gameVersion, comment);
		Indexes = LoadAllIndexData(directory);
		Repo = repo;
	}

	public static PatchIndexHolder FromDirectory(string gameVersion, string directory, string repo = "N/A")
	{
		//hacky wacky
		var repoo = repo;
		string? commentt = null;
		var file = Path.Combine(directory, "info.txt");
		if (File.Exists(file))
		{
			var lines = File.ReadAllLines(Path.Combine(directory, "info.txt"));
			repoo = lines[0];
			commentt = lines[1] == "N/A" ? null : commentt;
		}
		return new PatchIndexHolder(gameVersion, directory, repoo, commentt);
	}

	public static PatchIndexHolder FromPatch(string patchFilePath, string repo = "N/A")
	{
		var patchFileName = Path.GetFileNameWithoutExtension(patchFilePath);
		var patchVersion = patchFileName[1..];
		if (patchFileName.StartsWith("H"))
			return new PatchIndexHolder { GameVersion = GameVersion.Parse(patchVersion), Indexes = new List<CompositeIndexInfo>(), Repo = repo };
		
		var extractedPath = ExtractPatch(patchFilePath);
		var holder = new PatchIndexHolder(patchVersion, extractedPath, repo);
		Directory.Delete(extractedPath, true);
		return holder;
	}

	private static string ExtractPatch(string patchPath)
	{
		var patchApplyPath = Path.GetTempFileName();
		File.Delete(patchApplyPath);
		Directory.CreateDirectory(patchApplyPath);

		using var patchFile = new ZiPatchFile(new SqexFileStream(patchPath, FileMode.Open));
		using var store = new SqexFileStreamStore();

		var config = new ZiPatchConfig(patchApplyPath)
		{
			Store = store,
			IgnoreMissing = true,
			IgnoreOldMismatch = true,
		};
				
		foreach (var chunk in patchFile.GetChunks())
			if (chunk is SqpkFile f)
				if (f.Operation == SqpkFile.OperationKind.AddFile && (f.TargetFile.ToString().Contains("index")))
					chunk.ApplyChunk(config);
		return patchApplyPath;
	}

	public static List<CompositeIndexInfo> LoadAllIndexData(string directory)
	{
		var index1s = Directory.GetFiles(directory, "*.index", SearchOption.AllDirectories).ToDictionary(Path.GetFileNameWithoutExtension, IndexFile.Read);
		var index2s = Directory.GetFiles(directory, "*.index2", SearchOption.AllDirectories).ToDictionary(Path.GetFileNameWithoutExtension, IndexFile.Read);

		var combined = new List<CompositeIndexInfo>();

		foreach (var index1Element in index1s.ToArray())
		{
			if (index2s.TryGetValue(index1Element.Key, out var index2))
			{
				combined.Add(new CompositeIndexInfo(index1Element.Value, index2));
				index1s.Remove(index1Element.Key);
				index2s.Remove(index1Element.Key);
			}
		}
		
		foreach (var index1Element in index1s)
		{
			combined.Add(new CompositeIndexInfo(index1Element.Value, null));
		}
		
		foreach (var index2Element in index2s)
		{
			combined.Add(new CompositeIndexInfo(null, index2Element.Value));
		}

		return combined;
	}
}