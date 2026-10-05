using System;

namespace LocalVrcw
{
    // Identifiers and expected structural counts only; no source content.
    public sealed class ExternalWorldProfile
    {
        public string Version { get; }
        public string Variant { get; }
        public string Sha256 { get; }
        public long Bytes { get; }
        public int GameObjects { get; }
        public int UdonHosts { get; }
        public int ObjectReferences { get; }
        public int Pickups { get; }
        ExternalWorldProfile(string version, string variant, string sha256, long bytes,
            int gameObjects, int udonHosts, int references, int pickups)
        {
            Version = version; Variant = variant; Sha256 = sha256; Bytes = bytes;
            GameObjects = gameObjects; UdonHosts = udonHosts; ObjectReferences = references;
            Pickups = pickups;
        }
        static readonly ExternalWorldProfile[] Approved = {
            new ExternalWorldProfile("v135", "raw",
                "ADC8209B247A5AF8722376932B3B496D86DCFF400F30E632500BC32A4274C930",
                81674354, 3546, 443, 1097, 123),
            new ExternalWorldProfile("v134", "security",
                "0A8CBB2D37EBE948EABA0B5C2EC49B837276B661D051DAFA679ABFB9216A0ED7",
                79968961, 3526, 431, 1073, 117)
        };
        public static bool AcceptsLength(long bytes)
        {
            foreach (var item in Approved) if (item.Bytes == bytes) return true;
            return false;
        }
        public static ExternalWorldProfile Identify(string sha256, long bytes)
        {
            foreach (var item in Approved)
                if (item.Bytes == bytes && string.Equals(item.Sha256, sha256, StringComparison.Ordinal)) return item;
            throw new System.IO.InvalidDataException("SHA-256 not on the approved Android VRCW whitelist. Version is detected from bytes, not the filename.");
        }
    }
}
