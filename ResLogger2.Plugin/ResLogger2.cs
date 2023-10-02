using Dalamud.Game.Command;
using Dalamud.Plugin;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using ResLogger2.Common;
using ResLogger2.Plugin.Database;
using ResLogger2.Plugin.Windows;

namespace ResLogger2.Plugin;

public class ResLogger2 : IDalamudPlugin
{
    public string Name => "ResLogger2.Plugin";
    
    public Configuration Configuration { get; init; }
    public IndexRepository Repository { get; }
    public LocalHashDatabase Database { get; }
    public HashUploader Uploader { get; }

    private const string CommandName = "/reslog";
    private const int HookHitsTillCommit = 10000;
        
    private delegate IntPtr GetResourceSyncPrototype(IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4, IntPtr pPath, IntPtr a6);
    private delegate IntPtr GetResourceAsyncPrototype(IntPtr manager, IntPtr a2, IntPtr a3, IntPtr a4, IntPtr pPath, IntPtr a6, byte a7);
    private readonly Hook<GetResourceSyncPrototype> _getResourceSyncHook;
    private readonly Hook<GetResourceAsyncPrototype> _getResourceAsyncHook;
    
    private int _hookHits;

    private WindowSystem ResLogWindows { get; init; }
    private LogWindow LogWindow { get; init; }
    private StatsWindow StatsWindow { get; init; }

    public ResLogger2(DalamudPluginInterface pi)
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
        
        DalamudApi.PluginInterface.UiBuilder.Draw += () => ResLogWindows.Draw();
        
        var getResourceAsync = DalamudApi.SigScanner.ScanText("E8 ?? ?? ?? 00 48 8B D8 EB ?? F0 FF 83 ?? ?? 00 00");
        var getResourceSync = DalamudApi.SigScanner.ScanText("E8 ?? ?? 00 00 48 8D 8F ?? ?? 00 00 48 89 87 ?? ?? 00 00");
        _getResourceAsyncHook = DalamudApi.Hooks.HookFromAddress<GetResourceAsyncPrototype>(getResourceAsync, GetResourceAsyncDetour);
        _getResourceSyncHook = DalamudApi.Hooks.HookFromAddress<GetResourceSyncPrototype>(getResourceSync, GetResourceSyncDetour);
        _getResourceAsyncHook.Enable();
        _getResourceSyncHook.Enable();
    }

    private IntPtr GetResourceSyncDetour(IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4, IntPtr pPath, IntPtr a6)
    {
        var ret = _getResourceSyncHook.Original(a1, a2, a3, a4, pPath, a6);
        ProcessHook(pPath, HookType.Sync);
        return ret; 
    }

    private IntPtr GetResourceAsyncDetour(IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4, IntPtr pPath, IntPtr a6, byte a7)
    {
        var ret = _getResourceAsyncHook.Original(a1, a2, a3, a4, pPath, a6, a7);
        ProcessHook(pPath, HookType.Async);
        return ret;    
    }

    private void ProcessHook(IntPtr pPath, HookType type)
    {
        _hookHits++;
        try
        {
            if (pPath == IntPtr.Zero) return;
            var path = Marshal.PtrToStringAnsi(pPath);
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