using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Logging;

namespace ResLogger2.Plugin;

public class PathPostProcessor
{
	private static string[] _charaIds =
	{
		"c0101", "c0201", // Hyur Mid
		"c0104", "c0204", // Hyur Mid kids
		"c0301", "c0401", // Hyur Highlander
		"c0501", "c0601", // Elezen
		"c0504", "c0604", // Elezen kids
		"c0701", "c0801", // Miqote
		"c0704", "c0804", // Miqote kids
		"c0901", "c1001", // Roegadyn
		"c1101", "c1201", // Lalafell
		"c1301", "c1401", // Au ra
		"c1304", "c1404", // Au ra kids
		"c1501", "c1601", // Hrothgar
		"c1701", "c1801", // Viera
	};

	private static string[] _locIds =
	{
		"_en.", "_fr.", "_de.", "_ja.",
	};
	
	private static string[] _locIds2 =
	{
		"/en/", "/fr/", "/de/", "/ja/",
	};

	private static string[] _texSuffix =
	{
		"_m.tex", "_n.tex", "_d.tex", "_s.tex"
	};

	private static string[] _equipType =
	{
		"_met", "_top", "_glv", "_dwn", "_sho"
	};
	
	private static string[] _accType =
	{
		"_ear", "_nek", "_wrs", "_ril", "_rir"
	};

	private const string GenM = "_m_";
	private const string GenF = "_f_";
	private static readonly Regex _reChara = new(@"c\d{4}(\D)", RegexOptions.Compiled);
	// private static readonly HashSet<string> _staging = new();

	private static void PostProcessInternal(ref HashSet<string> paths, ref HashSet<string> staging)
	{
		staging.Clear();

		foreach (var path in paths)
		{
			if (_reChara.Match(path).Success)
				foreach (var charaId in _charaIds)
					staging.Add(_reChara.Replace(path, charaId + @"$1"));
			
			if (path.StartsWith("ui/") && path.EndsWith(".tex"))
			{
				if (path.EndsWith("_hr1.tex"))
					staging.Add(path.Replace("_hr1", ""));
				else
					staging.Add(path.Replace(".tex", "_hr1.tex"));
			}

			if (path.Contains(GenM))
				staging.Add(path.Replace(GenM, GenF));
			else if (path.Contains(GenF))
				staging.Add(path.Replace(GenF, GenM));

			AddReplacementsFromList(path, _locIds, ref staging);
			AddReplacementsFromList(path, _locIds2, ref staging);
			AddReplacementsFromList(path, _texSuffix, ref staging);
			AddReplacementsFromList(path, _equipType, ref staging);
			AddReplacementsFromList(path, _accType, ref staging);
		}

		paths.UnionWith(staging);
	}
	
	private static void AddReplacementsFromList(string path, string[] elements, ref HashSet<string> staging)
	{
		var ours = elements.FirstOrDefault(path.Contains);

		if (ours == null) return;
		
		foreach (var element in elements)
		{
			if (element != ours)
				staging.Add(path.Replace(ours, element));
		}
	}

	public static HashSet<string> PostProcess(string path)
	{
		PluginLog.Debug($"PostProcessing {path}");
		var paths = new HashSet<string> { path };
		var staging = new HashSet<string>();
		int pre;
		do
		{
			pre = paths.Count;
			PostProcessInternal(ref paths, ref staging);
		} while (paths.Count != pre);
		foreach (var path1 in paths)
		{
			PluginLog.Debug($"\t{path1}");
		}
		return paths;
	}
}