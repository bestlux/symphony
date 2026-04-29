using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Symphony.Service.Cli;

namespace Symphony.Service.Hosting;

public sealed class ServiceRestartService
{
    private readonly CliOptions _options;
    private readonly IHostApplicationLifetime _lifetime;

    public ServiceRestartService(CliOptions options, IHostApplicationLifetime lifetime)
    {
        _options = options;
        _lifetime = lifetime;
    }

    public ServiceRestartResult RequestRestart()
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot restart Symphony because the current executable path is unknown.");
        var workingDirectory = Environment.CurrentDirectory;
        var arguments = BuildLaunchArguments(executablePath);

        StartRestartHelper(Process.GetCurrentProcess().Id, executablePath, workingDirectory, arguments);

        _ = Task.Run(async () =>
        {
            await Task.Delay(350).ConfigureAwait(false);
            _lifetime.StopApplication();
        });

        return new ServiceRestartResult(Process.GetCurrentProcess().Id, executablePath, workingDirectory, arguments);
    }

    private IReadOnlyList<string> BuildLaunchArguments(string executablePath)
    {
        var arguments = new List<string>();
        var entryPath = Environment.GetCommandLineArgs().FirstOrDefault();

        if (IsDotnetHost(executablePath)
            && !string.IsNullOrWhiteSpace(entryPath)
            && string.Equals(Path.GetExtension(entryPath), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            arguments.Add(Path.GetFullPath(entryPath));
        }

        arguments.AddRange(BuildCliArguments());
        return arguments;
    }

    private IReadOnlyList<string> BuildCliArguments()
    {
        var arguments = new List<string>();

        if (_options.Port is { } port)
        {
            arguments.Add("--port");
            arguments.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(_options.LogsRoot))
        {
            arguments.Add("--logs-root");
            arguments.Add(_options.LogsRoot);
        }

        if (!string.IsNullOrWhiteSpace(_options.SecretsPath))
        {
            arguments.Add("--secrets");
            arguments.Add(_options.SecretsPath);
        }

        arguments.Add(_options.WorkflowPath);

        if (_options.AcknowledgedGuardrails)
        {
            arguments.Add(CliParser.GuardrailsFlag);
        }

        return arguments;
    }

    private static bool IsDotnetHost(string executablePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(executablePath);
        return string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase);
    }

    private static void StartRestartHelper(
        int currentProcessId,
        string executablePath,
        string workingDirectory,
        IReadOnlyList<string> arguments)
    {
        var executable = Convert.ToBase64String(Encoding.UTF8.GetBytes(executablePath));
        var cwd = Convert.ToBase64String(Encoding.UTF8.GetBytes(workingDirectory));
        var args = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(arguments)));
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $exe = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{executable}}'))
            $cwd = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{cwd}}'))
            $launchArgs = [string[]](ConvertFrom-Json ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{args}}'))))
            Wait-Process -Id {{currentProcessId}} -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 300
            Start-Process -FilePath $exe -ArgumentList $launchArgs -WorkingDirectory $cwd -WindowStyle Hidden
            """;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(encoded);

        Process.Start(startInfo);
    }
}

public sealed record ServiceRestartResult(
    int CurrentProcessId,
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments);
