using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace ResLogger2.Plugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool Upload { get; set; } = true;
    public bool AutoScroll { get; set; }
    public bool OpenAtStartup { get; set; }
    public bool OnlyDisplayUnique { get; set; }
    public bool HashTooltip { get; set; } = true;
    public bool LogNonexistentPaths { get; set; } = true;

    [NonSerialized]
    private DalamudPluginInterface _pluginInterface;

    public void Initialize(DalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
    }

    public void Save()
    {
        _pluginInterface.SavePluginConfig(this);
    }
}