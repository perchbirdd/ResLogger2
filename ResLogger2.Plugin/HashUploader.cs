using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Dalamud.Logging;
using Newtonsoft.Json;
using Timer = System.Timers.Timer;

namespace ResLogger2.Plugin;

public class HashUploader : IDisposable
{
    private const int UploadInterval = 5000;
    private const int UploadLimit = 250;

    private readonly ResLogger2 _plugin;
    private readonly ElapsedEventHandler _uploadDelegate;
    private readonly CancellationTokenSource _tokenSource;
    private readonly Timer _timer;
    private AggregateException _lastException;
    private bool _isUploading;
    
    public UploadState State { get; init; }

    public HashUploader(ResLogger2 plugin)
    {
        _plugin = plugin;

        State = new UploadState
        {
            UploadStatus = UploadState.Status.Idle,
            Count = -1,
            Response = HttpStatusCode.UnavailableForLegalReasons,
        };
        
        _timer = new Timer();

        _uploadDelegate = (_, _) => Upload();
        _tokenSource = new CancellationTokenSource();

        _timer.Interval = UploadInterval;
        _timer.Elapsed += _uploadDelegate;
        _timer.Start();
    }

    public void Dispose()
    {
        _tokenSource.Cancel();
        _timer.Stop();
        _timer.Elapsed -= _uploadDelegate;
        _timer.Dispose();
    }

    private void Upload()
    {
        if (_isUploading || !_plugin.Configuration.Upload) return;
            
        _isUploading = true;

        Task.Run(async () => {
            var data = await _plugin.Database.GetUploadableData(UploadLimit);
            if (data.Entries.Count == 0)
            {
                _isUploading = false;
                return;
            }
                
            PluginLog.Verbose($"[Upload] Data has {data.Entries.Count} index2s.");
            var text = JsonConvert.SerializeObject(data);
            
            using var httpContent = new StringContent(text, Encoding.UTF8, "application/json");
            using var content = new CompressedContent(httpContent, "gzip");

            State.UploadStatus = UploadState.Status.Uploading;
            State.Response = HttpStatusCode.UnavailableForLegalReasons;
            State.Count = -1;
            
            // var response = await _client.PostAsync(Endpoint, content, _tokenSource.Token);
            var response = await Api.Client.PostAsync(Api.UploadEndpoint, httpContent, _tokenSource.Token);
            State.Response = response.StatusCode;
                
            PluginLog.Verbose($"result: {response.StatusCode}");

            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                PluginLog.Verbose("[Upload] Status was accepted, setting data as uploaded.");
                _plugin.Database.SetUploaded(data);
                State.UploadStatus = UploadState.Status.Success;
                State.Response = response.StatusCode;
                State.Count = data.Entries.Count;
            }
            else
            {
                State.UploadStatus = UploadState.Status.FaultedRemotely;
            }
            _isUploading = false;
        }, _tokenSource.Token).ContinueWith(task =>
        {
            _lastException = task.Exception;
            // LogUploadExceptions();
            
            // If this is still "Uploading", then we never even managed to connect to the server
            State.UploadStatus = State.UploadStatus == UploadState.Status.Uploading
                                ? UploadState.Status.FaultedLocally 
                                : UploadState.Status.FaultedRemotely;
            State.Count = -1;

            _isUploading = false;
        }, _tokenSource.Token, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    public void LogUploadExceptions()
    {
        PluginLog.Error(_lastException, "An exception occurred while uploading ResLogger2 data.");
        foreach (var ex in _lastException.InnerExceptions)
            PluginLog.Error(ex, "Nested exception(s) occurred while uploading ResLogger2 data.");
    }
}

public class UploadState
{
    public enum Status
    {
        Idle,
        Uploading,
        Success,
        FaultedLocally,
        FaultedRemotely
    }
    
    public Status UploadStatus { get; set; }
    public HttpStatusCode Response { get; set; }
    public int Count { get; set; }
}