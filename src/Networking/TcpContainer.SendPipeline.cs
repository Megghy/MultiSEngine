using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Channels;

namespace MultiSEngine.Networking
{
    public sealed partial class TcpContainer
    {
        public sealed class SendPipelineContext : IAsyncDisposable
        {
            private sealed class QueuedSend : IDisposable
            {
                private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                private IMemoryOwner<byte>? _owner;

                public QueuedSend(IMemoryOwner<byte> owner, int length)
                {
                    _owner = owner;
                    Memory = owner.Memory[..length];
                }

                public ReadOnlyMemory<byte> Memory { get; }

                public Task<bool> Completion => _completion.Task;

                public void Complete(bool success)
                    => _completion.TrySetResult(success);

                public void Dispose()
                {
                    _owner?.Dispose();
                    _owner = null;
                }
            }

            private readonly TcpContainer _owner;
            private readonly SemaphoreSlim _sendLock = new(1, 1);
            // 连接一旦出现发送争用，就切到队列模式，避免调用方继续在 FlushAsync 前排队。
            private readonly Channel<QueuedSend> _queuedSends = Channel.CreateUnbounded<QueuedSend>(new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = false
            });
            private readonly CancellationTokenSource _queueCancellation = new();
            private PipeWriter? _pipeWriter;
            private Task? _queuePumpTask;
            private int _queueModeStarted;

            public SendPipelineContext(TcpContainer owner)
            {
                _owner = owner;
            }

            private void EnsurePipelineConfigured()
            {
                if (_pipeWriter is not null)
                    return;

                _pipeWriter = PipeWriter.Create(_owner._stream, new StreamPipeWriterOptions(leaveOpen: true));
                if (Config.Instance.DisableTcpDelayWhenPipeline)
                    _owner.Connection.NoDelay = true;
            }

            private static QueuedSend RentCopy(ReadOnlyMemory<byte> buffer)
            {
                var owner = MemoryPool<byte>.Shared.Rent(buffer.Length);
                buffer.Span.CopyTo(owner.Memory.Span);
                return new(owner, buffer.Length);
            }

            private static QueuedSend RentCopy(IReadOnlyList<ReadOnlyMemory<byte>> buffers)
            {
                var totalLength = 0;
                for (var i = 0; i < buffers.Count; i++)
                    totalLength += buffers[i].Length;

                var owner = MemoryPool<byte>.Shared.Rent(totalLength);
                var destination = owner.Memory.Span;
                var offset = 0;
                for (var i = 0; i < buffers.Count; i++)
                {
                    var buffer = buffers[i];
                    buffer.Span.CopyTo(destination[offset..]);
                    offset += buffer.Length;
                }

                return new(owner, totalLength);
            }

            private void EnsureQueueModeStarted()
            {
                if (Interlocked.CompareExchange(ref _queueModeStarted, 1, 0) == 0)
                    _queuePumpTask = PumpQueuedSendsAsync();
            }

            private async ValueTask<bool> WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
            {
                EnsurePipelineConfigured();
                var writer = _pipeWriter!;
                var memory = writer.GetMemory(buffer.Length);
                buffer.Span.CopyTo(memory.Span);
                writer.Advance(buffer.Length);
                var result = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                return !result.IsCanceled;
            }

            private async ValueTask<bool> WriteBatchAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancellationToken)
            {
                EnsurePipelineConfigured();
                var writer = _pipeWriter!;
                var totalLen = 0;
                for (int i = 0; i < buffers.Count; i++)
                    totalLen += buffers[i].Length;

                var destination = writer.GetSpan(totalLen)[..totalLen];
                var offset = 0;
                for (int i = 0; i < buffers.Count; i++)
                {
                    var buffer = buffers[i];
                    buffer.Span.CopyTo(destination[offset..]);
                    offset += buffer.Length;
                }

                writer.Advance(totalLen);
                var result = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                return !result.IsCanceled;
            }

            private async ValueTask<bool> EnqueueAsync(QueuedSend queuedSend, CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    queuedSend.Dispose();
                    return false;
                }

                EnsureQueueModeStarted();
                if (!_queuedSends.Writer.TryWrite(queuedSend))
                {
                    queuedSend.Dispose();
                    return false;
                }

                try
                {
                    return await queuedSend.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }

            private async Task PumpQueuedSendsAsync()
            {
                var batch = new List<QueuedSend>(8);
                try
                {
                    while (await _queuedSends.Reader.WaitToReadAsync(_queueCancellation.Token).ConfigureAwait(false))
                    {
                        batch.Clear();
                        while (_queuedSends.Reader.TryRead(out var queuedSend))
                            batch.Add(queuedSend);

                        if (batch.Count == 0)
                            continue;

                        await _sendLock.WaitAsync(_queueCancellation.Token).ConfigureAwait(false);
                        try
                        {
                            EnsurePipelineConfigured();
                            var writer = _pipeWriter!;

                            // 队列模式会把当前已到达的发送请求一并刷出，直接减少 FlushAsync / WSASend 次数。
                            for (var i = 0; i < batch.Count; i++)
                            {
                                var buffer = batch[i].Memory;
                                var memory = writer.GetMemory(buffer.Length);
                                buffer.Span.CopyTo(memory.Span);
                                writer.Advance(buffer.Length);
                            }

                            var result = await writer.FlushAsync(_queueCancellation.Token).ConfigureAwait(false);
                            var success = !result.IsCanceled;
                            for (var i = 0; i < batch.Count; i++)
                                batch[i].Complete(success);
                        }
                        catch (OperationCanceledException)
                        {
                            for (var i = 0; i < batch.Count; i++)
                                batch[i].Complete(false);
                        }
                        catch (Exception ex)
                        {
                            Logs.Error($"Failed to flush queued send data ({batch.Count} items).{Environment.NewLine}{ex}");
                            for (var i = 0; i < batch.Count; i++)
                                batch[i].Complete(false);
                        }
                        finally
                        {
                            for (var i = 0; i < batch.Count; i++)
                                batch[i].Dispose();

                            _sendLock.Release();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    while (_queuedSends.Reader.TryRead(out var queuedSend))
                    {
                        queuedSend.Complete(false);
                        queuedSend.Dispose();
                    }
                }
            }

            public async ValueTask<bool> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (_owner.IsDisposed)
                    return false;
                if (buffer.IsEmpty)
                    return true;

                if (Volatile.Read(ref _queueModeStarted) != 0)
                    return await EnqueueAsync(RentCopy(buffer), cancellationToken).ConfigureAwait(false);

                if (!_sendLock.Wait(0))
                {
                    EnsureQueueModeStarted();
                    return await EnqueueAsync(RentCopy(buffer), cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    return await WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    Logs.Error($"Failed to send data via pipeline.{Environment.NewLine}{ex}");
                    return false;
                }
                finally
                {
                    _sendLock.Release();
                }
            }

            public ValueTask<bool> SendViaPipelineAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
                => SendAsync(buffer, cancellationToken);

            public async ValueTask<bool> SendBatchAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancellationToken = default)
            {
                if (_owner.IsDisposed)
                    return false;
                if (buffers.Count == 0)
                    return true;

                if (Volatile.Read(ref _queueModeStarted) != 0)
                    return await EnqueueAsync(RentCopy(buffers), cancellationToken).ConfigureAwait(false);

                if (!_sendLock.Wait(0))
                {
                    EnsureQueueModeStarted();
                    return await EnqueueAsync(RentCopy(buffers), cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    return await WriteBatchAsync(buffers, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    Logs.Error($"Failed to send batch data ({buffers.Count} packets).{Environment.NewLine}{ex}");
                    return false;
                }
                finally
                {
                    _sendLock.Release();
                }
            }

            public ValueTask<bool> SendViaPipelineBatchAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancellationToken = default)
                => SendBatchAsync(buffers, cancellationToken);

            public async ValueTask DisposeAsync()
            {
                _queuedSends.Writer.TryComplete();
                _queueCancellation.Cancel();
                if (_queuePumpTask is not null)
                {
                    try
                    {
                        await _queuePumpTask.ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                    _queuePumpTask = null;
                }

                if (_pipeWriter is not null)
                {
                    try
                    {
                        await _pipeWriter.CompleteAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                    _pipeWriter = null;
                }

                _queueCancellation.Dispose();
                _sendLock.Dispose();
            }
        }
    }
}


