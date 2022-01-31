using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Internal.Notifications;
using Dalamud.Interface.Windowing;
using Dalamud.Logging;
using ImGuiNET;
using ResLogger2.Plugin.Database;

namespace ResLogger2.Plugin;

public class LogWindow : Window
{
    private readonly List<uint> _logText = new();
    private readonly List<uint> _filteredLogText = new();
    private readonly HashSet<uint> _uniqueLogText = new();
    private readonly HashSet<uint> _filteredUniqueLogText = new();
    private readonly Dictionary<uint, LogEntry> _entries = new();
    private readonly object _renderLock = new();

    private string _textFilter = string.Empty;
    private HookType _levelFilter = HookType.None;
    private bool _isFiltered;

    private readonly UiBuilder _uiBuilder;
    private readonly Configuration _config;
    private readonly LocalHashDatabase _db;
    private readonly HashUploader _uploader;

    private ICollection<uint> Source
    {
        get
        {
            if (_config.OnlyDisplayUnique && _isFiltered) return _filteredUniqueLogText;
            if (_config.OnlyDisplayUnique && !_isFiltered) return _uniqueLogText;
            if (!_config.OnlyDisplayUnique && _isFiltered) return _filteredLogText;
            return _logText;
        }
    }

    public LogWindow(UiBuilder uiBuilder,
        Configuration config,
        LocalHashDatabase db,
        HashUploader uploader) : base("ResLogger2", ImGuiWindowFlags.MenuBar)
    {
        _uiBuilder = uiBuilder;
        _config = config;
        _db = db;
        _uploader = uploader;

        Size = new Vector2(500, 400);
        SizeCondition = ImGuiCond.FirstUseEver;
        RespectCloseHotkey = false;
    }

    private void Clear()
    {
        lock (_renderLock)
        {
            _logText.Clear();
            _uniqueLogText.Clear();
            _filteredLogText.Clear();
            _filteredUniqueLogText.Clear();
            _entries.Clear();
        }
    }

    private void Copy(bool unique = false)
    {
        StringBuilder sb = new StringBuilder();

        lock (_renderLock)
        {
            ICollection<uint> source = unique ? _uniqueLogText : _logText;
        
            foreach (var hash in source)
            {
                if (!_entries.TryGetValue(hash, out var logEntry)) continue;
                sb.Append(logEntry.Info.FullText);
                sb.Append(Environment.NewLine);
            }    
        }
        
        ImGui.SetClipboardText(sb.ToString());
    }

    public void HandleLogLine(ExistsResult result, HookType level)
    {
        AddAndFilter(result, level);
    }

    public override void Draw()
    {
        DrawMenuBar();
        DrawLogPane();
    }

    private void DrawMenuBar()
    {
        if (ImGui.BeginMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("Export database to path list"))
                {
                    Task.Run(() =>
                    {
                        var result = _db.ExportFileList();
                        if (result)
                            _uiBuilder.AddNotification("Exported path list.", "Export Complete", NotificationType.Success);
                        else
                            _uiBuilder.AddNotification("An error occurred while exporting path list.", "Export Failed", NotificationType.Error);
                    });
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Edit"))
            {
                if (ImGui.MenuItem("Clear log"))
                {
                    Clear();
                }

                if (ImGui.MenuItem("Copy"))
                {
                    Copy();
                }

                if (ImGui.MenuItem("Copy unique"))
                {
                    Copy(true);
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Settings"))
            {
                var upload = _config.Upload;
                var autoScroll = _config.AutoScroll;
                var onlyDisplayUnique = _config.OnlyDisplayUnique;
                var openAtStartup = _config.OpenAtStartup;

                if (ImGui.MenuItem("Upload paths", "", ref upload))
                {
                    _config.Upload = upload;
                    _config.Save();
                }

                if (ImGui.MenuItem("Auto-scroll", "", ref autoScroll))
                {
                    _config.AutoScroll = autoScroll;
                    _config.Save();
                }
                
                if (ImGui.MenuItem("Log unique paths only", "", ref onlyDisplayUnique))
                {
                    _config.OnlyDisplayUnique = onlyDisplayUnique;
                    _config.Save();
                }

                if (ImGui.MenuItem("Open at startup", "", ref openAtStartup))
                {
                    _config.OpenAtStartup = openAtStartup;
                    _config.Save();
                }

                ImGui.EndMenu();
            }

            #if DEBUG
            if (ImGui.BeginMenu("Debug"))
            {
                if (ImGui.MenuItem("Reset Uploaded"))
                {
                    _db.ResetUploaded();
                }
                ImGui.EndMenu();
            }
            #endif
            
            if (_config.Upload)
            {
                var state = _uploader.State;

                switch (state.UploadStatus)
                {
                    case UploadState.Status.Idle:
                        ImGui.BeginMenu("Upload Status: Idle...");
                        break;
                    case UploadState.Status.Success:
                        ImGui.PushStyleColor(ImGuiCol.TextDisabled, ImGuiColors.HealerGreen);
                        ImGui.BeginMenu($"Last upload: Success ({state.Response}) ({state.Count} paths)", false);
                        ImGui.PopStyleColor();
                        break;
                    case UploadState.Status.FaultedLocally:
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                        if (ImGui.BeginMenu("Last upload: Faulted Locally"))
                        {
                            _uploader.LogUploadExceptions();
                            ImGui.EndMenu();
                        }
                        ImGui.PopStyleColor();
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            ImGui.Text("Click to log last exception to the Plugin Log for troubleshooting.");
                            ImGui.EndTooltip();
                        }
                        break;
                    case UploadState.Status.FaultedRemotely:
                        ImGui.PushStyleColor(ImGuiCol.TextDisabled, ImGuiColors.DalamudRed);
                        ImGui.BeginMenu($"Last upload: Faulted Remotely ({state.Response})", false);
                        ImGui.PopStyleColor();
                        break;
                }
            }
            
            ImGui.EndMenuBar();
        }
    }

    private void DrawLogPane()
    {
        if (ImGui.InputTextWithHint("##filterText", "Path Filter", ref _textFilter, 255))
        {
            Refilter();
        }
        ImGui.SameLine();
        ImGui.Text("Hook Type:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        
        var filterVal = (int)_levelFilter;
        var enumValues = Enum.GetValues(typeof(HookType));
        if (ImGui.Combo("##filtertype", ref filterVal, enumValues.Cast<HookType>().Select(x => x.ToString()).ToArray(), enumValues.Length))
        { 
            _levelFilter = (HookType)filterVal;
            Refilter();
        }
        _isFiltered = !string.IsNullOrEmpty(_textFilter) || _levelFilter != HookType.None;

        ImGui.BeginChild("scrolling", new Vector2(0, -1), false, ImGuiWindowFlags.AlwaysVerticalScrollbar);

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);

        ImGuiListClipperPtr clipper;
        unsafe
        {
            clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
        }

        ImGui.PushFont(UiBuilder.MonoFont);

        var childPos = ImGui.GetWindowPos();
        var childDrawList = ImGui.GetWindowDrawList();
        var childSize = ImGui.GetWindowSize();

        var cursorLogLine = ImGuiHelpers.GlobalScale * 35;

        lock (_renderLock)
        {
            var e = Source.ToList();
            clipper.Begin(e.Count);
            
            while (clipper.Step())
            {
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    var hash = e[i];
                    if (!_entries.TryGetValue(hash, out var line))
                    {
                        var text = $"wtf {hash}";
                        PluginLog.Error(text);
                        ImGui.TextUnformatted(text);
                    }
                    else
                    {
                        ImGui.TextUnformatted(GetTextForLogEventLevel(line.HookType));
                        ImGui.SameLine();
                        ImGui.SetCursorPosX(cursorLogLine);
                        ImGui.TextUnformatted(line.Info.FullText);    
                    }
                }
            }

            clipper.End();
        }

        ImGui.PopFont();
        ImGui.PopStyleVar();

        if (_config.AutoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
        {
            ImGui.SetScrollHereY(1.0f);
        }

        // Draw dividing line
        var offset = ImGuiHelpers.GlobalScale * 28;
        childDrawList.AddLine(new Vector2(childPos.X + offset, childPos.Y), new Vector2(childPos.X + offset, childPos.Y + childSize.Y), 0x4FFFFFFF, 1.0f);

        ImGui.EndChild();
    }

    private void AddAndFilter(ExistsResult info, HookType level)
    {
        if (!_entries.TryGetValue(info.FullHash, out var entry))
        {
            entry = new LogEntry(info, level);
            _entries.Add(info.FullHash, entry);
        }
        
        lock (_renderLock)
        {
            _logText.Add(info.FullHash);
            _uniqueLogText.Add(info.FullHash);

            if (!_isFiltered) return;
            if (!IsFilterApplicable(entry)) return;
        
            _filteredLogText.Add(info.FullHash);
            _filteredUniqueLogText.Add(info.FullHash);    
        }
    }

    private bool IsFilterApplicable(LogEntry entry)
    {
        if (_levelFilter != HookType.None)
        {
            return entry.HookType == _levelFilter;
        }

        if (!string.IsNullOrEmpty(_textFilter))
            return entry.Info.FullText.Contains(_textFilter);

        return true;
    }

    private void Refilter()
    {
        lock (_renderLock)
        {
            _filteredLogText.Clear();
            _filteredUniqueLogText.Clear();
            
            _filteredLogText.AddRange(_logText.Where(x => IsFilterApplicable(_entries[x])));
            _filteredUniqueLogText.UnionWith(_uniqueLogText.Where(x => IsFilterApplicable(_entries[x])));
        }
    }

    private string GetTextForLogEventLevel(HookType level) => level switch
    {
        HookType.Sync => "[S]",
        HookType.Async => "[A]",
        _ => throw new ArgumentOutOfRangeException(level.ToString(), "Invalid HookType"),
    };

    private class LogEntry
    {
        public ExistsResult Info { get; }
        public HookType HookType { get; }

        public LogEntry(ExistsResult info, HookType hookType)
        {
            Info = info;
            HookType = hookType;
        }

        public override bool Equals(object obj)
        {
            return obj is LogEntry le && Info.Equals(le.Info) && HookType == le.HookType;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Info, HookType);
        }
    }
}