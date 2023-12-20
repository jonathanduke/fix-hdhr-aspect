using System.Net.Sockets;

namespace JonathanDuke.FixHdhrAspect;

public abstract class ProxyStream : IDisposable
{
    private bool _isDisposed;
    private TcpClient? _client;
    private NetworkStream? _clientStream;
    private TcpClient? _device;
    private NetworkStream? _deviceStream;

    protected readonly ProxyService _service;
    protected readonly ILogger _logger;

    protected virtual bool HttpBuffering => false;
    protected virtual bool DisconnectAfterFirstRead => false;

    protected ProxyStream(ProxyService service, ILogger logger)
    {
        _service = service;
        _logger = logger;
    }

    public async Task OpenAsync(TcpClient client, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, typeof(ProxyStream));

        _client = client;
        await StreamAsync(cancellationToken);
    }

    protected abstract Task ConnectAsync(TcpClient client, CancellationToken cancellationToken);

    private async Task StreamAsync(CancellationToken cancellationToken)
    {
        _clientStream = _client!.GetStream();
        await ConnectAsync(_device = new TcpClient(), cancellationToken);

        if (!cancellationToken.IsCancellationRequested && _device.Connected)
        {
            _deviceStream = _device.GetStream();

            MemoryStream? responseBuffer = HttpBuffering ? new MemoryStream() : null;
            var buffer = new byte[4096];
            var count = await _clientStream.ReadAsync(buffer, cancellationToken);

            if (!cancellationToken.IsCancellationRequested && count > 0)
            {
                InspectRequest(ref buffer, ref count);
                await _deviceStream.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                bool deviceDisconnected = false;

                if (!_device.Connected)
                {
                    deviceDisconnected = true;
                }
                else
                {
                    try
                    {
                        count = await _deviceStream.ReadAsync(buffer, cancellationToken);

                        if (HttpBuffering)
                        {
                            responseBuffer!.Write(buffer, 0, count);
                        }

                        if (!cancellationToken.IsCancellationRequested && count == 0)
                        {
                            deviceDisconnected = true;
                        }
                    }
                    catch (IOException)
                    {
                        deviceDisconnected = true;
                    }
                }

                if (HttpBuffering)
                {
                    if (!deviceDisconnected)
                    {
                        continue;
                    }
                    else if (responseBuffer != null)
                    {
                        buffer = responseBuffer!.ToArray();
                        count = buffer.Length;
                        responseBuffer.Close();
                        responseBuffer = null;
                    }
                    else
                    {
                        break;
                    }
                }
                else if (deviceDisconnected)
                {
                    OnLostDeviceConnection();
                    _client.Close();
                    break;
                }
                else if (DisconnectAfterFirstRead)
                {
                    _device.Close();
                }

                if (!cancellationToken.IsCancellationRequested && _clientStream.CanWrite)
                {
                    InspectResponse(ref buffer, ref count);

                    if (count > 0)
                    {
                        try
                        {
                            await _clientStream.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                        }
                        catch (IOException)
                        {
                            OnClientDisconnected();
                            _device.Close();
                            break;
                        }
                    }
                }
            }
        }
    }

    protected virtual void OnClientDisconnected()
    {
    }

    protected virtual void OnLostDeviceConnection()
    {
    }

    protected virtual void InspectRequest(ref byte[] buffer, ref int count)
    {
    }

    protected virtual void InspectResponse(ref byte[] buffer, ref int count)
    {
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                _deviceStream?.Dispose();
                _device?.Dispose();
                _clientStream?.Dispose();
                _client?.Dispose();
            }

            _deviceStream = null;
            _device = null;
            _clientStream = null;
            _client = null;
            _isDisposed = true;
        }
    }

    public void Close()
    {
        Dispose(disposing: true);
    }

    void IDisposable.Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }
}
