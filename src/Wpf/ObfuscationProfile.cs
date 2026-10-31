using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Obfuscar.Wpf
{
    /// <summary>
    /// Settings shown in the left pane. Defaults mirror Obfuscar's Settings
    /// class; every bound value is emitted explicitly into the project XML.
    /// </summary>
    internal sealed class ObfuscationProfile : INotifyPropertyChanged
    {
        private string _inPath = "";
        private string _outPath = "";
        private bool _markedOnly;
        private bool _renameFields = true;
        private bool _renameProperties = true;
        private bool _keepProperties;
        private bool _renameEvents = true;
        private bool _keepPublicApi = true;
        private bool _hidePrivateApi = true;
        private bool _reuseNames = true;
        private bool _useUnicodeNames;
        private bool _useKoreanNames;
        private bool _hideStrings = true;
        private bool _optimize = true;
        private bool _suppressIldasm = true;
        private bool _xmlMapping;
        private bool _regenerateDebugInfo;
        private bool _analyzeXaml;
        private bool _skipGenerated;
        private bool _skipSpecialName;
        private string _customChars = "";
        private bool _injectBait = true;
        private string _baitTypeCount = "120";
        private string _baitMethodsPerType = "8";
        private string _baitFieldsPerType = "3";
        private string _baitSeed = "-1";
        private string _baitWordList = "";
        private string _baitStringEncoding = "mixed";
        private bool _baitIntoMethods = true;
        private bool _baitSensitive = true;
        private bool _flattenControlFlow;
        private string _cffMinInstructions = "12";

        public string InPath { get => _inPath; set => Set(ref _inPath, value); }
        public string OutPath { get => _outPath; set => Set(ref _outPath, value); }
        public bool MarkedOnly { get => _markedOnly; set => Set(ref _markedOnly, value); }
        public bool RenameFields { get => _renameFields; set => Set(ref _renameFields, value); }
        public bool RenameProperties { get => _renameProperties; set => Set(ref _renameProperties, value); }
        public bool KeepProperties { get => _keepProperties; set => Set(ref _keepProperties, value); }
        public bool RenameEvents { get => _renameEvents; set => Set(ref _renameEvents, value); }
        public bool KeepPublicApi { get => _keepPublicApi; set => Set(ref _keepPublicApi, value); }
        public bool HidePrivateApi { get => _hidePrivateApi; set => Set(ref _hidePrivateApi, value); }
        public bool ReuseNames { get => _reuseNames; set => Set(ref _reuseNames, value); }
        public bool UseUnicodeNames { get => _useUnicodeNames; set => Set(ref _useUnicodeNames, value); }
        public bool UseKoreanNames { get => _useKoreanNames; set => Set(ref _useKoreanNames, value); }
        public bool HideStrings { get => _hideStrings; set => Set(ref _hideStrings, value); }
        public bool Optimize { get => _optimize; set => Set(ref _optimize, value); }
        public bool SuppressIldasm { get => _suppressIldasm; set => Set(ref _suppressIldasm, value); }
        public bool XmlMapping { get => _xmlMapping; set => Set(ref _xmlMapping, value); }
        public bool RegenerateDebugInfo { get => _regenerateDebugInfo; set => Set(ref _regenerateDebugInfo, value); }
        public bool AnalyzeXaml { get => _analyzeXaml; set => Set(ref _analyzeXaml, value); }
        public bool SkipGenerated { get => _skipGenerated; set => Set(ref _skipGenerated, value); }
        public bool SkipSpecialName { get => _skipSpecialName; set => Set(ref _skipSpecialName, value); }
        public string CustomChars { get => _customChars; set => Set(ref _customChars, value); }
        public bool InjectBait { get => _injectBait; set => Set(ref _injectBait, value); }
        public string BaitTypeCount { get => _baitTypeCount; set => Set(ref _baitTypeCount, value); }
        public string BaitMethodsPerType { get => _baitMethodsPerType; set => Set(ref _baitMethodsPerType, value); }
        public string BaitFieldsPerType { get => _baitFieldsPerType; set => Set(ref _baitFieldsPerType, value); }
        public string BaitSeed { get => _baitSeed; set => Set(ref _baitSeed, value); }
        public string BaitWordList { get => _baitWordList; set => Set(ref _baitWordList, value); }
        public string BaitStringEncoding { get => _baitStringEncoding; set => Set(ref _baitStringEncoding, value); }
        public bool BaitIntoMethods { get => _baitIntoMethods; set => Set(ref _baitIntoMethods, value); }
        public bool BaitSensitive { get => _baitSensitive; set => Set(ref _baitSensitive, value); }
        public bool FlattenControlFlow { get => _flattenControlFlow; set => Set(ref _flattenControlFlow, value); }
        public string CffMinInstructions { get => _cffMinInstructions; set => Set(ref _cffMinInstructions, value); }

        public XDocument BuildDocument(IEnumerable<string> modules)
        {
            var vars = new List<XElement>();
            void Var(string name, object value)
            {
                string text = value?.ToString()?.Trim() ?? "";
                if (text.Length > 0)
                    vars.Add(new XElement("Var",
                        new XAttribute("name", name), new XAttribute("value", text)));
            }
            void Flag(string name, bool value) => vars.Add(new XElement("Var",
                new XAttribute("name", name), new XAttribute("value", value ? "true" : "false")));

            Var("InPath", InPath);
            Var("OutPath", OutPath);
            Flag("MarkedOnly", MarkedOnly);
            Flag("RenameFields", RenameFields);
            Flag("RenameProperties", RenameProperties);
            Flag("KeepProperties", KeepProperties);
            Flag("RenameEvents", RenameEvents);
            Flag("KeepPublicApi", KeepPublicApi);
            Flag("HidePrivateApi", HidePrivateApi);
            Flag("ReuseNames", ReuseNames);
            Flag("UseUnicodeNames", UseUnicodeNames);
            Flag("UseKoreanNames", UseKoreanNames);
            Flag("HideStrings", HideStrings);
            Flag("OptimizeMethods", Optimize);
            Flag("SuppressIldasm", SuppressIldasm);
            Flag("XmlMapping", XmlMapping);
            Flag("RegenerateDebugInfo", RegenerateDebugInfo);
            Flag("AnalyzeXaml", AnalyzeXaml);
            Flag("SkipGenerated", SkipGenerated);
            Flag("SkipSpecialName", SkipSpecialName);
            Var("CustomChars", CustomChars);
            Flag("InjectBait", InjectBait);
            Var("BaitTypeCount", BaitTypeCount);
            Var("BaitMethodsPerType", BaitMethodsPerType);
            Var("BaitFieldsPerType", BaitFieldsPerType);
            Var("BaitSeed", BaitSeed);
            Var("BaitWordList", BaitWordList);
            Var("BaitStringEncoding", BaitStringEncoding);
            Flag("BaitIntoMethods", BaitIntoMethods);
            Flag("BaitSensitive", BaitSensitive);
            Flag("FlattenControlFlow", FlattenControlFlow);
            Var("CffMinInstructions", CffMinInstructions);

            var doc = new XDocument(new XElement("Obfuscator", vars));
            foreach (var module in modules)
                doc.Root.Add(new XElement("Module", new XAttribute("file", module)));
            return doc;
        }

        public void Apply(XDocument doc)
        {
            var vars = doc.Root?.Elements("Var")
                .Where(e => e.Attribute("name") != null)
                .ToDictionary(
                    e => (string)e.Attribute("name"),
                    e => (string)e.Attribute("value") ?? "",
                    StringComparer.OrdinalIgnoreCase);
            if (vars == null)
                return;

            string S(string name, string fallback) => vars.TryGetValue(name, out var v) ? v : fallback;
            bool B(string name, bool fallback) =>
                vars.TryGetValue(name, out var v) && bool.TryParse(v, out var b) ? b : fallback;

            InPath = S("InPath", InPath);
            OutPath = S("OutPath", OutPath);
            MarkedOnly = B("MarkedOnly", MarkedOnly);
            RenameFields = B("RenameFields", RenameFields);
            RenameProperties = B("RenameProperties", RenameProperties);
            KeepProperties = B("KeepProperties", KeepProperties);
            RenameEvents = B("RenameEvents", RenameEvents);
            KeepPublicApi = B("KeepPublicApi", KeepPublicApi);
            HidePrivateApi = B("HidePrivateApi", HidePrivateApi);
            ReuseNames = B("ReuseNames", ReuseNames);
            UseUnicodeNames = B("UseUnicodeNames", UseUnicodeNames);
            UseKoreanNames = B("UseKoreanNames", UseKoreanNames);
            HideStrings = B("HideStrings", HideStrings);
            Optimize = B("OptimizeMethods", Optimize);
            SuppressIldasm = B("SuppressIldasm", SuppressIldasm);
            XmlMapping = B("XmlMapping", XmlMapping);
            RegenerateDebugInfo = B("RegenerateDebugInfo", RegenerateDebugInfo);
            AnalyzeXaml = B("AnalyzeXaml", AnalyzeXaml);
            SkipGenerated = B("SkipGenerated", SkipGenerated);
            SkipSpecialName = B("SkipSpecialName", SkipSpecialName);
            CustomChars = S("CustomChars", CustomChars);
            InjectBait = B("InjectBait", InjectBait);
            BaitTypeCount = S("BaitTypeCount", BaitTypeCount);
            BaitMethodsPerType = S("BaitMethodsPerType", BaitMethodsPerType);
            BaitFieldsPerType = S("BaitFieldsPerType", BaitFieldsPerType);
            BaitSeed = S("BaitSeed", BaitSeed);
            BaitWordList = S("BaitWordList", BaitWordList);
            BaitStringEncoding = S("BaitStringEncoding", BaitStringEncoding);
            BaitIntoMethods = B("BaitIntoMethods", BaitIntoMethods);
            BaitSensitive = B("BaitSensitive", BaitSensitive);
            FlattenControlFlow = B("FlattenControlFlow", FlattenControlFlow);
            CffMinInstructions = S("CffMinInstructions", CffMinInstructions);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
