namespace ResLogger2.Common.Api;

public class UploadedDbData
{
	public readonly List<string> Entries;

	public UploadedDbData()
	{
		Entries = new List<string>();
	}

	public UploadedDbData(List<string> entries)
	{
		Entries = entries;
	}
}