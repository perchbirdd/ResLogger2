namespace ResLogger2.Common.ServerDatabase.Model;

public class PathEntry : Entry
{
	public uint FolderHash { get; set; }
	public uint FileHash { get; set; }
	public uint FullHash { get; set; }
	public string? Path { get; set; }

	public override bool Equals(object? obj)
	{
		return obj is PathEntry entry &&
		       FolderHash == entry.FolderHash &&
		       FileHash == entry.FileHash &&
		       FullHash == entry.FullHash;
	}
	
	public override int GetHashCode()
	{
		return HashCode.Combine(FolderHash, FileHash, FullHash);
	}
}