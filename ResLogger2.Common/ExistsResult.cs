namespace ResLogger2.Common;

public struct ExistsResult
{
    public ReadOnlySpan<char> FileText => FullTextSpan[(FullText.LastIndexOf('/') + 1)..];
    public ReadOnlySpan<char> FolderText => FullTextSpan[..FullText.LastIndexOf('/')];
    public ReadOnlySpan<char> FullTextSpan => FullText.AsSpan();

    public uint FileHash => Utils.CalcHashes(FullText).fileHash;
    public uint FolderHash => Utils.CalcHashes(FullText).folderHash;
    public uint FullHash => Utils.CalcFullHash(FullText);
    
    public bool Exists => Exists1 || Exists2;

    // Just for less collisions. Used only for plugin UI for performance reasons
    private ulong? _extendedHash;
    public ulong ExtendedHash {
        get
        {
            _extendedHash ??= Utils.CalcExtendedHash(FullText);
            return _extendedHash.Value;
        }
    }

    public uint IndexId;
    public string FullText;
    public bool Exists1;
    public bool Exists2;
    
    public static bool operator ==(ExistsResult x, ExistsResult y)
    {
        return x.Equals(y);
    }
    
    public static bool operator !=(ExistsResult x, ExistsResult y)
    {
        return !x.Equals(y);
    }

    public override bool Equals(object obj)
    {
        return obj is ExistsResult er && IndexId == er.IndexId && FolderText.SequenceEqual(er.FolderText);
    }

    public override string ToString()
    {           
        return $"Path: {FullText} Exists1: {Exists1} Exists2: {Exists2} In: {IndexId:X6}";                
    }
}