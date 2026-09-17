using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Scarlet.Bun.Cli;

/// <summary>
/// Starts Bun as a child process and waits for it.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is redirected. Bun inherits this process's stdin, stdout and stderr <em>handles</em>, so
/// <c>isatty</c> is true, colours and progress rendering work, <c>bun repl</c> and <c>bun init</c> can
/// prompt, and piping behaves exactly as it would when invoking Bun directly. Redirecting in order to
/// re-emit the output would break all of that.
/// </para>
/// <para>
/// The working directory is deliberately left unset so the child inherits ours verbatim. Bun walks up from
/// the current directory to find <c>package.json</c>, <c>bunfig.toml</c> and <c>node_modules</c>, so
/// round-tripping the path through .NET's normalisation could genuinely change behaviour under symlinks.
/// </para>
/// </remarks>
[ExcludeFromCodeCoverage]
internal sealed class ProcessLauncher : IProcessLauncher
{
    /// <inheritdoc />
    public int Run(BunLaunchRequest request)
    {
        var startInfo = new ProcessStartInfo(request.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        // ArgumentList, never a concatenated string: the runtime applies the platform's quoting rules, which
        // is the only way arguments containing spaces, quotes or trailing backslashes survive intact.
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        process.Start();

        using var signals = new SignalBridge(process);

        process.WaitForExit();

        return process.ExitCode;
    }

    /// <summary>
    /// Keeps the wrapper alive until Bun exits, and makes sure Bun hears about termination.
    /// </summary>
    /// <remarks>
    /// Two failure modes this exists to prevent: the wrapper dying first, which returns the shell prompt
    /// while Bun is still drawing to the terminal; and Bun being orphaned, which .NET 10 made easier by
    /// removing the runtime's default SIGTERM and SIGHUP handling.
    /// </remarks>
    private sealed class SignalBridge : IDisposable
    {
        private readonly Process _process;
        private readonly bool _childSharesProcessGroup;
        private readonly List<PosixSignalRegistration> _registrations = new();
        private readonly EventHandler _processExitHandler;

        public SignalBridge(Process process)
        {
            _process = process;
            _childSharesProcessGroup = PosixInterop.SharesProcessGroup(process.Id);

            Register(PosixSignal.SIGINT, OnInterrupt);
            Register(PosixSignal.SIGQUIT, OnInterrupt);
            Register(PosixSignal.SIGTERM, OnTerminate);
            Register(PosixSignal.SIGHUP, OnTerminate);

            // Last resort for paths that bypass the handlers entirely.
            _processExitHandler = (_, _) => TryKill();
            AppDomain.CurrentDomain.ProcessExit += _processExitHandler;
        }

        public void Dispose()
        {
            AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;

            foreach (var registration in _registrations)
            {
                registration.Dispose();
            }

            _registrations.Clear();
        }

        private void Register(PosixSignal signal, Action<PosixSignalContext> handler)
        {
            try
            {
                _registrations.Add(PosixSignalRegistration.Create(signal, handler));
            }
            catch (Exception)
            {
                // Not every signal is supported on every host. Losing one handler must not stop the tool
                // from running Bun at all.
            }
        }

        // Ctrl+C and Ctrl+Break are delivered by the OS to the whole console / foreground process group, so
        // Bun already got it. Killing it here would rob it of a graceful shutdown; exiting here would hand
        // the prompt back while it is still running. So: cancel our own termination and keep waiting.
        private void OnInterrupt(PosixSignalContext context)
        {
            context.Cancel = true;

            if (!_childSharesProcessGroup)
            {
                PosixInterop.Send(_process.Id, PosixInterop.SIGINT);
            }
        }

        // SIGTERM and SIGHUP are sent to this process alone, so they have to be forwarded explicitly.
        private void OnTerminate(PosixSignalContext context)
        {
            if (OperatingSystem.IsWindows())
            {
                // Cancellation is not honoured for SIGTERM on Windows; this is the last chance to take Bun
                // down with us rather than orphan it.
                TryKill();

                return;
            }

            context.Cancel = true;

            PosixInterop.Send(
                _process.Id,
                context.Signal == PosixSignal.SIGHUP ? PosixInterop.SIGHUP : PosixInterop.SIGTERM);
        }

        private void TryKill()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
                // Already gone, or we lost the right to signal it. Either way there is nothing to do.
            }
        }
    }
}
