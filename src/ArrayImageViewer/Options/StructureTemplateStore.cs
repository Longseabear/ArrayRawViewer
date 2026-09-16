using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace ArrayImageViewer.Options
{
    [DataContract]
    internal sealed class StructureTemplateItem
    {
        [DataMember(Name = "ClassName")]
        public string ClassName { get; set; }
        [DataMember(Name = "DataAccess")]
        public string DataAccess { get; set; }
        [DataMember(Name = "WidthAccess")]
        public string WidthAccess { get; set; }
        [DataMember(Name = "HeightAccess")]
        public string HeightAccess { get; set; }
    }

    [DataContract]
    internal sealed class StructureTemplateDocument
    {
        [DataMember(Name = "FormatVersion")]
        public int FormatVersion { get; set; }
        [DataMember(Name = "Templates")]
        public List<StructureTemplateItem> Templates { get; set; }
    }

    internal sealed class StructureTemplateSettingsChangedEventArgs : EventArgs
    {
        public string SolutionIdentity { get; private set; }
        public IList<StructureTemplateItem> Templates { get; private set; }
        public int Revision { get; private set; }

        public StructureTemplateSettingsChangedEventArgs(string identity, List<StructureTemplateItem> templates, int revision)
        {
            SolutionIdentity = identity;
            Templates = templates.AsReadOnly();
            Revision = revision;
        }
    }

    internal static class StructureTemplateStore
    {
        public const string GlobalIdentity = "<global>";
        public static int Revision { get; private set; }
        public static event EventHandler SettingsChanged;
        private static readonly object writeGate = new object();

        public static void SaveSettings(string solutionIdentity, IList<StructureTemplateItem> templates, int seconds, bool debug, string path = null)
        {
            SaveSettingsScopes(new Dictionary<string, IList<StructureTemplateItem>> { { solutionIdentity, templates } }, seconds, debug, path);
        }

        internal static void SaveSettingsScopes(IDictionary<string, IList<StructureTemplateItem>> scopes, int seconds, bool debug, string path = null)
        {
            if (seconds < 1 || seconds > 120) throw new ArgumentOutOfRangeException("seconds");
            var definitionsByScope = new Dictionary<string, List<StructureTemplateItem>>(StringComparer.Ordinal);
            foreach (var scope in scopes) definitionsByScope.Add(scope.Key, PrepareDefinitions(scope.Value));
            var notifications = new List<StructureTemplateSettingsChangedEventArgs>();
            lock (writeGate)
            {
                var records = ReadRecords(path);
                var previous = new Dictionary<string, string>(records, StringComparer.Ordinal);
                foreach (var scope in definitionsByScope)
                {
                    var prefix = scope.Key + "\nstructure-template\n";
                    foreach (var key in new List<string>(records.Keys))
                        if (key.StartsWith(prefix, StringComparison.Ordinal)) records.Remove(key);
                    foreach (var definition in scope.Value)
                        records[prefix + definition.ClassName] = Serialize(new StructureTemplateDocument {
                            FormatVersion = 1, Templates = new List<StructureTemplateItem> { definition } });
                    records[scope.Key + "\nstructure-templates-initialized"] = "true";
                }
                records["search-timeout-seconds"] = seconds.ToString(CultureInfo.InvariantCulture);
                records["search-debug-enabled"] = debug ? "true" : "false";
                if (SameRecords(previous, records)) return;
                WriteRecords(records, path);
                foreach (var scope in definitionsByScope)
                    notifications.Add(new StructureTemplateSettingsChangedEventArgs(scope.Key, scope.Value, ++Revision));
            }
            // All edited scopes commit together, before any viewer notification.
            // Listeners must never run a debugger
            // evaluation here, nor turn a successful save into a reported failure.
            var changed = SettingsChanged;
            if (changed == null) return;
            foreach (var args in notifications)
            {
                foreach (EventHandler listener in changed.GetInvocationList())
                {
                    try { listener(null, args); }
                    catch (Exception exception) { System.Diagnostics.Trace.WriteLine("Template refresh failed: " + exception.Message); }
                }
            }
        }

        private static List<StructureTemplateItem> PrepareDefinitions(IList<StructureTemplateItem> templates)
        {
            var definitions = new List<StructureTemplateItem>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in templates)
            {
                Validate(item);
                var definition = new StructureTemplateItem { ClassName = item.ClassName.Trim(), DataAccess = item.DataAccess.Trim(),
                    WidthAccess = item.WidthAccess.Trim(), HeightAccess = item.HeightAccess.Trim() };
                if (!names.Add(definition.ClassName)) throw new ArgumentException("Duplicate template: " + definition.ClassName);
                definitions.Add(definition);
            }
            return definitions;
        }

        private static bool SameRecords(Dictionary<string, string> first, Dictionary<string, string> second)
        {
            if (first.Count != second.Count) return false;
            foreach (var pair in first)
            {
                string value;
                if (!second.TryGetValue(pair.Key, out value) || pair.Value != value) return false;
            }
            return true;
        }
        public static bool LoadSearchDebug(string path = null)
        {
            string value;
            return ReadRecords(path).TryGetValue("search-debug-enabled", out value) && value == "true";
        }

        public static int LoadSearchSeconds(string path = null)
        {
            string value;
            int seconds;
            return ReadRecords(path).TryGetValue("search-timeout-seconds", out value) &&
                Int32.TryParse(value, out seconds) && seconds >= 1 && seconds <= 120 ? seconds : 10;
        }

        private static string PathValue
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArrayImageViewer", "structure-templates-v1.txt"); }
        }

        public static List<StructureTemplateItem> Load(string solutionIdentity, string path = null)
        {
            var merged = new Dictionary<string, StructureTemplateItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in LoadScope(GlobalIdentity, path)) merged[item.ClassName] = item;
            if (solutionIdentity != GlobalIdentity)
                foreach (var item in LoadScope(solutionIdentity, path)) merged[item.ClassName] = item;
            return new List<StructureTemplateItem>(merged.Values);
        }

        public static List<StructureTemplateItem> LoadScope(string solutionIdentity, string path = null)
        {
            var result = new List<StructureTemplateItem>();
            var prefix = solutionIdentity + "\nstructure-template\n";
            var records = ReadRecords(path);
            foreach (var pair in records)
            {
                if (!pair.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var document = Deserialize(pair.Value);
                if (document == null || document.FormatVersion != 1 || document.Templates == null || document.Templates.Count != 1) continue;
                try { Validate(document.Templates[0]); result.Add(document.Templates[0]); } catch (Exception) { }
            }

            // Make the shipped native debuggee immediately usable. This is a
            // display-only starter row until the user presses Save, and is
            // scoped to this repository's sample solution so it never
            // pollutes an unrelated solution's template list.
            if (result.Count == 0 && !records.ContainsKey(solutionIdentity + "\nstructure-templates-initialized") && IsArrayImageViewerSampleSolution(solutionIdentity))
            {
                result.Add(new StructureTemplateItem
                {
                    ClassName = "ImageStream",
                    DataAccess = "m_data",
                    WidthAccess = "m_width",
                    HeightAccess = "m_height"
                });
            }
            return result;
        }

        private static bool IsArrayImageViewerSampleSolution(string solutionIdentity)
        {
            return !String.IsNullOrWhiteSpace(solutionIdentity) &&
                solutionIdentity.IndexOfAny(Path.GetInvalidPathChars()) < 0 &&
                String.Equals(Path.GetFileName(solutionIdentity), "ArrayImageViewer.sln", StringComparison.OrdinalIgnoreCase);
        }

        public static List<StructureTemplateItem> Import(string fileName)
        {
            var document = Deserialize(File.ReadAllText(fileName, Encoding.UTF8));
            if (document == null || document.FormatVersion != 1 || document.Templates == null) throw new InvalidDataException("This is not a supported Array RAW structure-template JSON file.");
            for (var index = 0; index < document.Templates.Count; index++) Validate(document.Templates[index]);
            return document.Templates;
        }

        public static void Export(string fileName, IList<StructureTemplateItem> templates)
        {
            for (var index = 0; index < templates.Count; index++) Validate(templates[index]);
            File.WriteAllText(fileName, Serialize(new StructureTemplateDocument { FormatVersion = 1, Templates = new List<StructureTemplateItem>(templates) }), Encoding.UTF8);
        }

        internal static void Validate(StructureTemplateItem item)
        {
            if (item == null || String.IsNullOrWhiteSpace(item.ClassName)) throw new ArgumentException("Every template requires a class/template name.");
            if (String.IsNullOrWhiteSpace(item.DataAccess)) throw new ArgumentException("Template '" + item.ClassName + "' requires RAW data access.");
            if (String.IsNullOrWhiteSpace(item.WidthAccess)) throw new ArgumentException("Template '" + item.ClassName + "' requires Width access.");
            if (String.IsNullOrWhiteSpace(item.HeightAccess)) throw new ArgumentException("Template '" + item.ClassName + "' requires Height access.");
        }

        private static string Serialize(StructureTemplateDocument document)
        {
            using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(StructureTemplateDocument)).WriteObject(stream, document); return Encoding.UTF8.GetString(stream.ToArray()); }
        }

        private static StructureTemplateDocument Deserialize(string value)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(value ?? String.Empty))) { return new DataContractJsonSerializer(typeof(StructureTemplateDocument)).ReadObject(stream) as StructureTemplateDocument; }
        }

        private static Dictionary<string, string> ReadRecords(string path = null)
        {
            var records = new Dictionary<string, string>(StringComparer.Ordinal);
            path = path ?? PathValue;
            if (!File.Exists(path)) return records;
            foreach (var line in File.ReadAllLines(path))
            {
                var tab = line.IndexOf('\t');
                if (tab > 0) records[Decode(line.Substring(0, tab))] = line.Substring(tab + 1);
            }
            return records;
        }

        private static void WriteRecords(Dictionary<string, string> records, string path = null)
        {
            path = path ?? PathValue;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var lines = new List<string>();
            foreach (var pair in records) lines.Add(Encode(pair.Key) + "\t" + pair.Value);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllLines(temporary, lines.ToArray());
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static string Encode(string value) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? String.Empty)); }
        private static string Decode(string value) { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
    }
}
