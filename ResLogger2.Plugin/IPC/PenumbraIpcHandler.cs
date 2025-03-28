using System;
using Dalamud.Plugin.Ipc;
using ResLogger2.Plugin.PapHandling;

namespace ResLogger2.Plugin.IPC;

public class PenumbraIpcHandler
{
    private readonly PapHandler _papHandler;
    private ICallGateSubscriber<int, int, object> _penumbraLaunching;

    public PenumbraIpcHandler(PapHandler papHandler)
    {
        _papHandler = papHandler;
        _penumbraLaunching = DalamudApi.PluginInterface.GetIpcSubscriber<int, int, object>("Penumbra.Launching");
        _penumbraLaunching.Subscribe(PenumbraLaunching);
    }

    private void PenumbraLaunching(int major, int minor)
    {
        DalamudApi.PluginLog.Debug("Detected Penumbra launch, unloading pap handler.");
        _papHandler.Dispose();
    }
}