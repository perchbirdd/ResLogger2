using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using ResLogger2.Common;

namespace ResLogger2.Plugin.Windows;

public class LogWindow : Window
{
    private readonly List<ulong> _logText = new();
    private readonly List<ulong> _filteredLogText = new();
    private readonly HashSet<ulong> _uniqueLogText = new();
    private readonly HashSet<ulong> _filteredUniqueLogText = new();
    private readonly Dictionary<ulong, LogEntry> _entries = new();
    private readonly object _renderLock = new();
    private bool _tooltipLock = false;

    private string _textFilter = string.Empty;
    private HookType _levelFilter = HookType.None;
    private bool _isFiltered;

    private readonly FileDialogManager _fileDialogManager = new();
    private bool _isImporting;

    private readonly ResLogger2 _plugin;

    private ICollection<ulong> Source
    {
        get
        {
            if (_plugin.Configuration.OnlyDisplayUnique && _isFiltered) return _filteredUniqueLogText;
            if (_plugin.Configuration.OnlyDisplayUnique && !_isFiltered) return _uniqueLogText;
            if (!_plugin.Configuration.OnlyDisplayUnique && _isFiltered) return _filteredLogText;
            return _logText;
        }
    }

    public LogWindow(ResLogger2 plugin) : base("ResLogger2", ImGuiWindowFlags.MenuBar)
    {
        _plugin = plugin;

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
            ICollection<ulong> source = unique ? _uniqueLogText : _logText;

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
        _tooltipLock = false;
        DrawMenuBar();
        DrawLogPane();
        _fileDialogManager.Draw();
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
                        try
                        {
                            var result = _plugin.Database.GetPathList().Result;
                            if (result == null)
                            {
                                DalamudApi.PluginLog.Error("Failed to get path list from database.");
                                return;
                            }

                            var configDir = DalamudApi.PluginInterface.ConfigDirectory;
                            var path = Path.Combine(configDir.FullName, "export.txt");
                            File.WriteAllLines(path, result);
                            DalamudApi.NotificationManager.AddNotification(new Notification
                            {
                                Title = "Export Complete",
                                Content = $"Exported path list to {path}.",
                                Type = NotificationType.Success
                            });
                        }
                        catch (Exception e)
                        {
                            DalamudApi.PluginLog.Error(e, "Failed to export path list...");
                            DalamudApi.NotificationManager.AddNotification(new Notification
                            {
                                Content = "An error occurred while exporting path list. Check the plugin log for more information.",
                                Title = "Export Failed",
                                Type = NotificationType.Error
                            });
                        }
                    });
                }

                if (ImGui.MenuItem("Import path list to database"))
                {
                    _fileDialogManager.OpenFileDialog("Import path list", ".*", (b, s) => { Task.Run(() => _plugin.HandleImport(b, s)); });
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
                var upload = _plugin.Configuration.Upload;
                var autoScroll = _plugin.Configuration.AutoScroll;
                var onlyDisplayUnique = _plugin.Configuration.OnlyDisplayUnique;
                var openAtStartup = _plugin.Configuration.OpenAtStartup;
                var hashTooltip = _plugin.Configuration.HashTooltip;
                var logNonexistent = _plugin.Configuration.LogNonexistentPaths;

                if (ImGui.MenuItem("Upload paths", "", ref upload))
                {
                    _plugin.Configuration.Upload = upload;
                    _plugin.Configuration.Save();
                }

                if (ImGui.MenuItem("Auto-scroll", "", ref autoScroll))
                {
                    _plugin.Configuration.AutoScroll = autoScroll;
                    _plugin.Configuration.Save();
                }

                if (ImGui.MenuItem("Log unique paths only", "", ref onlyDisplayUnique))
                {
                    _plugin.Configuration.OnlyDisplayUnique = onlyDisplayUnique;
                    _plugin.Configuration.Save();
                }

                if (ImGui.MenuItem("Open at startup", "", ref openAtStartup))
                {
                    _plugin.Configuration.OpenAtStartup = openAtStartup;
                    _plugin.Configuration.Save();
                }

                if (ImGui.MenuItem("Show hash tooltip", "", ref hashTooltip))
                {
                    _plugin.Configuration.HashTooltip = hashTooltip;
                    _plugin.Configuration.Save();
                }

                if (ImGui.MenuItem("Log paths that don't exist", "", ref logNonexistent))
                {
                    _plugin.Configuration.LogNonexistentPaths = logNonexistent;
                    _plugin.Configuration.Save();
                    Refilter();
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                if (ImGui.MenuItem("Open Server Stats Window"))
                {
                    _plugin.OpenStatsWindow();
                }

                ImGui.EndMenu();
            }

#if DEBUG
            if (ImGui.BeginMenu("Debug"))
            {
                if (ImGui.MenuItem("Set none uploaded"))
                {
                    _plugin.Database.SetAllUploaded(false);
                }

                if (ImGui.MenuItem("Set all uploaded"))
                {
                    _plugin.Database.SetAllUploaded(true);
                }

                ImGui.MenuItem("_isFiltered", "", ref _isFiltered);
                ImGui.EndMenu();
            }
#endif

            if (_plugin.Configuration.Upload)
            {
                var state = _plugin.Uploader.State;

                ImGui.PushStyleVar(ImGuiStyleVar.DisabledAlpha, 1f);
                switch (state.UploadStatus)
                {
                    case UploadState.Status.Idle:
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudWhite);
                        ImGui.BeginMenu("Upload Status: Idle...", false);
                        ImGui.PopStyleColor();
                        break;
                    case UploadState.Status.Uploading:
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudWhite);
                        ImGui.BeginMenu("Upload Status: Uploading...", false);
                        ImGui.PopStyleColor();
                        break;
                    case UploadState.Status.Success:
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.HealerGreen);
                        ImGui.BeginMenu($"Last upload: Success ({state.Response}) ({state.Count} paths)", false);
                        ImGui.PopStyleColor();
                        break;
                    case UploadState.Status.FaultedLocally:
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                        var windowPos = ImGui.GetWindowPos();
                        var pos = windowPos + ImGui.GetCursorPos();
                        ImGui.BeginMenu("Last upload: Faulted Locally", false);
                        var size = ImGui.GetItemRectSize();
                        var pos2 = pos + size;

                        ImGui.PopStyleColor();

                        var clicked = false;
                        if (ImGui.IsMouseHoveringRect(pos, pos2, false))
                        {
                            clicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
                            ImGui.BeginTooltip();
                            ImGui.Text("Click to log last exception to the Plugin Log for troubleshooting.");
                            ImGui.EndTooltip();
                        }

                        if (clicked)
                            _plugin.Uploader.LogUploadExceptions();
                        break;
                    case UploadState.Status.FaultedRemotely:
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                        ImGui.BeginMenu($"Last upload: Faulted Remotely ({state.Response})", false);
                        ImGui.PopStyleColor();
                        break;
                }

                ImGui.PopStyleVar();
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

        _isFiltered = !string.IsNullOrEmpty(_textFilter) || _levelFilter != HookType.None || !_plugin.Configuration.LogNonexistentPaths;

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
                        DalamudApi.PluginLog.Error(text);
                        ImGui.TextUnformatted(text);
                    }
                    else
                    {
                        if (!line.Info.Exists)
                            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);

                        ImGui.TextUnformatted(GetTextForLogEventLevel(line.HookType));
                        ImGui.SameLine();
                        ImGui.SetCursorPosX(cursorLogLine);
                        ImGui.TextUnformatted(line.Info.FullText);

                        if (!line.Info.Exists)
                            ImGui.PopStyleColor();

                        if (ImGui.IsItemHovered() && _plugin.Configuration.HashTooltip)
                        {
                            DrawLineTooltip(line.Info);
                        }
                    }
                }
            }

            clipper.End();
        }

        ImGui.PopFont();
        ImGui.PopStyleVar();

        if (_plugin.Configuration.AutoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
        {
            ImGui.SetScrollHereY(1.0f);
        }

        // Draw dividing line
        var offset = ImGuiHelpers.GlobalScale * 28;
        childDrawList.AddLine(new Vector2(childPos.X + offset, childPos.Y), new Vector2(childPos.X + offset, childPos.Y + childSize.Y), 0x4FFFFFFF, 1.0f);

        ImGui.EndChild();
    }

    private void DrawLineTooltip(ExistsResult info)
    {
        var color = info.Exists ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey;

        if (_tooltipLock) return;
        _tooltipLock = true;
        ImGui.BeginTooltip();
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        var fileText = $"{info.FolderText}: {info.FolderHash:X} ({info.FolderHash})";
        var folderText = $"{info.FileText}: {info.FileHash:X} ({info.FileHash})";
        var fullText = $"{info.FullText}: {info.FullHash:X} ({info.FullHash})";
        ImGui.TextUnformatted(fileText);
        ImGui.TextUnformatted(folderText);
        ImGui.TextUnformatted(fullText);
        ImGui.PopStyleColor();
        ImGui.TextUnformatted("Left click to copy game path to clipboard.");
        ImGui.TextUnformatted("Right click to copy full data to clipboard.");
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            var toCopy = $"{info.FullText}";
            ImGui.SetClipboardText(toCopy);
        }
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            var toCopy = $"{fileText}\n{folderText}\n{fullText}";
            ImGui.SetClipboardText(toCopy);
        }

        if (!info.Exists)
            ImGui.TextUnformatted("* This path is gray because it is not present in your index files.");

        ImGui.EndTooltip();
    }

    private void AddAndFilter(ExistsResult info, HookType level)
    {
        // if (!info.Exists)
        // {
        //     DalamudApi.PluginLog.Error($"{info.FullText} doesn't exist: {info}");
        // };

        lock (_renderLock)
        {
            if (!_entries.TryGetValue(info.ExtendedHash, out var entry))
            {
                entry = new LogEntry(info, level);
                _entries.Add(info.ExtendedHash, entry);
            }

            _logText.Add(info.ExtendedHash);
            _uniqueLogText.Add(info.ExtendedHash);

            if (!_isFiltered) return;
            if (!IsFilterApplicable(entry)) return;

            _filteredLogText.Add(info.ExtendedHash);
            _filteredUniqueLogText.Add(info.ExtendedHash);
        }
    }

    private bool IsFilterApplicable(LogEntry entry)
    {
        if (_levelFilter != HookType.None)
        {
            if (entry.HookType != _levelFilter)
                return false;
        }

        if (!_plugin.Configuration.LogNonexistentPaths && !entry.Info.Exists)
            return false;

        if (!string.IsNullOrEmpty(_textFilter) && !entry.Info.FullText.Contains(_textFilter))
            return false;

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
        HookType.Unknown => "[U]",
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