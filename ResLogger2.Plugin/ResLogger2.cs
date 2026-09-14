using Dalamud.Game.Command;
using Dalamud.Plugin;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using ResLogger2.Common;
using ResLogger2.Plugin.Database;
using ResLogger2.Plugin.IPC;
using ResLogger2.Plugin.PapHandling;
using ResLogger2.Plugin.Windows;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using InteropGenerator.Runtime;

namespace ResLogger2.Plugin;

public unsafe class ResLogger2 : IDalamudPlugin
{
    public string Name => "ResLogger2.Plugin";
    
    public Configuration Configuration { get; init; }
    public IndexRepository Repository { get; }
    public LocalHashDatabase Database { get; }
    public HashUploader Uploader { get; }

    private const string CommandName = "/reslog";
    private const int HookHitsTillCommit = 10000;

    private readonly Hook<ResourceManager.Delegates.GetResourceSync> _getResourceSyncHook;
    private readonly Hook<ResourceManager.Delegates.GetResourceAsync> _getResourceAsyncHook;

    private readonly PapHandler _papHandler;
    private readonly PenumbraIpcHandler _penumbraIpcHandler;
    
    private int _hookHits;

    private WindowSystem ResLogWindows { get; init; }
    private LogWindow LogWindow { get; init; }
    private StatsWindow StatsWindow { get; init; }

    public ResLogger2(IDalamudPluginInterface pi)
    {
        DalamudApi.Initialize(pi);

        Configuration = DalamudApi.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(DalamudApi.PluginInterface);
        
        DalamudApi.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Performs ResLogger2 commands. Use /reslog help for more information.",
        });

        var loc = DalamudApi.PluginInterface.GetPluginConfigDirectory();
        loc = Path.Join(loc, "hashdb.db");

        try
        {
            Repository = new IndexRepository();
            Database = new LocalHashDatabase(this, loc);
            Uploader = new HashUploader(this);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "An error occurred in ResLogger2.");
            Dispose();
            return;
        }
        
        ResLogWindows = new WindowSystem();
        LogWindow = new LogWindow(this)
        {
            IsOpen = Configuration.OpenAtStartup
        };
        StatsWindow = new StatsWindow(this);
        ResLogWindows.AddWindow(LogWindow);
        ResLogWindows.AddWindow(StatsWindow);
        
        DalamudApi.PluginInterface.UiBuilder.Draw += ResLogWindows.Draw;

        _getResourceSyncHook = DalamudApi.Hooks.HookFromAddress<ResourceManager.Delegates.GetResourceSync>(
            (nint)ResourceManager.MemberFunctionPointers.GetResourceSync,
            GetResourceSyncDetour);

        _getResourceAsyncHook = DalamudApi.Hooks.HookFromAddress<ResourceManager.Delegates.GetResourceAsync>(
            (nint)ResourceManager.MemberFunctionPointers.GetResourceAsync,
            GetResourceAsyncDetour);

        _getResourceSyncHook.Enable();
        _getResourceAsyncHook.Enable();
        
        var list = DalamudApi.PluginInterface.InstalledPlugins.Where(plugin => plugin.IsLoaded).ToList();

        if (list.All(p => p.Name != "Penumbra"))
        {
            DalamudApi.PluginLog.Verbose("Penumbra not found, enabling PapHandler.");
            _papHandler = new PapHandler(ProcessHook);
            _penumbraIpcHandler = new PenumbraIpcHandler(_papHandler);
        }
        else
        {
            DalamudApi.PluginLog.Verbose("Penumbra detected, so not enabling PapHandler.");
        }
    }

    private ResourceHandle* GetResourceSyncDetour(ResourceManager* thisPtr, ResourceCategory* category, uint* type, uint* hash, CStringPointer path, void* unknown, void* unkDebugPtr, uint unkDebugInt)
    {
        var ret = _getResourceSyncHook.Original(thisPtr, category, type, hash, path, unknown, unkDebugPtr, unkDebugInt);
        ProcessHook(path, HookType.Sync);
        return ret;
    }

    private ResourceHandle* GetResourceAsyncDetour(ResourceManager* thisPtr, ResourceCategory* category, uint* type, uint* hash, CStringPointer path, void* unknown, bool isUnknown, void* unkDebugPtr, uint unkDebugInt)
    {
        var ret = _getResourceAsyncHook.Original(thisPtr, category, type, hash, path, unknown, isUnknown, unkDebugPtr, unkDebugInt);
        ProcessHook(path, HookType.Async);
        return ret;
    }

    public void ProcessHook(CStringPointer pPath, HookType type)
    {
        _hookHits++;
        try
        {
            if (!pPath.HasValue) return;
            var path = pPath.ToString();
            if (string.IsNullOrEmpty(path)) return;
            if (!IsAscii(path)) return;

            var result = Repository.Exists(path);
            LogWindow.HandleLogLine(result, type);
            Database.AddPath(result);

            if (_hookHits <= HookHitsTillCommit) return;
            
            _hookHits = 0;
            Database.SubmitRestartTransaction();
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "An error occurred in ResLogger2.");
        }
    }

    private static bool IsAscii(string value)
    {
        return Encoding.UTF8.GetByteCount(value) == value.Length;
    }

    public void Dispose()
    {
        _getResourceSyncHook?.Disable();
        _getResourceSyncHook?.Dispose();
        _getResourceAsyncHook?.Disable();
        _getResourceAsyncHook?.Dispose();
        _papHandler?.Dispose();
        Api.Dispose();
        StatsWindow?.Dispose();
        Uploader?.Dispose();
        Database?.Dispose();
        DalamudApi.CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        try
        {
            DalamudApi.PluginLog.Debug(command);
            DalamudApi.PluginLog.Debug(args);

            var argv = args.Split(' ');

            if (argv.Length == 1)
            {
                if (argv[0] == "")
                {
                    LogWindow.Toggle();
                } else if (argv[0] == "stats")
                {
                    StatsWindow.Toggle();
                } else if (argv[0] == "help")
                {
                    DalamudApi.ChatGui.Print("ResLogger2 commands:");
                    DalamudApi.ChatGui.Print("/reslog - Opens the ResLogger2 window.");
                    DalamudApi.ChatGui.Print("/reslog help - Shows this help message.");
                    DalamudApi.ChatGui.Print("/reslog hash - Perform XIV's crc32 on an input string or path.");
                    DalamudApi.ChatGui.Print("/reslog stats - Opens the ResLogger2 stats window.");
                }
                
            }
            else if (argv.Length == 2)
            {
                if (argv[0] == "hash")
                {
                    var toHash = args.Replace("hash ", "");
                    var toHash2 = toHash.AsSpan();
                    if (toHash.Contains('/'))
                    {
                        var hashes = Utils.CalcAllHashes(toHash);
                        var splitter = toHash2.LastIndexOf('/');
                        var folderStr = toHash2[..splitter];
                        var fileStr = toHash2[(splitter + 1)..];
                        DalamudApi.ChatGui.Print($"{folderStr}: {hashes.folderHash:X} ({hashes.folderHash})");
                        DalamudApi.ChatGui.Print($"{fileStr}: {hashes.fileHash:X} ({hashes.fileHash})");
                        DalamudApi.ChatGui.Print($"{toHash}: {hashes.fullHash:X} ({hashes.fullHash})");
                    }
                    else
                    {
                        var hash = Utils.CalcFullHash(toHash);
                        DalamudApi.ChatGui.Print($"{toHash}: {hash:X} ({hash})");
                    }
                }
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "oopsie woopsie");
        }
    }
    
    public void OpenStatsWindow()
    {
        StatsWindow.IsOpen = true;
    }

    public void HandleImport(bool isOk, string result)
    {
        if (!isOk) return;
        if (!File.Exists(result)) return;
        try
        {
            var paths = File.ReadAllLines(result);
            foreach (var path in paths)
            {
                var exists = Repository.Exists(path);

                // Be more particular with imported hashes
                if (exists.Exists1 && exists.Exists2)
                    Database.AddPath(exists);
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "An error occurred in ResLogger2.");
        }
    }
}