namespace Obfuscar.Wpf
{
    internal sealed class LogEntry
    {
        public LogEntry(string level, string text)
        {
            Level = level;
            Text = text;
        }

        public string Level { get; }

        public string Text { get; }

        public override string ToString() => "[" + Level + "] " + Text;
    }
}
