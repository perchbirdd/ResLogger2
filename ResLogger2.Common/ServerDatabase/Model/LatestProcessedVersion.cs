namespace ResLogger2.Common.ServerDatabase.Model;

public class LatestProcessedVersion
{
	public string Repo { get; set; }
	public GameVersion Version { get; set; }
}