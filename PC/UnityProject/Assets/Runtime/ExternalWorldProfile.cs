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
                "314B3EEF87035E07A4B1EB02DD976F40A3648EB94AB4FAE961149E175615F66B",
                91207817, 3546, 443, 1097, 123),
            new ExternalWorldProfile("v134", "security",
                "9DF63F11D07C971C21BE3239882DC7C5667B2D46F36C01325C8F1BEA123F7E2B",
                88996960, 3526, 431, 1073, 117)
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
            throw new System.IO.InvalidDataException("SHA-256 not on the approved Windows VRCW whitelist. Version is detected from bytes, not the filename.");
        }
    }
}
