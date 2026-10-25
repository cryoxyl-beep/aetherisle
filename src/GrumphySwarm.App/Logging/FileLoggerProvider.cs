using System.IO;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace GrumphySwarm.App.Logging;

/// <summary>
/// Minimal file logger: one daily file in %LOCALAPPDATA%\GrumphySwarm\logs,
/// written from a background consumer so logging never blocks the UI thread.
/// Files older than seven days are deleted on startup. Zero external packages.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly LogLevel _minimumLevel;
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly StreamWriter? _writer;
    private readonly Task _writeTask;
    private bool _disposed;

    public FileLoggerProvider(string? directory = null, LogLevel minimumLevel = LogLevel.Information)
    {
        _minimumLevel = minimumLevel;
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GrumphySwarm",
            "logs");

        Directory.CreateDirectory(_directory);
        CleanupOldLogs(retentionDays: 7);

        _writer = TryOpenWriter($"grumphy-{DateTime.Now:yyyyMMdd}.log")
            ?? TryOpenWriter($"grumphy-{DateTime.Now:yyyyMMdd}-{Environment.ProcessId}.log");

        // Logging must never crash startup: if every candidate file is locked
        // (a zombie instance holding an exclusive handle), this provider
        // degrades to a no-op and the app still runs.
        _writeTask = Task.Run(WriteLoopAsync);
    }

    private StreamWriter? TryOpenWriter(string fileName)
    {
        try
        {
            // Shared read/write so diagnostics can follow the log while we run.
            var stream = new FileStream(
                Path.Combine(_directory, fileName),
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite);
            return new StreamWriter(stream) { AutoFlush = true };
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this, _minimumLevel);

    internal bool TryEnqueue(string line)
    {
        if (_disposed) return false;
        _queue.Writer.TryWrite(line);
        return true;
    }

    private async Task WriteLoopAsync()
    {
        await foreach (var line in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (_writer is null)
            {
                continue; // logging disabled — drain the queue
            }

            try
            {
                await _writer.WriteLineAsync(line).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // A failed log write must never take the process down.
            }
        }
    }

    private void CleanupOldLogs(int retentionDays)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-retentionDays);
            foreach (var file in Directory.EnumerateFiles(_directory, "grumphy-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (IOException)
        {
            // Best effort only.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _queue.Writer.TryComplete();
        try
        {
            _writeTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Shutdown-time flush failures are not actionable.
        }

        _writer?.Dispose();
    }

    private sealed class FileLogger(string category, FileLoggerProvider owner, LogLevel minimumLevel) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var level = logLevel switch
            {
                LogLevel.Trace => "TRC",
                LogLevel.Debug => "DBG",
                LogLevel.Information => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "CRT",
                _ => "???",
            };

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {category}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            owner.TryEnqueue(line);
        }
    }
}
