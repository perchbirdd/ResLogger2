using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.IoC;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Game;
using Dalamud.Game.Gui;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Dalamud.Logging;
using ResLogger2.Plugin.Database;

namespace ResLogger2.Plugin;

public class ResLogger2 : IDalamudPlugin
{
    public string Name => "ResLogger2.Plugin";
    
    public static DalamudPluginInterface PluginInterface { get; set; }
    public static CommandManager CommandManager { get; set; }
    public static ChatGui ChatGui { get; set; }
    public Configuration Configuration { get; init; }
    public IndexValidator Validator { get; }
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
            Validator = new IndexValidator();
            Database = new LocalHashDatabase(loc);
            Uploader = new HashUploader(this);
        }
        catch (Exception e)
        {
            PluginLog.Error(e, "An error occurred in ResLogger2.");
            Dispose();
            return;
        }
        
        ResLogWindows = new WindowSystem();
        LogWindow = new LogWindow(this)
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

            var result = Validator.Exists(path);
            LogWindow.HandleLogLine(result, type);
            Database.AddPath(result);

            if (_hookHits <= HookHitsTillCommit) return;
            
            _hookHits = 0;
            Database.SubmitRestartTransaction();
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
        Uploader?.Dispose();
        Database?.Dispose();
        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        LogWindow.Toggle();
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
                var exists = Validator.Exists(path);
                if (!exists.FullExists)
                    PluginLog.Debug($"{exists}");
                if (exists.FullExists)
                {
                    Database.AddPath(exists);
                }
            }
        }
        catch (Exception e)
        {
            PluginLog.Error(e, "An error occurred in ResLogger2.");
        }
    }
}