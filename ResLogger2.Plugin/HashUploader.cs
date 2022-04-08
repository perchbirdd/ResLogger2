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
    private const string Endpoint = "https://rl2.perchbird.dev/upload";
    // private const string Endpoint = "http://127.0.0.1:5000/upload";

    private readonly ResLogger2 _plugin;
    private readonly ElapsedEventHandler _uploadDelegate;
    private readonly CancellationTokenSource _tokenSource;
    private readonly HttpClient _client;
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
            Response = HttpStatusCode.UnavailableForLegalReasons
        };

        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

        // var handler = 
        _client = new HttpClient();
        _timer = new Timer();

        _uploadDelegate = (_, _) => Upload();
        _tokenSource = new CancellationTokenSource();

        _timer.Interval = 5000;
        _timer.Elapsed += _uploadDelegate;
        _timer.Start();
    }

    public void Dispose()
    {
        _tokenSource.Cancel();
        _timer.Stop();
        _timer.Elapsed -= _uploadDelegate;
        _timer.Dispose();
        _client.Dispose();
    }

    private void Upload()
    {
        if (_isUploading || !_plugin.Configuration.Upload) return;
            
        _isUploading = true;

        Task.Run(async () => {
            var data = await _plugin.Database.GetUploadableData();
            if (data.Entries.Count == 0)
            {
                _isUploading = false;
                return;
            }
                
            PluginLog.Verbose($"[Upload] Data has {data.Entries.Count} index2s.");
            var text = JsonConvert.SerializeObject(data);
            var textCompressed = StringCompressor.CompressString(text);
            var content = new StringContent(textCompressed, Encoding.UTF8, "application/rl2");
            
            State.UploadStatus = UploadState.Status.Uploading;
            State.Response = HttpStatusCode.UnavailableForLegalReasons;
            State.Count = -1;
            
            var response = await _client.PostAsync(Endpoint, content, _tokenSource.Token);
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