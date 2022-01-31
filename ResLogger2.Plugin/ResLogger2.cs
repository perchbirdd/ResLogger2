using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.IoC;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Game;
using Dalamud.Game.Gui;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Dalamud.Logging;
using ResLogger2.Plugin.Database;

namespace ResLogger2.Plugin;

public class ResLogger2 : IDalamudPlugin
{
    public string Name => "ResLogger2.Plugin";

    private const string CommandName = "/reslog";
    private const int HookHitsTillCommit = 10000;
        
    private delegate IntPtr GetResourceSyncPrototype(IntPtr a1, IntPtr a2, IntPtr a3, IntPtr a4, IntPtr pPath, IntPtr a6);
    private delegate IntPtr GetResourceAsyncPrototype(IntPtr manager, IntPtr a2, IntPtr a3, IntPtr a4, IntPtr pPath, IntPtr a6, byte a7);
    private readonly Hook<GetResourceSyncPrototype> _getResourceSyncHook;
    private readonly Hook<GetResourceAsyncPrototype> _getResourceAsyncHook;

    private readonly IndexValidator _validator;
    private readonly LocalHashDatabase _database;
    private readonly HashUploader _uploader;
    private int _hookHits;

    private static DalamudPluginInterface PluginInterface { get; set; }
    private static CommandManager CommandManager { get; set; }
    private static ChatGui ChatGui { get; set; }

    private WindowSystem ResLogWindows { get; init; }
    private Configuration Configuration { get; init; }
    private LogWindow LogWindow { get; init; }

    public ResLogger2(
        [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
        [RequiredVersion("1.0")] CommandManager commandManager,
        [RequiredVersion("1.0")] ChatGui chatGui,
        [RequiredVersion("1.0")] SigScanner sigScanner)
    {
        PluginInterface = pluginInterface;
        CommandManager = commandManager;
        ChatGui = chatGui;

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);
        
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the ResLogger2 interface."
        });

        var loc = pluginInterface.GetPluginConfigDirectory();
        loc = Path.Join(loc, "hashdb.db");

        try
        {
            _validator = new IndexValidator();
            _database = new LocalHashDatabase(loc);
            _uploader = new HashUploader(_database, Configuration);
        }
        catch (Exception e)
        {
            PluginLog.Error(e, "An error occurred in ResLogger2.");
            Dispose();
            return;
        }
        
        ResLogWindows = new WindowSystem();
        LogWindow = new LogWindow(PluginInterface.UiBuilder, Configuration, _database, _uploader)
        {
            IsOpen = Configuration.OpenAtStartup
        };
        ResLogWindows.AddWindow(LogWindow);
        PluginInterface.UiBuilder.Draw += () => ResLogWindows.Draw();
        
        var getResourceAsync = sigScanner.ScanText("E8 ?? ?? ?? 00 48 8B D8 EB ?? F0 FF 83 ?? ?? 00 00");
        var getResourceSync = sigScanner.ScanText("E8 ?? ?? 00 00 48 8D 8F ?? ?? 00 00 48 89 87 ?? ?? 00 00");
        _getResourceAsyncHook = new Hook<GetResourceAsyncPrototype>(getResourceAsync, GetResourceAsyncDetour);
        _getResourceSyncHook = new Hook<GetResourceSyncPrototype>(getResourceSync, GetResourceSyncDetour);
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
            var path = Marshal.PtrToStringAnsi(pPath);
            if (pPath == IntPtr.Zero) return;
            if (string.IsNullOrEmpty(path)) return;
            if (!IsAscii(path)) return;

            var result = _validator.Exists(path);
            LogWindow.HandleLogLine(result, type);
            _database.AddPath(result);

            if (_hookHits <= HookHitsTillCommit) return;
            
            _hookHits = 0;
            _database.SubmitRestartTransaction();
        }
        catch (Exception e)
        {
            PluginLog.Error(e, "An error occurred in ResLogger2.");
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
        _uploader?.Dispose();
        _database?.Dispose();
        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        LogWindow.Toggle();
        
        // var entry = new XivChatEntry();
        //     
        // switch (args)
        // {
        //     case "log":
        //         LogWindow.Toggle();
        //         break;
        //     case { } s when s.StartsWith("crc"):
        //         var arg = args.Split(' ')[1];
        //         var hash = Lumina.Misc.Crc32.Get(arg);
        //         entry.Message.Append(new TextPayload($"{arg}: {hash} (0x{hash:X})"));
        //         ChatGui.PrintChat(entry);    
        //         break;
        //     default:
        //         entry.Message.Append(new TextPayload($"[{command}] [{args}]"));
        //         ChatGui.PrintChat(entry);
        //         break;
        // }
    }
}