using System.Globalization;
using System.Windows;

namespace Obfuscar.Wpf
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            string language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh" : "en";
            Loc.Apply(language);
            base.OnStartup(e);
        }
    }
}
