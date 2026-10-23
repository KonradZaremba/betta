// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Betta.Services
{
    /// <summary>
    /// ILoggerProvider that appends every message to a file next to the .gha
    /// in %AppData%\Grasshopper\Libraries\Betta.log. Useful for post-mortem
    /// debugging: the log persists after Rhino closes, and tailing the file
    /// gives a clean stream without Rhino's own chatter mixed in.
    /// Every solve logs at Debug, so once the file passes <see cref="DefaultMaxBytes"/>
    /// it moves to Betta.log.old (replacing the previous one) and a fresh file starts.
    /// </summary>
    public sealed class FileLoggerProvider : ILoggerProvider
    {
        internal const long DefaultMaxBytes = 10 * 1024 * 1024;

        // The size is checked when a session starts and then every RollCheckInterval lines, not on every line.
        internal const int RollCheckInterval = 1000;

        // Static: Startup builds more than one provider, all writing the same file.
        private static readonly object _fileLock = new();
        private static int _writes;
        private readonly string _path;
        private readonly long _maxBytes;

        public FileLoggerProvider() : this(DefaultPath()) { }

        public FileLoggerProvider(string path) : this(path, DefaultMaxBytes) { }

        internal FileLoggerProvider(string path, long maxBytes)
        {
            _path = path;
            _maxBytes = maxBytes;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            Append($"--- Betta session started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---{Environment.NewLine}", checkSize: true);
        }

        public ILogger CreateLogger(string categoryName) => new FileLogger(this, ShortName(categoryName));

        public void Dispose() { }

        internal void Append(string text, bool checkSize = false)
        {
            lock (_fileLock)
            {
                if (checkSize || ++_writes % RollCheckInterval == 0) RollIfFull();
                // A log line is never worth failing a solve over: another Rhino, or a
                // tool holding the file, makes the append throw.
                try { File.AppendAllText(_path, text); }
                catch (IOException) { }
            }
        }

        private void RollIfFull()
        {
            try
            {
                var file = new FileInfo(_path);
                if (file.Exists && file.Length >= _maxBytes) File.Move(_path, _path + ".old", overwrite: true);
            }
            catch (IOException) { } // held open without delete sharing: try again at the next check
        }

        private static string ShortName(string category)
        {
            if (string.IsNullOrEmpty(category)) return "Betta";
            var dot = category.LastIndexOf('.');
            return dot < 0 ? category : category.Substring(dot + 1);
        }

        private static string DefaultPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Grasshopper", "Libraries", "Betta.log");
        }
    }

    internal sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter?.Invoke(state, exception) ?? state?.ToString();
            var line = $"{DateTime.Now:HH:mm:ss.fff} [{Label(logLevel)}] {_category}: {message}";
            if (exception != null) line += Environment.NewLine + exception;

            _provider.Append(line + Environment.NewLine);
        }

        private static string Label(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRIT",
            _ => level.ToString().ToUpperInvariant()
        };

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
