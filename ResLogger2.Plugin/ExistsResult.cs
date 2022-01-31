namespace ResLogger2.Plugin;

public struct ExistsResult
{
    public uint IndexId;
    
    public string FileText => FullText[(FullText.LastIndexOf('/') + 1)..];
    public string FolderText => FullText[..FullText.LastIndexOf('/')];
    public string FullText;
            
    public uint FileHash => Lumina.Misc.Crc32.Get(FileText);
    public uint FolderHash => Lumina.Misc.Crc32.Get(FolderText);
    public uint FullHash => Lumina.Misc.Crc32.Get(FullText);
    
    public bool FullExists;

    public override bool Equals(object obj)
    {
        return obj is ExistsResult er &&
               IndexId == er.IndexId &&
               FolderText.Equals(er.FolderText);
    }

    public override int GetHashCode()
    {
        return unchecked((int) FullHash);
    }

    public override string ToString()
    {           
        return $"Path: {FullText} Exists: {FullExists} In: {IndexId:X6}";                
    }
}