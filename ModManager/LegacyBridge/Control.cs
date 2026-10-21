using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace WorkshopBridge
{
    [DataContract]
    public sealed class ModControl
    {
        [DataMember(Name="schemaVersion", IsRequired=true)] public int Schema = 1;
        [DataMember(Name="pauseAll", IsRequired=true)] public bool PauseAll;
        [DataMember(Name="disabledIds", IsRequired=true)] public string[] DisabledIds = new string[0];

        public static ModControl Read(string path)
        {
            if (!File.Exists(path)) return new ModControl();
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Mod control file is too large.");
            var value = Catalog.ReadJson<ModControl>(path);
            if (value.Schema != 1 || value.DisabledIds == null || value.DisabledIds.Length > 1024 ||
                value.DisabledIds.Any(x => x == null || !Regex.IsMatch(x, @"^[A-Za-z0-9._-]{1,160}$")))
                throw new InvalidDataException("Invalid Mod control settings.");
            return value;
        }

        public bool Allows(string id) => !PauseAll && !DisabledIds.Contains(id, StringComparer.OrdinalIgnoreCase);
    }
}
