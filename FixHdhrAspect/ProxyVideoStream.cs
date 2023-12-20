using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace JonathanDuke.FixHdhrAspect;

public partial class ProxyVideoStream : ProxyStream
{
    [GeneratedRegex(@"^GET /(?:.*?/)*v(\d+\.\d+)", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex ChannelRegex();
    private readonly static Regex _rxChannel = ChannelRegex();

    private string? _channel;
    private int _state = 0;
    private MpegAspectRatio _aspectRatio = 0;

    public ProxyVideoStream(ProxyService service, ILogger logger)
        : base(service, logger)
    {
    }

    protected override async Task ConnectAsync(TcpClient client, CancellationToken cancellationToken)
    {
        await client.ConnectAsync(_service!.DeviceHostName, _service!.DeviceCapturePort, cancellationToken);
    }

    protected override void OnClientDisconnected()
    {
        _logger.LogInformation("Connection to channel {Channel} was disconnected at the client.", _channel);
        base.OnClientDisconnected();
    }

    protected override void OnLostDeviceConnection()
    {
        _logger.LogInformation("Connection to channel {Channel} on the capture device was lost.", _channel);
        base.OnLostDeviceConnection();
    }

    protected override void InspectRequest(ref byte[] buffer, ref int count)
    {
        if (_channel == null)
        {
            var request = Encoding.UTF8.GetString(buffer, 0, count);
            Match match;

            if ((match = _rxChannel.Match(request)).Success)
            {
                _channel = match.Groups[1].Value;

                if (_service.Channels.TryGetValue(_channel, out MpegAspectRatio value) && value != MpegAspectRatio.Default)
                {
                    _aspectRatio = value;
                    _logger.LogInformation("Streaming channel {Channel} with overridden aspect ratio of {Aspect}.", _channel, value.ToRatioString());
                }
                else
                {
                    _logger.LogInformation("Streaming channel {Channel} with default aspect ratio.", _channel);
                }
            }
        }
    }

    protected override void InspectResponse(ref byte[] buffer, ref int count)
    {
        if (_aspectRatio > 0)
        {
            for (int i = 0; i < count; i++)
            {
                if (_state == 0 && buffer[i] == 0x00)
                {
                    _state = 1;
                }
                else if (_state == 1)
                {
                    _state = buffer[i] == 0x00 ? 2 : 0;
                }
                else if (_state == 2)
                {
                    _state = buffer[i] == 0x01 ? 3 : 0;
                }
                else if (_state == 3)
                {
                    _state = buffer[i] == 0xB3 ? 4 : 0;
                }
                else if (_state == 4)
                {
                    _state = (buffer[i] & 0xFE) == 0x2C ? 5 : 0; // allow 720 or 704
                }
                else if (_state == 5)
                {
                    _state = buffer[i] == 0x01 ? 6 : 0;
                }
                else if (_state == 6)
                {
                    _state = buffer[i] == 0xE0 ? 7 : 0;
                }
                else if (_state == 7)
                {
                    if ((buffer[i] & 0xEF) == 0x24)
                    {
                        buffer[i] = (byte)(buffer[i] & 0x0F | (byte)_aspectRatio << 4);
                    }

                    _state = 0;
                }
            }
        }
    }
}
