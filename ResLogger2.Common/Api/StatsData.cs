namespace ResLogger2.Common.Api;

public struct StatsData
{
	public Dictionary<uint, IndexStats> Totals { get; set; }
	public Dictionary<uint, IndexStats> Possible { get; set; }
}

public struct IndexStats
{
	public uint TotalPaths { get; set; }
	public uint Paths { get; set; }
}