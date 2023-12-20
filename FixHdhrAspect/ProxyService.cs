using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace JonathanDuke.FixHdhrAspect;

public partial class ProxyService : IDisposable
{
    [GeneratedRegex(":80$", RegexOptions.Compiled)]
    private static partial Regex Port80SuffixRegex();
    internal static readonly Regex RegexPort80Suffix = Port80SuffixRegex();

    [GeneratedRegex(@"(?<=\bDevice\s*ID\""*\s*:\s*\""*)[0-9A-F]+", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex DeviceIdRegex();
    internal static readonly Regex RegexDeviceId = DeviceIdRegex();

    [GeneratedRegex(@"^\s*0*([1-9]\d*)\s*[-.]\s*([1-9]\d*)\s*$", RegexOptions.Compiled)]
    private static partial Regex ChannelRegex();
    private static readonly Regex _rxChannel = ChannelRegex();

    private const string DefaultDeviceHostName = "hdhomerun.local";
    private const int DefaultDeviceCapturePort = 5004;
    private const int DefaultDeviceWebPort = 80;
    private const string DefaultServiceEndpoint = "0.0.0.0";

    private bool _isDisposed;
    private readonly ConcurrentDictionary<Task, ProxyStream> _requests = new();
    protected readonly ILogger _logger;

    public string DeviceHostName { get; private set; }
    public int DeviceCapturePort { get; private set; }
    public int DeviceWebPort { get; private set; }
    public string? ServiceHostName { get; private set; }
    public IPAddress ServiceEndpoint { get; private set; }
    public IPAddress? ServiceEndpointIPv6 { get; private set; }
    public int ServiceCapturePort { get; private set; }
    public int ServiceWebPort { get; private set; }
    public bool ProxyAllChannels { get; private set; }

    internal readonly Regex HostReplaceRegex;

    internal IReadOnlyDictionary<string, MpegAspectRatio> Channels { get; private set; }
    internal string? LastHostName { get; set; }
    internal string? OriginalDeviceId { get; private set; }
    internal Regex? OriginalDeviceRegex { get; private set; }

    internal DeviceIdentifier? ProxyDeviceId
    {
        get;
        private set;
    }

    public ProxyService(IConfiguration configuration, ILogger<Worker> logger)
    {
        _logger = logger;
        var settings = configuration.GetSection("Settings");
        DeviceHostName = settings.GetValue<string>(nameof(DeviceHostName)) is string deviceHostName && !string.IsNullOrWhiteSpace(deviceHostName) ? deviceHostName : DefaultDeviceHostName;
        DeviceCapturePort = settings.GetValue(nameof(DeviceCapturePort), DefaultDeviceCapturePort);
        DeviceWebPort = settings.GetValue(nameof(DeviceWebPort), DefaultDeviceWebPort);
        ServiceHostName = settings.GetValue<string>(nameof(ServiceHostName)) is string serviceHostName && !string.IsNullOrWhiteSpace(serviceHostName) ? serviceHostName : null;
        ServiceEndpoint = IPAddress.Parse(settings.GetValue<string>(nameof(ServiceEndpoint)) is string serviceEndpoint && !string.IsNullOrWhiteSpace(serviceEndpoint) ? serviceEndpoint : DefaultServiceEndpoint);

        if (ServiceEndpoint.AddressFamily != AddressFamily.InterNetworkV6)
        {
            // if the service endpoint is IPv4, we can also add a secondary IPv6 address
            if (settings.GetValue<string>(nameof(ServiceEndpointIPv6)) is string value && !string.IsNullOrWhiteSpace(value))
            {
                if (IPAddress.TryParse(value, out IPAddress? ipv6) && ipv6!.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    ServiceEndpointIPv6 = ipv6;
                }
            }
        }

        // if the capture/web ports are not set for the service, it is assumed they should match the device ports
        ServiceCapturePort = settings.GetValue(nameof(ServiceCapturePort), DeviceCapturePort);
        ServiceWebPort = settings.GetValue(nameof(ServiceWebPort), DeviceWebPort);
        ProxyAllChannels = settings.GetValue(nameof(ProxyAllChannels), false);

        _logger.LogInformation("Settings:\n\t{Key1}={Value1}\n\t{Key2}={Value2}\n\t{Key3}={Value3}\n\t{Key4}={Value4}\n\t{Key5}={Value5}\n\t{Key6}={Value6}\n\t{Key7}={Value7}\n\t{Key8}={Value8}\n\t{Key9}={Value9}",
            nameof(DeviceHostName), DeviceHostName,
            nameof(DeviceCapturePort), DeviceCapturePort,
            nameof(DeviceWebPort), DeviceWebPort,
            nameof(ServiceHostName), ServiceHostName,
            nameof(ServiceEndpoint), ServiceEndpoint,
            nameof(ServiceEndpointIPv6), ServiceEndpointIPv6,
            nameof(ServiceCapturePort), ServiceCapturePort,
            nameof(ServiceWebPort), ServiceWebPort,
            nameof(ProxyAllChannels), ProxyAllChannels);

        HostReplaceRegex = new Regex(@"(hdhr-[0-9a-z]+\.local|" + Regex.Escape(DeviceHostName) + @")(:(?<Port>\d+))?(?<Path>/([\w.-]+/)*v(?<Channel>\d+\.\d+))?", RegexOptions.Compiled);

        var channels = new Dictionary<string, MpegAspectRatio>();
        var channelList = new StringBuilder();

        foreach (var pair in configuration.GetRequiredSection("Settings:Channels").Get<IDictionary<string, string>>()!)
        {
            string key = _rxChannel.IsMatch(pair.Key) ? _rxChannel.Replace(pair.Key, "$1.$2") : string.Empty;

            if (!Enum.TryParse(pair.Value.FromAspectRatio(), out MpegAspectRatio value))
            {
                value = MpegAspectRatio.Default;
                _logger.LogWarning("Invalid channel override setting ignored: {Channel} => {Aspect}", pair.Key, pair.Value);
            }

            if (!string.IsNullOrEmpty(key) && value != MpegAspectRatio.Default)
            {
                channels.Add(key, value);
                channelList.Append($"\n\t{key} => {value.ToRatioString()}");
            }
        }

        if (channelList.Length > 0)
        {
            _logger.LogInformation("Channels to override:{List}", channelList);
        }
        else
        {
            _logger.LogWarning("No valid channel overrides are defined. The service will still run, but no streams will be altered.");
        }

        Channels = channels;
        _ = DetectOriginalDeviceId();
    }

    private async Task DetectOriginalDeviceId()
    {
        using var http = new HttpClient();

        try
        {
            var response = await http.GetStringAsync(RegexPort80Suffix.Replace($"http://{DeviceHostName}:{DeviceWebPort}/", ""));
            DetectOriginalDeviceId(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect device ID on startup.");
        }
    }

    internal void DetectOriginalDeviceId(string htmlResponse)
    {
        if (OriginalDeviceId == null)
        {
            var match = RegexDeviceId.Match(htmlResponse);

            if (match.Success)
            {
                OriginalDeviceId = match.Value;
#if DEBUG
                _logger.LogDebug("Device ID: {Device}", OriginalDeviceId);
#endif
                OriginalDeviceRegex = new Regex(@"(?<!\?DeviceID=|hdhr-)" + match.Value, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                ProxyDeviceId = AlterDeviceId(match.Value);
            }
        }
    }

    private static DeviceIdentifier? AlterDeviceId(string originalDeviceId)
    {
        if (DeviceIdentifier.TryParse(originalDeviceId, out DeviceIdentifier? deviceId))
        {
            uint newId = (uint)deviceId! ^ 0x00000010;

            for (uint i = 0; i < 16; i++)
            {
                newId = (newId & 0xFFFFFFF0) | i;

                if (DeviceIdentifier.CalculateChecksum(newId) == 0)
                {
                    return new DeviceIdentifier(newId);
                }
            }
        }

        return null;
    }

    private async Task TcpListenAsync<T>(Func<T> streamFactory, IEnumerable<TcpListener> listeners, CancellationToken cancellationToken) where T : ProxyStream
    {
        ObjectDisposedException.ThrowIf(_isDisposed, typeof(ProxyService));

        foreach (var listener in listeners)
        {
            listener.Start();
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                bool nop = true;

                foreach (var listener in listeners)
                {
                    if (listener.Pending())
                    {
                        try
                        {
                            nop = false;
                            var stream = streamFactory();
                            var client = await listener.AcceptTcpClientAsync(cancellationToken);
                            _requests.TryAdd(stream.OpenAsync(client, cancellationToken), stream);
                        }
                        catch (TaskCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Unexpected TCP listener error.");
                        }
                    }
                }

                if (nop)
                {
                    DisposeFinishedRequests();
                    await Task.Delay(1, cancellationToken);
                }
            }
        }
        catch (TaskCanceledException)
        {
        }
        finally
        {
            foreach (var listener in listeners)
            {
                listener.Stop();
            }
        }
    }

    private void DisposeFinishedRequests()
    {
        if (_requests?.Count > 0)
        {
            var completed = _requests.Where(r => true == r.Key?.IsCompleted).ToArray();

            foreach (var request in completed)
            {
                if (_requests.TryRemove(request.Key, out _))
                {
                    request.Value.Close();
                    request.Key.Dispose();
                }
            }
        }
    }

    public Task StartCaptureAsync(CancellationToken cancellationToken)
    {
        var listeners = new List<TcpListener>
        {
            new(ServiceEndpoint, ServiceCapturePort)
        };

        if (ServiceEndpointIPv6 != null)
        {
            listeners.Add(new TcpListener(ServiceEndpointIPv6, ServiceCapturePort));
        }

        return TcpListenAsync(() => new ProxyVideoStream(this, _logger), listeners, cancellationToken);
    }

    public Task StartWebAsync(CancellationToken cancellationToken)
    {
        var listeners = new List<TcpListener>
        {
            new(ServiceEndpoint, ServiceWebPort)
        };

        if (ServiceEndpointIPv6 != null)
        {
            listeners.Add(new TcpListener(ServiceEndpointIPv6, ServiceWebPort));
        }

        return TcpListenAsync(() => new ProxyHttpStream(this, _logger), listeners, cancellationToken);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                DisposeFinishedRequests();
            }

            _requests?.Clear();
            _isDisposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
