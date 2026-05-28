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
    /// </summary>
    public sealed class FileLoggerProvider : ILoggerProvider
    {
        private static readonly object _fileLock = new();
        private readonly string _path;

        public FileLoggerProvider() : this(DefaultPath()) { }

        public FileLoggerProvider(string path)
        {
            _path = path;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.AppendAllText(_path,
                $"--- Betta session started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---{Environment.NewLine}");
        }

        public ILogger CreateLogger(string categoryName) =>
            new FileLogger(_path, _fileLock, ShortName(categoryName));

        public void Dispose() { }

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
        private readonly string _path;
        private readonly object _lock;
        private readonly string _category;

        public FileLogger(string path, object fileLock, string category)
        {
            _path = path;
            _lock = fileLock;
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

            lock (_lock)
            {
                File.AppendAllText(_path, line + Environment.NewLine);
            }
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
