using MultiSEngine.Commands;
using MultiSEngine.Models;
using MultiSEngine.Runtime;

namespace MultiSEngine.Tests;

public sealed class CommandDispatcherTests
{
    [Fact]
    public async Task HandleCommand_ParsesQuotedAndEscapedParameters()
    {
        var command = new CaptureCommand("echo", continueSend: false);
        RuntimeState.Commands.Clear();

        try
        {
            RuntimeState.Commands.Add(command);

            var (handled, continueSend) = await CommandDispatcher.HandleCommand(
                null!,
                "/echo alpha \"two words\" escaped\\ value quote\\\"test slash\\\\tail");

            Assert.True(handled);
            Assert.False(continueSend);
            Assert.Equal("echo", command.ReceivedCommandName);
            Assert.Equal(
                ["alpha", "two words", "escaped value", "quote\"test", "slash\\tail"],
                command.ReceivedArguments);
        }
        finally
        {
            RuntimeState.Commands.Clear();
        }
    }

    [Fact]
    public async Task HandleCommand_UsesServerCommandsForConsoleInput()
    {
        var command = new CaptureCommand("status", continueSend: true, serverCommand: true);
        RuntimeState.Commands.Clear();

        try
        {
            RuntimeState.Commands.Add(command);

            var (handled, continueSend) = await CommandDispatcher.HandleCommand(null!, "status remote", fromConsole: true);

            Assert.True(handled);
            Assert.True(continueSend);
            Assert.Equal("status", command.ReceivedCommandName);
            Assert.Equal(["remote"], command.ReceivedArguments);
        }
        finally
        {
            RuntimeState.Commands.Clear();
        }
    }

    private sealed class CaptureCommand(string name, bool continueSend, bool serverCommand = false) : CommandDispatcher.CmdBase
    {
        public override string Name => name;

        public override bool ServerCommand => serverCommand;

        public string? ReceivedCommandName { get; private set; }

        public IReadOnlyList<string> ReceivedArguments { get; private set; } = [];

        public override ValueTask<bool> Execute(ClientData client, string cmdName, string[] parma)
        {
            ReceivedCommandName = cmdName;
            ReceivedArguments = parma;
            return ValueTask.FromResult(continueSend);
        }
    }
}
