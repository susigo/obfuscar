using System;
using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace Obfuscar.Wpf
{
    /// <summary>
    /// Feeds Obfuscar's LoggerService output into an observable collection
    /// so the log shows up in the window while a run is in progress.
    /// </summary>
    internal sealed class UiLogger : ILogger
    {
        private const int MaxLines = 5000;
        private readonly ObservableCollection<LogEntry> _lines;

        public UiLogger(ObservableCollection<LogEntry> lines)
        {
            _lines = lines;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception exception, Func<TState, Exception, string> formatter)
        {
            string message = formatter(state, exception);
            if (exception != null)
                message += "  " + exception.Message;

            var entry = new LogEntry(logLevel.ToString(), message);
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                Append(entry);
            else
                dispatcher.BeginInvoke(new Action(() => Append(entry)));
        }

        private void Append(LogEntry entry)
        {
            _lines.Add(entry);
            while (_lines.Count > MaxLines)
                _lines.RemoveAt(0);
        }
    }
}
