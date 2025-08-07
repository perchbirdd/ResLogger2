using System;

// This namespace is taken almost directly from Penumbra
namespace ResLogger2.Plugin.PapHandling;

public sealed class PapHandler : IDisposable
{
    public const string LoadAlwaysResidentMotionPacks = "E8 ?? ?? ?? ?? 48 8B D0 48 8D 8E ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 4C 24 ?? 48 8B BC 24 ?? ?? ?? ?? 48 85 C9";
    public const string LoadWeaponDependentResidentMotionPacks = "E8 ?? ?? ?? ?? 48 8B D0 48 8D 8E ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 4C 24 ?? 48 8B BC 24 ?? ?? ?? ?? 48 8B 5C 24";
    public const string LoadInitialResidentMotionPacks = "E8 ?? ?? ?? ?? 48 8B 5D BF 48 8B 7D C7 48 3B DF 74 59";
    public const string LoadMotionPacks = "E8 ?? ?? ?? 00 48 8B 44 24 ?? 49 89 04 24";
    public const string LoadMotionPacks2 = "E8 ?? ?? ?? 00 48 8B 44 24 ?? 48 89 03";
    public const string LoadMigratoryMotionPack = "E9 ?? ?? ?? 00 8B 84 24 ?? ?? 00 00 48 8D";

    private readonly PapRewriter _rewriter;
    private readonly Action<IntPtr, HookType> _processHook;
    
    public unsafe PapHandler(Action<IntPtr, HookType> processHook)
    {
        _processHook = processHook;
        _rewriter = new PapRewriter(PapResourceHandler);
        
        ReadOnlySpan<(string Sig, string Name)> signatures =
        [
            (LoadAlwaysResidentMotionPacks, nameof(LoadAlwaysResidentMotionPacks)),
            (LoadWeaponDependentResidentMotionPacks, nameof(LoadWeaponDependentResidentMotionPacks)),
            (LoadInitialResidentMotionPacks, nameof(LoadInitialResidentMotionPacks)),
            (LoadMotionPacks, nameof(LoadMotionPacks)),
            (LoadMotionPacks2, nameof(LoadMotionPacks2)),
            (LoadMigratoryMotionPack, nameof(LoadMigratoryMotionPack)),
        ];

        foreach (var (sig, name) in signatures)
            _rewriter.Rewrite(sig, name);
    }
    
    public void Dispose() => _rewriter.Dispose();

    private unsafe int PapResourceHandler(void* self, byte* path, int length)
    {
        _processHook.Invoke((IntPtr)path, HookType.Unknown);
        return length;
    }
}