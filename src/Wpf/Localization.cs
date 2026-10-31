using System;
using System.Linq;
using System.Windows;

namespace Obfuscar.Wpf
{
    /// <summary>
    /// Swaps the merged string dictionary at runtime. XAML uses DynamicResource
    /// so switching the language updates the UI in place.
    /// </summary>
    internal static class Loc
    {
        public static string Language { get; private set; } = "en";

        public static event Action LanguageChanged;

        public static void Apply(string language)
        {
            if (language != "zh")
                language = "en";

            var resources = Application.Current.Resources;
            var dictionaries = resources.MergedDictionaries;
            var existing = dictionaries.FirstOrDefault(d =>
                d.Source != null && d.Source.OriginalString.Contains("Strings."));
            if (existing != null)
                dictionaries.Remove(existing);

            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("Themes/Strings." + language + ".xaml", UriKind.Relative)
            });

            Language = language;
            LanguageChanged?.Invoke();
        }

        public static string T(string key) =>
            Application.Current.TryFindResource(key) as string ?? key;
    }
}
