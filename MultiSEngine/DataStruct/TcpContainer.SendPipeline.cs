using System.IO.Pipelines;

namespace MultiSEngine.DataStruct
{
    public sealed partial class TcpContainer
    {
        public sealed class SendPipelineContext : IAsyncDisposable
        {
            private readonly TcpContainer _owner;
            private readonly SemaphoreSlim _sendLock = new(1, 1);
            private PipeWriter? _pipeWriter;

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

            public async ValueTask<bool> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (_owner.IsDisposed)
                    return false;

                await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    EnsurePipelineConfigured();
                    var writer = _pipeWriter!;
                    var memory = writer.GetMemory(buffer.Length);
                    buffer.Span.CopyTo(memory.Span);
                    writer.Advance(buffer.Length);
                    var result = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                    return !result.IsCanceled;
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

                await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    EnsurePipelineConfigured();
                    var writer = _pipeWriter!;
                    var totalLen = 0;
                    for (int i = 0; i < buffers.Count; i++)
                    {
                        var buffer = buffers[i];
                        var memory = writer.GetMemory(buffer.Length);
                        buffer.Span.CopyTo(memory.Span);
                        writer.Advance(buffer.Length);
                        totalLen += buffer.Length;
                    }
                    var result = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                    return !result.IsCanceled;
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
                if (_pipeWriter is not null)
                {
                    try
                    {
                        await _pipeWriter.CompleteAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // ignore pipe dispose exceptions
                    }
                    _pipeWriter = null;
                }

                _sendLock.Dispose();
            }
        }
    }
}
