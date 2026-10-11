using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace ClickDeck
{
    [DataContract]
    public sealed class ActionSpec
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Label = "";
        [DataMember] public string Kind = "shortcut";
        [DataMember] public string Shortcut = "";
        [DataMember] public string Text = "";
        [DataMember] public bool Enabled = true;
        public ActionSpec Copy() { return (ActionSpec)MemberwiseClone(); }
        public static ActionSpec Key(string label, string shortcut, bool enabled)
        { return new ActionSpec { Label = label, Shortcut = shortcut, Enabled = enabled }; }
    }
    [DataContract]
    public sealed class Preferences
    {
        [DataMember] public int Version = 1;
        [DataMember] public List<ActionSpec> Actions = new List<ActionSpec>();
        [DataMember] public int Columns = 2;
        [DataMember] public bool Compact = false;
        [DataMember] public bool ShowHints = true;
        [DataMember] public int OpacityPercent = 100;
        [DataMember] public bool HasPosition;
        [DataMember] public int X, Y;
        public Preferences Copy()
        {
            Preferences copy = (Preferences)MemberwiseClone();
            copy.Actions = Actions.ConvertAll(a => a.Copy()); return copy;
        }
        public static Preferences Defaults()
        {
            Preferences p = new Preferences();
            p.Actions.Add(ActionSpec.Key("复制", "Ctrl+C", true));
            p.Actions.Add(ActionSpec.Key("粘贴", "Ctrl+V", true));
            p.Actions.Add(ActionSpec.Key("全选", "Ctrl+A", true));
            p.Actions.Add(ActionSpec.Key("剪切", "Ctrl+X", false));
            p.Actions.Add(ActionSpec.Key("撤销", "Ctrl+Z", true));
            p.Actions.Add(ActionSpec.Key("重做", "Ctrl+Y", false));
            p.Actions.Add(ActionSpec.Key("回车", "Enter", true));
            p.Actions.Add(ActionSpec.Key("换行", "Shift+Enter", true));
            p.Actions.Add(ActionSpec.Key("退格", "Backspace", false));
            p.Actions.Add(ActionSpec.Key("删除", "Delete", false));
            p.Actions.Add(ActionSpec.Key("保存", "Ctrl+S", false));
            p.Actions.Add(ActionSpec.Key("Tab", "Tab", false));
            p.Actions.Add(ActionSpec.Key("取消", "Esc", false));
            return p;
        }
        public void Normalize()
        {
            Columns = Math.Max(1, Math.Min(6, Columns));
            OpacityPercent = Math.Max(45, Math.Min(100, OpacityPercent));
            if (Actions == null) Actions = Defaults().Actions;
            if (Actions.Count > 80) Actions = Actions.GetRange(0, 80);
            Actions.RemoveAll(a => a == null);
            HashSet<string> ids = new HashSet<string>();
            foreach (ActionSpec a in Actions)
            {
                if (String.IsNullOrEmpty(a.Id) || !ids.Add(a.Id)) { a.Id = Guid.NewGuid().ToString("N"); ids.Add(a.Id); }
                a.Label = String.IsNullOrWhiteSpace(a.Label) ? "快捷操作" : a.Label;
                if (a.Label.Length > 24) a.Label = a.Label.Substring(0, 24);
                a.Kind = a.Kind == "text" ? "text" : "shortcut";
                a.Text = a.Text ?? "";
                if (a.Text.Length > 4000) a.Text = a.Text.Substring(0, 4000);
                if (a.Kind == "shortcut")
                {
                    Chord chord; string error;
                    if (Chord.TryParse(a.Shortcut, out chord, out error)) a.Shortcut = chord.Display;
                    else a.Enabled = false;
                }
            }
        }
    }
    public sealed class PreferenceStore
    {
        public readonly string FilePath;
        public string LoadNotice;
        public PreferenceStore(string filePath) { FilePath = filePath; }
        public Preferences Load()
        {
            if (!File.Exists(FilePath)) return Preferences.Defaults();
            try
            {
                using (FileStream stream = File.OpenRead(FilePath))
                {
                    Preferences p = (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(stream);
                    if (p == null || p.Version != 1) throw new InvalidDataException("不支持的配置版本");
                    p.Normalize(); return p;
                }
            }
            catch (Exception e)
            {
                if (!(e is IOException || e is SerializationException || e is UnauthorizedAccessException || e is InvalidDataException || e is ArgumentException)) throw;
                string backup = FilePath + ".invalid-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                try { File.Copy(FilePath, backup); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                LoadNotice = "原设置无法读取，已载入默认按钮。原文件已尽可能保留。";
                return Preferences.Defaults();
            }
        }
        public void Save(Preferences prefs)
        {
            string directory = Path.GetDirectoryName(FilePath);
            Directory.CreateDirectory(directory);
            string temporary = FilePath + ".tmp";
            try
            {
                using (FileStream stream = File.Create(temporary)) new DataContractJsonSerializer(typeof(Preferences)).WriteObject(stream, prefs);
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
                else File.Move(temporary, FilePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
