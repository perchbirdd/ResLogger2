namespace ResLogger2.Common.LocalDatabase.Model;

public class LocalPathEntry
{
	public uint IndexId { get; set; }
	public uint FolderHash { get; set; }
	public uint FileHash { get; set; }
	public uint FullHash { get; set; }
	public string Path { get; set; }
	public bool Uploaded { get; set; }
}