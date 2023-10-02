using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using Newtonsoft.Json;
using ResLogger2.Common.Api;

namespace ResLogger2.Plugin.Windows;

public class StatsWindow : Window
{
	private readonly ResLogger2 _plugin;
	private readonly CancellationTokenSource _tokenSource;
	private StatsData? _data;
	private Exception _exception;

	public StatsWindow(ResLogger2 plugin) : base("ResLogger2 Stats", ImGuiWindowFlags.MenuBar)
	{
		_plugin = plugin;
		_tokenSource = new CancellationTokenSource();

		Size = new Vector2(500, 400);
		SizeCondition = ImGuiCond.FirstUseEver;
		RespectCloseHotkey = true;
	}

	public void Dispose()
	{
		_tokenSource.Cancel();
	}

	private void FetchData()
	{
		_data = null;
		_exception = null;
		Task.Run(() => Api.Client.GetAsync(Api.StatsEndpoint, _tokenSource.Token).Result, _tokenSource.Token)
			.ContinueWith(data =>
			{
				if (data.Result.IsSuccessStatusCode)
				{
					var content = data.Result.Content.ReadAsStringAsync().Result;
					_data = JsonConvert.DeserializeObject<StatsData>(content);
				}
				else
				{
					throw new Exception(data.Result.ReasonPhrase);
				}
			}, _tokenSource.Token).ContinueWith(task =>
			{
				_exception = task.Exception;
			}, _tokenSource.Token, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
	}

	public override void OnOpen()
	{
		FetchData();
	}

	public override void Draw()
	{
		DrawMenuBar();
		DrawContent();
	}

	private void DrawMenuBar()
	{
		if (ImGui.BeginMenuBar())
		{
			if (ImGui.BeginMenu("Menu"))
			{
				if (ImGui.MenuItem("Refresh"))
					FetchData();
				ImGui.EndMenu();	
			}
			ImGui.EndMenuBar();
		}
	}

	private void DrawContent()
	{
		if (_data == null && _exception == null)
		{
			ImGui.Text("Please wait...");
		}
		else if (_data == null && _exception != null)
		{
			ImGui.TextWrapped("An error occurred while fetching data:");
			ImGui.TextWrapped(_exception.Message ?? "");
			ImGui.TextWrapped(_exception.StackTrace ?? "");
		} else if (_data != null)
		{
			if (ImGui.BeginTable("rl2stats", 7,ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY))
			{
				ImGui.TableSetupColumn("Index", ImGuiTableColumnFlags.WidthFixed);
				ImGui.TableSetupColumn("Total Paths", ImGuiTableColumnFlags.WidthFixed);
				ImGui.TableSetupColumn("Found Paths", ImGuiTableColumnFlags.WidthFixed);
				ImGui.TableSetupColumn("Percentage", ImGuiTableColumnFlags.WidthFixed);
				ImGui.TableSetupColumn("Current Version Paths", ImGuiTableColumnFlags.WidthFixed);
				ImGui.TableSetupColumn("Current Version Found Paths", ImGuiTableColumnFlags.WidthFixed);
				ImGui.TableSetupColumn("Current Version Percentage", ImGuiTableColumnFlags.WidthStretch);
				ImGui.TableHeadersRow();

				foreach (var index in _data!.Value.Totals.Keys)
				{
					var hasTotals = _data!.Value.Totals.TryGetValue(index, out var totals);
					var hasPossible = _data!.Value.Possible.TryGetValue(index, out var possible);
					
					ImGui.TableNextRow();
					if (hasTotals)
					{
						ImGui.TableSetColumnIndex(0);
						ImGui.Text(index.ToString("X6"));
						ImGui.TableNextColumn();
						ImGui.Text(totals.TotalPaths.ToString());
						ImGui.TableNextColumn();
						ImGui.Text(totals.Paths.ToString());
						ImGui.TableNextColumn();
						var frac = totals.Paths / (float)totals.TotalPaths;
						ImGui.ProgressBar(frac, new Vector2(200f, 0f), $"{frac * 100:F2}%");
					}
					else
					{
						ImGui.TableSetColumnIndex(3);
					}
					
					if (hasPossible)
					{
						ImGui.TableNextColumn();
						ImGui.Text(possible.TotalPaths.ToString());
						ImGui.TableNextColumn();
						ImGui.Text(possible.Paths.ToString());
						ImGui.TableNextColumn();
						var fracPoss = possible.Paths / (float)possible.TotalPaths;
						ImGui.ProgressBar(fracPoss, new Vector2(200f, 0f), $"{fracPoss * 100:F2}%");
					}
					else
					{
						ImGui.TableNextColumn();
						ImGui.Text("0");
						ImGui.TableNextColumn();
						ImGui.Text("0");
						ImGui.TableNextColumn();
						var fracPoss = 1.0f;
						ImGui.ProgressBar(fracPoss, new Vector2(200f, 0f), $"{fracPoss * 100:F2}%");
					}
				}
				ImGui.EndTable();
			}
		}
	}
}