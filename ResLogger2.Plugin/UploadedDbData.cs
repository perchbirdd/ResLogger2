using System.Collections.Generic;

namespace ResLogger2.Plugin;

public class UploadedDbData
{
    public readonly List<string> Entries;

    public UploadedDbData()
    {
        Entries = new List<string>();
    }
}