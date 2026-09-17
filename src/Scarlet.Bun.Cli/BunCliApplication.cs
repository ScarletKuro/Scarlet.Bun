using System.ComponentModel;
using Scarlet.Bun.Core;

namespace Scarlet.Bun.Cli;

/// <summary>
/// Orchestrates a single invocation: resolve Bun, then hand it every argument untouched.
/// </summary>
internal sealed class BunCliApplication
{
    /// <summary>
    /// The one argument the tool reserves for itself.
    /// </summary>
    /// <remarks>
    /// Honoured only as the first argument, and only when <c>SCARLET_BUN_PASSTHROUGH</c> is unset. Bun would
    /// never ship a flag carrying a third party's brand, and restricting it to position 0 means
    /// <c>bun run build --scarlet-info</c> still reaches Bun. The environment variable is a permanent
    /// opt-out should that reasoning ever fail.
    /// </remarks>
    public const string InfoFlag = "--scarlet-info";

    private const string JsonFlag = "--json";

    private readonly BunCliResolver _resolver;
    private readonly IProcessLauncher _launcher;
    private readonly BunCliOptions _options;
    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;

    /// <summary>
    /// Initializes a new instance of the <see cref="BunCliApplication"/> class.
    /// </summary>
    /// <param name="resolver">Finds the Bun executable.</param>
    /// <param name="launcher">Runs it.</param>
    /// <param name="options">Configuration read from the environment.</param>
    /// <param name="stdout">Standard output, used only by diagnostics.</param>
    /// <param name="stderr">Standard error, used for tool messages so Bun's stdout stays clean.</param>
    public BunCliApplication(
        BunCliResolver resolver,
        IProcessLauncher launcher,
        BunCliOptions options,
        TextWriter stdout,
        TextWriter stderr)
    {
        _resolver = resolver;
        _launcher = launcher;
        _options = options;
        _stdout = stdout;
        _stderr = stderr;
    }

    /// <summary>
    /// Runs one invocation.
    /// </summary>
    /// <param name="args">The command line, forwarded verbatim unless it opens with <see cref="InfoFlag"/>.</param>
    /// <returns>Bun's exit code, or one of <see cref="ExitCodes"/> when Bun never ran.</returns>
    public int Run(string[] args)
    {
        if (IsInfoRequest(args))
        {
            return WriteDiagnostics(args);
        }

        var log = new ConsoleBunLogger(_stderr);

        BunResolution resolution;
        try
        {
            resolution = _resolver.Resolve(_options, allowDownload: true, log);
        }
        catch (Exception exception)
        {
            _stderr.WriteLine($"Scarlet.Bun: could not obtain Bun {_options.RequestedVersion}: {exception.Message}");

            return ExitCodes.BunNotFound;
        }

        if (!resolution.IsResolved)
        {
            _stderr.WriteLine($"Scarlet.Bun: {resolution.FailureReason}");

            return ExitCodes.BunNotFound;
        }

        if (_options.Diagnostics)
        {
            // Contract relied upon by tests/e2e/cli-tool: it is how a test proves which Bun actually ran,
            // and therefore that nothing was downloaded. Do not reword without updating that script.
            _stderr.WriteLine($"Scarlet.Bun: using Bun at {resolution.ExecutablePath}");
        }

        try
        {
            return _launcher.Run(new BunLaunchRequest(resolution.ExecutablePath!, args));
        }
        catch (Win32Exception exception)
        {
            _stderr.WriteLine($"Scarlet.Bun: failed to start '{resolution.ExecutablePath}': {exception.Message}");

            return ExitCodes.BunNotExecutable;
        }
    }

    private bool IsInfoRequest(string[] args)
    {
        return !_options.PurePassthrough
               && args.Length > 0
               && string.Equals(args[0], InfoFlag, StringComparison.Ordinal);
    }

    private int WriteDiagnostics(string[] args)
    {
        var asJson = false;

        foreach (var argument in args.Skip(1))
        {
            if (string.Equals(argument, JsonFlag, StringComparison.Ordinal))
            {
                asJson = true;

                continue;
            }

            _stderr.WriteLine($"Scarlet.Bun: unrecognised option '{argument}' after {InfoFlag}.");

            return ExitCodes.UsageError;
        }

        // allowDownload: false - asking the tool what it would do must never itself fetch 90 MB.
        var resolution = _resolver.Resolve(_options, allowDownload: false, new ConsoleBunLogger(_stderr));

        _stdout.Write(asJson
            ? DiagnosticsReport.ToJson(resolution, _options)
            : DiagnosticsReport.ToText(resolution, _options));

        return ExitCodes.Success;
    }
}
