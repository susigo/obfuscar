using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml;
using System.Xml.Linq;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using Microsoft.Win32;

namespace Obfuscar.Wpf
{
    public partial class MainWindow : Window
    {
        private readonly ObfuscationProfile _profile = new ObfuscationProfile();
        private readonly ObservableCollection<string> _modules = new ObservableCollection<string>();
        private readonly ObservableCollection<LogEntry> _log = new ObservableCollection<LogEntry>();
        private bool _running;
        private bool _suppressLanguageChange;
        private string _statusKey = "Status_Ready";
        private object[] _statusArgs = Array.Empty<object>();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _profile;
            ModulesList.ItemsSource = _modules;
            LogList.ItemsSource = _log;
            ((INotifyCollectionChanged)_log).CollectionChanged += OnLogAdded;
            LoggerService.SetLogger(new UiLogger(_log));
            Loc.LanguageChanged += OnLanguageChanged;

            var csharp = LoadDarkHighlighting();
            OriginalEditor.SyntaxHighlighting = csharp;
            ObfuscatedEditor.SyntaxHighlighting = csharp;

            _suppressLanguageChange = true;
            LanguageBox.SelectedIndex = Loc.Language == "zh" ? 0 : 1;
            _suppressLanguageChange = false;

            PreviewKeyDown += OnWindowKeyDown;
            UpdateModuleCount();
            SetStatus("Status_Ready");

            var initial = Environment.GetCommandLineArgs().Skip(1).Where(File.Exists).ToArray();
            if (initial.Length > 0)
            {
                foreach (var file in initial)
                    _modules.Add(file);
                _profile.InPath = Path.GetDirectoryName(initial[0]);
                _profile.OutPath = Path.Combine(_profile.InPath, "Obfuscar_Output");
                ModulesList.SelectedItem = _modules[0];
                UpdateModuleCount();
            }
        }

        private static IHighlightingDefinition LoadDarkHighlighting()
        {
            const string resourceName = "Obfuscar.Wpf.Themes.CSharpDark.xshd";
            using (var stream = typeof(MainWindow).Assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    return HighlightingManager.Instance.GetDefinition("C#");
                using (var reader = new XmlTextReader(stream))
                {
                    var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                    HighlightingManager.Instance.RegisterHighlighting("CSharp Dark", new[] { ".cs" }, definition);
                    return definition;
                }
            }
        }

        // ---------- localization ----------

        private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressLanguageChange)
                return;
            Loc.Apply(LanguageBox.SelectedIndex == 0 ? "zh" : "en");
        }

        private void OnLanguageChanged()
        {
            RefreshPreviews();
            UpdateModuleCount();
            RenderStatus();
        }

        private void SetStatus(string key, params object[] args)
        {
            _statusKey = key;
            _statusArgs = args;
            RenderStatus();
        }

        private void RenderStatus() =>
            StatusText.Text = _statusArgs.Length == 0
                ? Loc.T(_statusKey)
                : string.Format(Loc.T(_statusKey), _statusArgs);

        // ---------- chrome ----------

        private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void OnMaximize(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void OnClose(object sender, RoutedEventArgs e) => Close();

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                OnRun(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
            {
                OnAddModules(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        // ---------- modules ----------

        private void OnAddModules(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = ".NET assemblies (*.dll;*.exe)|*.dll;*.exe|All files (*.*)|*.*",
                Multiselect = true,
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != true)
                return;

            foreach (var file in dialog.FileNames)
            {
                if (!_modules.Contains(file))
                    _modules.Add(file);
            }

            if (_modules.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(_profile.InPath))
                    _profile.InPath = Path.GetDirectoryName(_modules[0]);
                if (string.IsNullOrWhiteSpace(_profile.OutPath))
                    _profile.OutPath = Path.Combine(_profile.InPath, "Obfuscar_Output");
                ModulesList.SelectedItem = _modules[_modules.Count - 1];
            }

            UpdateModuleCount();
        }

        private void OnRemoveModule(object sender, RoutedEventArgs e)
        {
            if (ModulesList.SelectedItem is string selected)
                _modules.Remove(selected);
            UpdateModuleCount();
        }

        private void OnModuleSelected(object sender, SelectionChangedEventArgs e) => RefreshPreviews();

        private void UpdateModuleCount() =>
            ModulesCountText.Text = string.Format(
                Loc.T(_modules.Count == 1 ? "Count_Module" : "Count_Modules"), _modules.Count);

        // ---------- previews ----------

        private void RefreshPreviews()
        {
            var input = ModulesList.SelectedItem as string;
            if (input == null)
            {
                OriginalTitle.Text = Loc.T("Pane_Original");
                OriginalEditor.Text = "";
                OriginalEmpty.Visibility = Visibility.Visible;
                ObfuscatedTitle.Text = Loc.T("Pane_Obfuscated");
                ObfuscatedEditor.Text = "";
                ObfuscatedEmpty.Text = Loc.T("Empty_RunToCompare");
                ObfuscatedEmpty.Visibility = Visibility.Visible;
                return;
            }

            OriginalTitle.Text = Loc.T("Pane_Original") + "  ·  " + Path.GetFileName(input);
            OriginalEmpty.Visibility = Visibility.Collapsed;
            _ = DecompileIntoAsync(input, OriginalEditor);

            string output = OutputPathFor(input);
            if (File.Exists(output))
            {
                ObfuscatedTitle.Text = Loc.T("Pane_Obfuscated") + "  ·  " + Path.GetFileName(output);
                ObfuscatedEmpty.Visibility = Visibility.Collapsed;
                _ = DecompileIntoAsync(output, ObfuscatedEditor);
            }
            else
            {
                ObfuscatedTitle.Text = Loc.T("Pane_Obfuscated") + "  ·  " + Path.GetFileName(input);
                ObfuscatedEditor.Text = "";
                ObfuscatedEmpty.Text = Loc.T("Empty_NotObfuscated") + "\n" + output;
                ObfuscatedEmpty.Visibility = Visibility.Visible;
            }
        }

        private string OutputPathFor(string input)
        {
            string dir = string.IsNullOrWhiteSpace(_profile.OutPath)
                ? Path.GetDirectoryName(input)
                : _profile.OutPath;
            return Path.Combine(dir, Path.GetFileName(input));
        }

        private static async Task DecompileIntoAsync(string file, ICSharpCode.AvalonEdit.TextEditor editor)
        {
            string text;
            try
            {
                text = await Task.Run(() =>
                {
                    var settings = new DecompilerSettings { ThrowOnAssemblyResolveErrors = false };
                    return new CSharpDecompiler(file, settings).DecompileWholeModuleAsString();
                });
            }
            catch (Exception ex)
            {
                text = "// decompile failed: " + ex.Message;
            }

            editor.Text = text;
        }

        // ---------- run ----------

        private async void OnRun(object sender, RoutedEventArgs e)
        {
            if (_running)
                return;
            if (_modules.Count == 0)
            {
                SetStatus("Status_NoModules");
                return;
            }

            _running = true;
            RunButton.IsEnabled = false;
            RunPulse.Visibility = Visibility.Visible;
            RunStateText.Text = Loc.T("State_Running");
            SetStatus("Status_Obfuscating", _modules.Count);

            var document = _profile.BuildDocument(_modules);
            string dir = Path.Combine(Path.GetTempPath(), "Obfuscar.Wpf");
            Directory.CreateDirectory(dir);
            string projectFile = Path.Combine(dir, "ui-project.obfuscar");
            document.Save(projectFile);
            _log.Add(new LogEntry("Debug", string.Format(Loc.T("Log_ProjectWritten"), projectFile)));

            var stopwatch = Stopwatch.StartNew();
            try
            {
                await Task.Run(() =>
                {
                    var obfuscator = new Obfuscator(projectFile);
                    obfuscator.RunRules();
                });
                SetStatus("Status_Done", _profile.OutPath);
                RunStateText.Text = Loc.T("State_Done");
            }
            catch (Exception ex)
            {
                SetStatus("Status_Failed", ex.Message);
                RunStateText.Text = Loc.T("State_Failed");
                _log.Add(new LogEntry("Error", ex.ToString()));
            }
            finally
            {
                stopwatch.Stop();
                ElapsedText.Text = stopwatch.Elapsed.TotalSeconds.ToString("F1") + "s";
                _running = false;
                RunButton.IsEnabled = true;
                RunPulse.Visibility = Visibility.Collapsed;
                RefreshPreviews();
            }
        }

        private void OnRevealOutput(object sender, RoutedEventArgs e)
        {
            string dir = _profile.OutPath;
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                Process.Start("explorer.exe", "\"" + dir + "\"");
            else
                SetStatus("Status_OutputMissing");
        }

        // ---------- project files ----------

        private void OnOpenProject(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Obfuscar project (*.obfuscar;*.xml)|*.obfuscar;*.xml|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                var doc = XDocument.Load(dialog.FileName);
                _profile.Apply(doc);

                _modules.Clear();
                foreach (var module in doc.Root.Elements("Module"))
                {
                    var file = (string)module.Attribute("file");
                    if (!string.IsNullOrEmpty(file))
                        _modules.Add(Path.IsPathRooted(file)
                            ? file
                            : Path.GetFullPath(Path.Combine(_profile.InPath, file)));
                }

                UpdateModuleCount();
                if (_modules.Count > 0)
                    ModulesList.SelectedItem = _modules[0];
                SetStatus("Status_Loaded", Path.GetFileName(dialog.FileName));
            }
            catch (Exception ex)
            {
                SetStatus("Status_LoadFailed", ex.Message);
                _log.Add(new LogEntry("Error", ex.ToString()));
            }
        }

        private void OnSaveProject(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Obfuscar project (*.obfuscar)|*.obfuscar",
                FileName = "obfuscar.obfuscar"
            };
            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                _profile.BuildDocument(_modules).Save(dialog.FileName);
                SetStatus("Status_Saved", Path.GetFileName(dialog.FileName));
            }
            catch (Exception ex)
            {
                SetStatus("Status_SaveFailed", ex.Message);
            }
        }

        // ---------- editors / log ----------

        private void OnCopyOriginal(object sender, RoutedEventArgs e) => CopyToClipboard(OriginalEditor.Text);

        private void OnCopyObfuscated(object sender, RoutedEventArgs e) => CopyToClipboard(ObfuscatedEditor.Text);

        private static void CopyToClipboard(string text)
        {
            if (!string.IsNullOrEmpty(text))
                Clipboard.SetText(text);
        }

        private void OnToggleWrapOriginal(object sender, RoutedEventArgs e) =>
            OriginalEditor.WordWrap = !OriginalEditor.WordWrap;

        private void OnToggleWrapObfuscated(object sender, RoutedEventArgs e) =>
            ObfuscatedEditor.WordWrap = !ObfuscatedEditor.WordWrap;

        private void OnClearLog(object sender, RoutedEventArgs e)
        {
            _log.Clear();
            LogEmpty.Visibility = Visibility.Visible;
        }

        private void OnCopyLog(object sender, RoutedEventArgs e)
        {
            if (_log.Count > 0)
                Clipboard.SetText(string.Join(Environment.NewLine, _log.Select(entry => entry.ToString())));
        }

        private void OnLogAdded(object sender, NotifyCollectionChangedEventArgs e)
        {
            LogEmpty.Visibility = _log.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_log.Count > 0)
                LogList.ScrollIntoView(_log[_log.Count - 1]);
        }
    }
}
