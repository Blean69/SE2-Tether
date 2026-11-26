// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using Tether;
using Tether.Diagnostics;
using Tether.Protocol;
using Tether.Transport.Udp;

namespace Tether.NetTest;

internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitUsage = 1;
    private const int ExitFailed = 2;
    private const int ExitExpectationNotMet = 3;

    private const string Usage = """
        tether-nettest: test Tether networking between machines (no game required).

        Usage:
          tether-nettest host [options]              host a test session (UDP)
          tether-nettest join HOST[:PORT] [options]  join a test session or the in-game self-test
          tether-nettest version

        Options:
          --port N            host: UDP port to listen on (default 27027)
          --name NAME         your name in the session
          --password TEXT     session password (host sets it, joiners must match)
          --max-players N     host: players including the host (default 4)
          --report SECONDS    link report interval (default 10, 0 = off)
          --duration SECONDS  exit after this long (default: run until /quit or Ctrl+C)
          --say TEXT          send this chat line once joined (host: once someone joins)
          --expect-chat TEXT  succeed only if another player says something containing TEXT
          --expect-players N  succeed only if the session reaches N players
          --exit-on-success   exit as soon as every --expect-* is met

        While running, type a line to chat, or: /players  /stats  /help  /quit

        Exit codes: 0 ok, 1 bad usage, 2 could not host/join or was disconnected, 3 an expectation was not met.
        Hosting from home over the internet needs the UDP port forwarded on your router; LAN needs nothing.
        """;

    public static int Main(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(Usage);
            return ExitUsage;
        }

        switch (options.Mode)
        {
            case "help":
                Console.WriteLine(Usage);
                return ExitOk;
            case "version":
                Console.WriteLine($"tether-nettest {TetherVersion.Plugin} (protocol {ProtocolConstants.ProtocolVersion})");
                return ExitOk;
        }

        try
        {
            return Run(options);
        }
        catch (Exception ex) when (ex is InvalidOperationException or SocketException)
        {
            Print($"Error: {ex.Message}");
            return ExitFailed;
        }
    }

    private static int Run(Options options)
    {
        var log = new ConsoleLog();
        var testOptions = new NetTestOptions
        {
            Name = options.Name ?? (options.Mode == "host" ? "nettest host" : "nettest client"),
            Password = options.Password,
            MaxPlayers = options.MaxPlayers,
            ReportIntervalMs = options.ReportSeconds * 1000,
            Greeting = $"running tether-nettest on {OsName()}",
        };

        Print($"tether-nettest {TetherVersion.Plugin} (protocol {ProtocolConstants.ProtocolVersion}). Type /help for commands.");
        NetTestNode node;
        if (options.Mode == "host")
        {
            var transport = UdpTransport.CreateHost(options.Port, log: log);
            node = NetTestNode.StartHost(transport, testOptions, log: log);
            Print($"Listening on UDP port {transport.LocalPort}. Others can join with:");
            foreach (var address in LocalAddresses())
            {
                Print($"    tether-nettest join {new UdpEndpoint(address, transport.LocalPort)}");
            }
        }
        else
        {
            node = NetTestNode.StartClient(UdpTransport.CreateClient(options.Target!.Value.Host, options.Target.Value.Port, log: log), testOptions, log: log);
            Print($"Joining {options.Target}...");
        }

        var chatFromOthers = new List<string>();
        var maxPlayersSeen = 1;
        node.Output += Print;
        node.ChatReceived += (from, text) =>
        {
            if (!IsSelf(node, from))
            {
                chatFromOthers.Add(text);
            }
        };

        var input = StartInputReader();
        var quit = false;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            quit = true;
        };

        var start = Environment.TickCount64;
        var deadline = options.DurationSeconds > 0 ? start + (options.DurationSeconds * 1000L) : long.MaxValue;
        var said = options.Say is null;
        var everJoined = false;
        var hasExpectations = options.ExpectChat is not null || options.ExpectPlayers > 0;

        bool ExpectationsMet() =>
            (options.ExpectChat is null || chatFromOthers.Any(t => t.Contains(options.ExpectChat, StringComparison.OrdinalIgnoreCase)))
            && (options.ExpectPlayers <= 0 || maxPlayersSeen >= options.ExpectPlayers);

        try
        {
            while (!quit && !node.IsFinished && Environment.TickCount64 < deadline)
            {
                node.Poll();
                if (node.IsJoined && (node.IsHost ? node.Players.Count > 1 : true))
                {
                    everJoined = true;
                }

                maxPlayersSeen = Math.Max(maxPlayersSeen, node.IsJoined ? node.Players.Count : 0);

                if (!said && everJoined)
                {
                    node.Say(options.Say!);
                    said = true;
                }

                while (input.Lines.TryDequeue(out var line))
                {
                    quit |= HandleCommand(node, line);
                }

                if (input.Closed && options.DurationSeconds <= 0 && !hasExpectations)
                {
                    // Non-interactive run with nothing to wait for (e.g. input piped from a file).
                    quit = true;
                }

                if (hasExpectations && options.ExitOnSuccess && ExpectationsMet())
                {
                    Print("All expectations met.");
                    break;
                }

                Thread.Sleep(15);
            }

            if (Environment.TickCount64 >= deadline)
            {
                Print($"Duration of {options.DurationSeconds} s reached.");
            }

            if (node.IsJoined && node.Players.Count > 1)
            {
                Print(node.DescribeLink());
            }
        }
        finally
        {
            node.Dispose();
        }

        if (!node.IsHost && !everJoined)
        {
            Print("Never joined the session.");
            return ExitFailed;
        }

        if (!node.IsHost && node.EndReason is { } reason && reason is not (DisconnectReason.ClientLeft or DisconnectReason.HostShutdown))
        {
            // Joined, but then dropped for a reason other than a clean end of the session.
            return ExitFailed;
        }

        if (hasExpectations && !ExpectationsMet())
        {
            Print($"Expectation not met (chat: {(options.ExpectChat ?? "-")}, players: {(options.ExpectPlayers > 0 ? options.ExpectPlayers : "-")}, seen {maxPlayersSeen}).");
            return ExitExpectationNotMet;
        }

        return ExitOk;
    }

    private static bool HandleCommand(NetTestNode node, string line)
    {
        var trimmed = line.Trim();
        switch (trimmed.ToLowerInvariant())
        {
            case "":
                return false;
            case "/quit" or "/exit":
                return true;
            case "/players":
                Print(node.DescribePlayers());
                return false;
            case "/stats":
                Print(node.DescribeLink());
                return false;
            case "/help":
                Print("Type a line to chat, or: /players  /stats  /quit");
                return false;
        }

        if (trimmed.StartsWith('/'))
        {
            Print($"Unknown command {trimmed}. Try /help.");
            return false;
        }

        node.Say(trimmed);
        return false;
    }

    private static bool IsSelf(NetTestNode node, PlayerInfo from) => from.Id == node.LocalPlayerId;

    private static InputReader StartInputReader()
    {
        var reader = new InputReader();
        var thread = new Thread(() =>
        {
            try
            {
                string? line;
                while ((line = Console.ReadLine()) is not null)
                {
                    reader.Lines.Enqueue(line);
                }
            }
            catch (Exception)
            {
                // Console unavailable; treat as closed.
            }

            reader.Closed = true;
        })
        {
            IsBackground = true,
            Name = "stdin",
        };
        thread.Start();
        return reader;
    }

    private static IEnumerable<string> LocalAddresses()
    {
        var addresses = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        addresses.Add(unicast.Address.ToString());
                    }
                }
            }
        }
        catch (NetworkInformationException)
        {
        }

        if (addresses.Count == 0)
        {
            addresses.Add("<this machine's IP address>");
        }

        addresses.Add("127.0.0.1");
        return addresses.Distinct();
    }

    private static string OsName() =>
        OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : OperatingSystem.IsMacOS() ? "macOS" : "another OS";

    private static void Print(string line) =>
        Console.WriteLine($"{DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} {line}");

    private sealed class InputReader
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public volatile bool Closed;
    }

    /// <summary>Shows Tether's own warnings and errors; info-level chatter is already covered by node output.</summary>
    private sealed class ConsoleLog : ITetherLog
    {
        public void Write(LogLevel level, string message, Exception? exception = null)
        {
            if (level >= LogLevel.Warning)
            {
                Print($"[{level}] {message}{(exception is null ? string.Empty : ": " + exception.Message)}");
            }
        }
    }

    private sealed record Options
    {
        public string Mode { get; private init; } = "help";

        public UdpEndpoint? Target { get; private init; }

        public int Port { get; private init; } = UdpTransportOptions.DefaultPort;

        public string? Name { get; private init; }

        public string? Password { get; private init; }

        public int MaxPlayers { get; private init; } = 4;

        public int ReportSeconds { get; private init; } = 10;

        public int DurationSeconds { get; private init; }

        public string? Say { get; private init; }

        public string? ExpectChat { get; private init; }

        public int ExpectPlayers { get; private init; }

        public bool ExitOnSuccess { get; private init; }

        public static Options Parse(string[] args)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
            {
                return new Options { Mode = "help" };
            }

            var mode = args[0].ToLowerInvariant();
            if (mode is "version" or "--version")
            {
                return new Options { Mode = "version" };
            }

            if (mode is not ("host" or "join"))
            {
                throw new ArgumentException($"Unknown command '{args[0]}'.");
            }

            var options = new Options { Mode = mode };
            var i = 1;
            if (mode == "join")
            {
                if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException("join needs HOST[:PORT].");
                }

                if (!UdpEndpoint.TryParse(args[1], UdpTransportOptions.DefaultPort, out var target))
                {
                    throw new ArgumentException($"'{args[1]}' is not a valid HOST[:PORT].");
                }

                options = options with { Target = target };
                i = 2;
            }

            for (; i < args.Length; i++)
            {
                var name = args[i];
                string Value()
                {
                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException($"{name} needs a value.");
                    }

                    return args[++i];
                }

                int Number(int min, int max)
                {
                    var text = Value();
                    if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < min || n > max)
                    {
                        throw new ArgumentException($"{name} must be a number from {min} to {max}.");
                    }

                    return n;
                }

                options = name switch
                {
                    "--port" => options with { Port = Number(0, 65535) },
                    "--name" => options with { Name = Value() },
                    "--password" => options with { Password = Value() },
                    "--max-players" => options with { MaxPlayers = Number(2, ProtocolConstants.MaxPlayers) },
                    "--report" => options with { ReportSeconds = Number(0, 3600) },
                    "--duration" => options with { DurationSeconds = Number(1, 7 * 24 * 3600) },
                    "--say" => options with { Say = Value() },
                    "--expect-chat" => options with { ExpectChat = Value() },
                    "--expect-players" => options with { ExpectPlayers = Number(2, ProtocolConstants.MaxPlayers) },
                    "--exit-on-success" => options with { ExitOnSuccess = true },
                    _ => throw new ArgumentException($"Unknown option '{name}'."),
                };
            }

            return options;
        }
    }
}
