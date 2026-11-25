using System.Collections.Immutable;
using System.Text;
using MultiSEngine.Core.Configuration;
using MultiSEngine.Core.Extensions;
using MultiSEngine.Core.Logging;
using MultiSEngine.Core.Security;
using MultiSEngine.Core.Sessions;
using MultiSEngine.Runtime.Extensions;

namespace MultiSEngine.Runtime.Commands;

public static class CommandTokenizer
{
    public static bool TryParse(string? rawInput, bool requireLeadingSlash, out string commandName, out IReadOnlyList<string> arguments)
    {
        commandName = string.Empty;
        arguments = [];

        if (string.IsNullOrWhiteSpace(rawInput))
            return false;

        var trimmed = rawInput.Trim();
        if (requireLeadingSlash)
        {
            if (!trimmed.StartsWith('/'))
                return false;
            trimmed = trimmed[1..].TrimStart();
        }
        else if (trimmed.StartsWith('/'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        if (trimmed.Length == 0)
            return false;

        var tokens = Tokenize(trimmed);
        if (tokens.Count == 0 || string.IsNullOrWhiteSpace(tokens[0]))
            return false;

        commandName = tokens[0].ToLowerInvariant();
        if (tokens.Count > 1)
            arguments = tokens.GetRange(1, tokens.Count - 1);

        return true;
    }

    public static List<string> Tokenize(string text)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasTokenContent = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length)
            {
                var next = text[i + 1];
                if (next is '"' or '\\' or ' ')
                {
                    current.Append(next);
                    hasTokenContent = true;
                    i++;
                    continue;
                }
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasTokenContent = true;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (hasTokenContent)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    hasTokenContent = false;
                }
                continue;
            }

            current.Append(c);
            hasTokenContent = true;
        }

        if (hasTokenContent)
            result.Add(current.ToString());

        return result;
    }
}

public sealed class CommandRouter(IProxyLogger logger, ILocalizationStore localization)
{
    private readonly Lock _lock = new();
    private ICommand[] _commands = [];
    private ImmutableDictionary<string, ImmutableArray<ICommand>> _byName =
        ImmutableDictionary<string, ImmutableArray<ICommand>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);

    public IDisposable Register(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Name);

        lock (_lock)
        {
            _commands = [.. _commands, command];
            RebuildIndexLocked();
        }

        return new HookBus.UnsubscribeAction(() =>
        {
            lock (_lock)
            {
                _commands = [.. _commands.Where(c => !ReferenceEquals(c, command))];
                RebuildIndexLocked();
            }
        });
    }

    public ImmutableArray<ICommand> GetRegisteredCommands() => [.. Volatile.Read(ref _commands)];

    public async ValueTask<CommandExecutionResult> DispatchAsync(
        CommandSourceKind sourceKind,
        string? rawText,
        ISessionHandle? playerSession = null,
        ServerDescriptor? backendServer = null,
        bool hasClusterPermission = false,
        CancellationToken cancellationToken = default)
    {
        var requireSlash = sourceKind == CommandSourceKind.Player;
        if (!CommandTokenizer.TryParse(rawText, requireSlash, out var commandName, out var arguments))
            return CommandExecutionResult.PassThrough();

        var index = Volatile.Read(ref _byName);
        if (!index.TryGetValue(commandName, out var candidates))
            return CommandExecutionResult.PassThrough();

        ICommand? matched = null;
        foreach (var candidate in candidates)
        {
            if ((candidate.AllowedSources & sourceKind) != 0)
            {
                matched = candidate;
                break;
            }
        }

        if (matched is null)
            return CommandExecutionResult.PassThrough();

        var context = new CommandContext(
            sourceKind,
            commandName,
            arguments,
            rawText ?? string.Empty,
            playerSession,
            backendServer,
            hasClusterPermission);

        try
        {
            return await matched.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Error($"Command '{matched.Name}' threw an unhandled exception.", ex, "Commands");
            var prompt = localization.Get("Prompt_CommandFailed");
            if (playerSession is not null)
            {
                await playerSession.SendChatAsync(prompt, Protocol.Terraria.TerrariaPacketFactory.Rgb(255, 80, 80), withPrefix: true, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                logger.Error(prompt, category: "Commands");
            }

            // CRITICAL SECURITY INVARIANT: never leak failed proxy commands (e.g. /mse password) to backend servers.
            return CommandExecutionResult.Intercepted();
        }
    }

    private void RebuildIndexLocked()
    {
        var map = new Dictionary<string, ImmutableArray<ICommand>.Builder>(StringComparer.OrdinalIgnoreCase);

        void AddKey(string key, ICommand cmd)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;
            if (!map.TryGetValue(key, out var list))
                map[key] = list = ImmutableArray.CreateBuilder<ICommand>();
            if (!list.Contains(cmd))
                list.Add(cmd);
        }

        foreach (var cmd in _commands)
        {
            AddKey(cmd.Name.Trim(), cmd);
            foreach (var alias in cmd.Aliases)
                AddKey(alias.Trim(), cmd);
        }

        Volatile.Write(ref _byName, map.ToImmutableDictionary(kv => kv.Key, kv => kv.Value.ToImmutable(), StringComparer.OrdinalIgnoreCase));
    }
}
