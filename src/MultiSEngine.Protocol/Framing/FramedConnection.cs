using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using TrProtocol;

namespace MultiSEngine.Protocol.Framing;

public delegate ValueTask InboundFrameHandler(ReadOnlySequence<byte> frame, CancellationToken cancellationToken);

public sealed class FramedConnection : IAsyncDisposable
{
    private static readonly AsyncLocal<FramedConnection?> CurrentReadLoopConnection = new();

    private readonly TcpClient? _tcpClient;
    private readonly Stream? _stream;
    private readonly PipeReader _inboundReader;
    private readonly PipeWriter _transportWriter;
    private readonly Pipe _outboundPipe;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly FramingOptions _options;
    private readonly Lock _pauseLock = new();

    private TaskCompletionSource? _resumeSignal;
    private InboundFrameHandler? _inboundHandler;
    private Func<Exception?, ValueTask>? _closedHandler;
    private Task? _receiveTask;
    private Task? _outboundPumpTask;
    private int _started;
    private int _disposed;
    private int _closeNotified;

    private FramedConnection(
        TcpClient? tcpClient,
        Stream? stream,
        PipeReader inboundReader,
        PipeWriter transportWriter,
        IPEndPoint remoteEndPoint,
        FramingOptions options)
    {
        _tcpClient = tcpClient;
        _stream = stream;
        _inboundReader = inboundReader;
        _transportWriter = transportWriter;
        RemoteEndPoint = remoteEndPoint;
        _options = options;

        var pause = Math.Max(4096, options.PauseWriterThreshold);
        var resume = Math.Clamp(options.ResumeWriterThreshold, 1024, pause);
        _outboundPipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: pause,
            resumeWriterThreshold: resume,
            useSynchronizationContext: false));
    }

    public IPEndPoint RemoteEndPoint { get; }
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public static FramedConnection FromTcpClient(TcpClient client, FramingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        options ??= new FramingOptions();
        if (options.DisableTcpDelay)
            client.NoDelay = true;

        var remote = client.Client.RemoteEndPoint as IPEndPoint ?? new IPEndPoint(IPAddress.None, 0);
        var stream = client.GetStream();
        var reader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
        var writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));
        return new FramedConnection(client, stream, reader, writer, remote, options);
    }

    public static FramedConnection FromDuplexPipe(IDuplexPipe pipe, IPEndPoint? remoteEndPoint = null, FramingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        return new FramedConnection(
            tcpClient: null,
            stream: null,
            inboundReader: pipe.Input,
            transportWriter: pipe.Output,
            remoteEndPoint: remoteEndPoint ?? new IPEndPoint(IPAddress.Loopback, 0),
            options: options ?? new FramingOptions());
    }

    public void SetInboundHandler(InboundFrameHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Volatile.Write(ref _inboundHandler, handler);
    }

    public void SetClosedHandler(Func<Exception?, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Volatile.Write(ref _closedHandler, handler);
    }

    public void Start()
    {
        if (IsDisposed)
            return;
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            return;

        _outboundPumpTask = PumpOutboundAsync();
        _receiveTask = ReceiveLoopAsync();
    }

    /// <summary>
    /// Suspends inbound frame dispatch at the next frame boundary without detaching or losing packets.
    /// </summary>
    public void PauseReading()
    {
        lock (_pauseLock)
        {
            _resumeSignal ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void ResumeReading()
    {
        TaskCompletionSource? signal;
        lock (_pauseLock)
        {
            signal = _resumeSignal;
            _resumeSignal = null;
        }
        signal?.TrySetResult();
    }

    public ValueTask<bool> WriteFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default)
    {
        if (frame.IsEmpty)
            return ValueTask.FromResult(true);
        if (IsDisposed)
            return ValueTask.FromResult(false);

        return WriteCoreAsync(
            static (writer, state) =>
            {
                var span = writer.GetSpan(state.Length);
                state.Span.CopyTo(span);
                writer.Advance(state.Length);
            },
            frame,
            cancellationToken);
    }

    public ValueTask<bool> WriteFrameAsync(ReadOnlySequence<byte> frame, CancellationToken cancellationToken = default)
    {
        if (frame.IsEmpty)
            return ValueTask.FromResult(true);
        if (IsDisposed)
            return ValueTask.FromResult(false);

        return WriteCoreAsync(
            static (writer, state) =>
            {
                if (state.IsSingleSegment)
                {
                    var span = state.FirstSpan;
                    span.CopyTo(writer.GetSpan(span.Length));
                    writer.Advance(span.Length);
                    return;
                }

                foreach (var segment in state)
                {
                    if (segment.IsEmpty)
                        continue;
                    var dest = writer.GetSpan(segment.Length);
                    segment.Span.CopyTo(dest);
                    writer.Advance(segment.Length);
                }
            },
            frame,
            cancellationToken);
    }

    public async ValueTask<bool> WritePacketAsync(INetPacket packet, bool fromServer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (IsDisposed)
            return false;

        using var rental = PacketFrameCodec.SerializeRented(packet, fromServer);
        return await WriteFrameAsync(rental.Memory, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<bool> WriteRawBatchAsync(IReadOnlyList<ReadOnlyMemory<byte>> frames, CancellationToken cancellationToken = default)
    {
        if (frames.Count == 0)
            return ValueTask.FromResult(true);
        if (IsDisposed)
            return ValueTask.FromResult(false);

        return WriteCoreAsync(
            static (writer, list) =>
            {
                for (var i = 0; i < list.Count; i++)
                {
                    var item = list[i];
                    if (item.IsEmpty)
                        continue;
                    var dest = writer.GetSpan(item.Length);
                    item.Span.CopyTo(dest);
                    writer.Advance(item.Length);
                }
            },
            frames,
            cancellationToken);
    }

    private ValueTask<bool> WriteCoreAsync<TState>(
        Action<PipeWriter, TState> writeAction,
        TState state,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || _cts.IsCancellationRequested || IsDisposed)
            return ValueTask.FromResult(false);

        // Zero-allocation synchronous fast path when uncontended and below backpressure pause threshold.
        if (_writeGate.Wait(0, CancellationToken.None))
        {
            var releaseInFinally = true;
            try
            {
                if (IsDisposed)
                    return ValueTask.FromResult(false);

                writeAction(_outboundPipe.Writer, state);
                var flushTask = _outboundPipe.Writer.FlushAsync(CancellationToken.None);
                if (flushTask.IsCompletedSuccessfully)
                {
                    var flush = flushTask.Result;
                    if (flush.IsCanceled || flush.IsCompleted)
                    {
                        TriggerFaultClose(new IOException("Outbound pipe completed or canceled."));
                        return ValueTask.FromResult(false);
                    }

                    return ValueTask.FromResult(true);
                }

                // Backpressure hit: hand off gate ownership to the timeout-guarded slow path.
                releaseInFinally = false;
                return CompletePendingFlushSlowAsync(flushTask, cancellationToken);
            }
            catch (ObjectDisposedException)
            {
                return ValueTask.FromResult(false);
            }
            catch (Exception ex)
            {
                TriggerFaultClose(ex);
                return ValueTask.FromResult(false);
            }
            finally
            {
                if (releaseInFinally)
                    ReleaseWriteGateSafe();
            }
        }

        return WriteSlowAsync(writeAction, state, cancellationToken);
    }

    private async ValueTask<bool> CompletePendingFlushSlowAsync(
        ValueTask<FlushResult> flushTask,
        CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        if (_options.SendFlushTimeout > TimeSpan.Zero && _options.SendFlushTimeout != Timeout.InfiniteTimeSpan)
            linkedCts.CancelAfter(_options.SendFlushTimeout);

        try
        {
            var flush = await flushTask.AsTask().WaitAsync(linkedCts.Token).ConfigureAwait(false);
            if (flush.IsCanceled || flush.IsCompleted)
            {
                TriggerFaultClose(new IOException("Outbound pipe completed or canceled."));
                return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_cts.IsCancellationRequested)
        {
            TriggerFaultClose(new TimeoutException($"Send flush timed out after {_options.SendFlushTimeout.TotalMilliseconds:F0}ms (slow peer backpressure)."));
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (Exception ex)
        {
            TriggerFaultClose(ex);
            return false;
        }
        finally
        {
            ReleaseWriteGateSafe();
        }
    }

    private async ValueTask<bool> WriteSlowAsync<TState>(
        Action<PipeWriter, TState> writeAction,
        TState state,
        CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        if (_options.SendFlushTimeout > TimeSpan.Zero && _options.SendFlushTimeout != Timeout.InfiniteTimeSpan)
            linkedCts.CancelAfter(_options.SendFlushTimeout);

        var acquired = false;
        try
        {
            await _writeGate.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            acquired = true;

            if (IsDisposed)
                return false;

            writeAction(_outboundPipe.Writer, state);
            var flush = await _outboundPipe.Writer.FlushAsync(linkedCts.Token).ConfigureAwait(false);
            if (flush.IsCanceled || flush.IsCompleted)
            {
                TriggerFaultClose(new IOException("Outbound pipe completed or canceled."));
                return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_cts.IsCancellationRequested)
        {
            TriggerFaultClose(new TimeoutException($"Send flush timed out after {_options.SendFlushTimeout.TotalMilliseconds:F0}ms (slow peer backpressure)."));
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (Exception ex)
        {
            TriggerFaultClose(ex);
            return false;
        }
        finally
        {
            if (acquired)
                ReleaseWriteGateSafe();
        }
    }

    private void ReleaseWriteGateSafe()
    {
        try
        {
            _writeGate.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task ReceiveLoopAsync()
    {
        CurrentReadLoopConnection.Value = this;
        Exception? fault = null;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await WaitIfPausedAsync(_cts.Token).ConfigureAwait(false);

                var readResult = await _inboundReader.ReadAsync(_cts.Token).ConfigureAwait(false);
                var buffer = readResult.Buffer;

                while (PacketFrameCodec.TryReadFrame(ref buffer, out var frame))
                {
                    await WaitIfPausedAsync(_cts.Token).ConfigureAwait(false);

                    var handler = Volatile.Read(ref _inboundHandler);
                    if (handler is not null)
                    {
                        await handler(frame, _cts.Token).ConfigureAwait(false);
                    }
                }

                _inboundReader.AdvanceTo(buffer.Start, buffer.End);

                if (readResult.IsCompleted)
                {
                    if (!buffer.IsEmpty)
                        fault = new EndOfStreamException("Connection closed with an incomplete Terraria frame.");
                    break;
                }

                if (readResult.IsCanceled)
                    break;
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            fault = ex;
        }
        finally
        {
            CurrentReadLoopConnection.Value = null;
            try
            {
                await _inboundReader.CompleteAsync(fault).ConfigureAwait(false);
            }
            catch
            {
            }

            NotifyClosed(fault);
        }
    }

    private async Task PumpOutboundAsync()
    {
        Exception? fault = null;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var read = await _outboundPipe.Reader.ReadAsync(_cts.Token).ConfigureAwait(false);
                var buffer = read.Buffer;

                if (!buffer.IsEmpty)
                {
                    if (buffer.IsSingleSegment)
                    {
                        var span = buffer.FirstSpan;
                        span.CopyTo(_transportWriter.GetSpan(span.Length));
                        _transportWriter.Advance(span.Length);
                    }
                    else
                    {
                        foreach (var segment in buffer)
                        {
                            if (segment.IsEmpty)
                                continue;
                            var dest = _transportWriter.GetSpan(segment.Length);
                            segment.Span.CopyTo(dest);
                            _transportWriter.Advance(segment.Length);
                        }
                    }

                    var flushTask = _transportWriter.FlushAsync(_cts.Token);
                    FlushResult flushResult;
                    if (flushTask.IsCompletedSuccessfully)
                    {
                        flushResult = flushTask.Result;
                    }
                    else if (_options.SendFlushTimeout > TimeSpan.Zero && _options.SendFlushTimeout != Timeout.InfiniteTimeSpan)
                    {
                        using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                        flushCts.CancelAfter(_options.SendFlushTimeout);
                        flushResult = await flushTask.AsTask().WaitAsync(flushCts.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        flushResult = await flushTask.ConfigureAwait(false);
                    }

                    if (flushResult.IsCanceled || flushResult.IsCompleted)
                    {
                        fault = new IOException("Transport output stream closed.");
                        break;
                    }
                }

                _outboundPipe.Reader.AdvanceTo(buffer.End);

                if (read.IsCompleted || read.IsCanceled)
                    break;
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            fault = ex;
        }
        finally
        {
            try
            {
                await _outboundPipe.Reader.CompleteAsync(fault).ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                await _transportWriter.CompleteAsync(fault).ConfigureAwait(false);
            }
            catch
            {
            }

            if (fault is not null)
                TriggerFaultClose(fault);
        }
    }

    private ValueTask WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _resumeSignal) is null)
            return ValueTask.CompletedTask;

        TaskCompletionSource? signal;
        lock (_pauseLock)
        {
            signal = _resumeSignal;
        }

        if (signal is null)
            return ValueTask.CompletedTask;

        return new ValueTask(signal.Task.WaitAsync(cancellationToken));
    }

    private void TriggerFaultClose(Exception ex)
    {
        NotifyClosed(ex);
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
        {
            ResumeReading();
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                _outboundPipe.Writer.Complete(ex);
            }
            catch
            {
            }

            CloseUnderlyingSocket();
        }
    }

    private void NotifyClosed(Exception? exception)
    {
        if (Interlocked.Exchange(ref _closeNotified, 1) != 0)
            return;

        var handler = Volatile.Read(ref _closedHandler);
        if (handler is null)
            return;

        // Dispatch off the read/write loop to guarantee zero re-entrant disposal deadlocks.
        _ = Task.Run(async () =>
        {
            try
            {
                await handler(exception).ConfigureAwait(false);
            }
            catch
            {
            }
        });
    }

    private void CloseUnderlyingSocket()
    {
        try
        {
            _stream?.Close();
        }
        catch
        {
        }

        try
        {
            _tcpClient?.Close();
        }
        catch
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        var firstDispose = Interlocked.Exchange(ref _disposed, 1) == 0;
        ResumeReading();

        if (firstDispose)
        {
            try
            {
                await _cts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                await _outboundPipe.Writer.CompleteAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            CloseUnderlyingSocket();
        }

        var isSelfReceiveLoop = ReferenceEquals(CurrentReadLoopConnection.Value, this);
        if (!isSelfReceiveLoop)
        {
            if (_receiveTask is { } recv)
            {
                try
                {
                    await recv.ConfigureAwait(false);
                }
                catch
                {
                }
            }

            if (_outboundPumpTask is { } pump)
            {
                try
                {
                    await pump.ConfigureAwait(false);
                }
                catch
                {
                }
            }

            if (firstDispose)
            {
                _writeGate.Dispose();
                _cts.Dispose();
            }
        }
    }
}
