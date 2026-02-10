using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Armament.Net;

public sealed class UdpServerTransport : IAsyncDisposable
{
    private readonly UdpClient _udpClient;
    private readonly ConcurrentQueue<ReceivedDatagram> _inbox = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _receiveLoop;

    public UdpServerTransport(int port)
    {
        _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, port));
    }

    public int Port => ((IPEndPoint)_udpClient.Client.LocalEndPoint!).Port;

    public void Start()
    {
        _receiveLoop = Task.Run(ReceiveLoopAsync);
    }

    public bool TryDequeue(out ReceivedDatagram datagram)
    {
        return _inbox.TryDequeue(out datagram!);
    }

    public void Send(IPEndPoint endpoint, ReadOnlySpan<byte> payload)
    {
        _udpClient.Send(payload.ToArray(), payload.Length, endpoint);
    }

    private async Task ReceiveLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(_cts.Token);
                _inbox.Enqueue(new ReceivedDatagram(result.RemoteEndPoint, result.Buffer));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                // Keep transport alive for Phase 0 smoke path.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_receiveLoop is not null)
        {
            await _receiveLoop;
        }

        _udpClient.Dispose();
        _cts.Dispose();
    }
}

public readonly record struct ReceivedDatagram(IPEndPoint Endpoint, byte[] Payload);
