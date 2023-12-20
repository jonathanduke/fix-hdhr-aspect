using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace JonathanDuke.FixHdhrAspect;

public partial class ProxyHttpStream : ProxyStream
{
    [GeneratedRegex(@"(?<=^\s*Host:\s+)(?<Full>(?<Host>[^:\r\n]+)(:(?<Port>\d+))?)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex HostHeaderRegex();
    private static readonly Regex _rxHostHeader = HostHeaderRegex();

    [GeneratedRegex(@"(?<=^\s*Referer:\s+http(s)?://+)[^/\r\n]+", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex ReferrerHeaderRegex();
    private static readonly Regex _rxRefererHeader = ReferrerHeaderRegex();

    [GeneratedRegex(@"(?<=^\s*Content-Length:\s+)(?<Length>\d+)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex ContentLengthHeaderRegex();
    private static readonly Regex _rxContentLengthHeader = ContentLengthHeaderRegex();

    public ProxyHttpStream(ProxyService service, ILogger logger)
        : base(service, logger)
    {
    }

    protected override bool HttpBuffering => true;

    protected override async Task ConnectAsync(TcpClient client, CancellationToken cancellationToken)
    {
        await client.ConnectAsync(_service!.DeviceHostName, _service!.DeviceWebPort, cancellationToken);
    }

    protected override void InspectRequest(ref byte[] buffer, ref int count)
    {
        var altered = false;
        var request = Encoding.UTF8.GetString(buffer, 0, count);
#if DEBUG
        _logger.LogDebug("Original HTTP Request:\n{Request}", request);
#endif
        if (_service.ServiceHostName == null)
        {
            var match = _rxHostHeader.Match(request);

            if (match.Success && _service.LastHostName != match.Groups["Host"].Value)
            {
                _service.LastHostName = match.Groups["Host"].Value;
#if DEBUG
                _logger.LogDebug("Service Host Name: {Host}", _service.LastHostName);
#endif
            }
        }

        var hostReplace = ProxyService.RegexPort80Suffix.Replace(_service!.DeviceHostName + ":" + _service!.DeviceWebPort, "");
        request = _rxRefererHeader.Replace(_rxHostHeader.Replace(request, (match) =>
        {
            altered = true;
            return hostReplace;
        }), (match) =>
        {
            altered = true;
            return hostReplace;
        });

        if (altered)
        {
#if DEBUG
            _logger.LogDebug("Modified HTTP Request:\n{Request}", request);
#endif
            buffer = Encoding.UTF8.GetBytes(request, 0, request.Length);
            count = buffer.Length;
        }
    }

    protected override void InspectResponse(ref byte[] buffer, ref int count)
    {
        var altered = false;
        var response = Encoding.UTF8.GetString(buffer, 0, count);

        if (_service.OriginalDeviceId == null)
        {
            _service.DetectOriginalDeviceId(response);
        }

#if DEBUG
        _logger.LogDebug("Original HTTP Response:\n{Response}", response);
#endif
        var hostName = _service!.ServiceHostName ?? _service.LastHostName ?? _service.ServiceEndpoint.ToString();
        response = _service!.HostReplaceRegex.Replace(response, (match) =>
        {
            if (match.Groups["Port"].Value == _service.DeviceCapturePort.ToString())
            {
                if (_service.ProxyAllChannels || (match.Groups["Channel"].Success && _service.Channels.TryGetValue(match.Groups["Channel"].Value, out MpegAspectRatio ratio) && ratio != MpegAspectRatio.Default))
                {
                    altered = true;
                    return string.Concat(hostName, ':', _service.ServiceCapturePort, match.Groups["Path"].Value);
                }
            }
            else // this is a web URL, not a capture URL
            {
                altered = true;
                return ProxyService.RegexPort80Suffix.Replace(string.Concat(hostName, ':', _service.ServiceWebPort), "");
            }

            return match.Value;
        });

        if (_service.OriginalDeviceRegex != null && _service.OriginalDeviceId != null)
        {
            response = _service.OriginalDeviceRegex.Replace(response, (match) =>
            {
                altered = true;
                return _service.ProxyDeviceId!.ToString();
            });
        }

        if (altered)
        {
            int oldCount = count;
            buffer = Encoding.UTF8.GetBytes(response, 0, response.Length);
            int newCount = count = buffer.Length;

            response = _rxContentLengthHeader.Replace(response, (match) =>
            {
                int oldValue = int.Parse(match.Groups["Length"].Value);
                int newValue = oldValue + newCount - oldCount;
                string replacement = newValue.ToString();

                if (replacement.Length != match.Groups["Length"].Value.Length)
                {
                    newValue += replacement.Length - match.Groups["Length"].Value.Length;
                    replacement = newValue.ToString();
                }

                return replacement;
            });

            buffer = Encoding.UTF8.GetBytes(response, 0, response.Length);
            count = buffer.Length;
#if DEBUG
            _logger.LogDebug("Modified HTTP Response:\n{Response}", response);
#endif
        }
    }
}
