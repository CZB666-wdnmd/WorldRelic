using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace LocalVrcw
{
    public static class ExternalInputPaths
    {
        public const string DefaultWorldName = "AniqueMuseum-windows.vrcw";
        public const long FontBytes = 4149288;
        public const string FontSha256 = "9C32CC9CFF2D04AC95B0E8FCE51BA439083E473581B71CAE69AEAA8729E881B3";
        public static string ExecutableDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        public static string DefaultWorld => Path.Combine(ExecutableDirectory, DefaultWorldName);
        public static string ResolveFont(string directory, string supplied)
        {
            if (!string.IsNullOrEmpty(supplied))
            {
                string path = Path.GetFullPath(supplied);
                if (!File.Exists(path)) throw new FileNotFoundException("External font not found", path);
                return ValidateFont(path);
            }
            foreach (string path in Directory.EnumerateFiles(directory).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try { if (new FileInfo(path).Length == FontBytes && HashFont(path) == FontSha256) return path; }
                catch (IOException) { } // An unrelated locked file is not a font candidate.
                catch (UnauthorizedAccessException) { }
            }
            throw new InvalidDataException("Place the approved VL-PGothic-Regular.ttf beside the EXE (renaming is allowed), or use --font <path>. Required SHA256: " + FontSha256 + ". No other font or OS fallback is accepted.");
        }
        static string ValidateFont(string path)
        {
            if (new FileInfo(path).Length != FontBytes || HashFont(path) != FontSha256)
                throw new InvalidDataException("External font SHA256 is not approved. Only the exact VL-PGothic-Regular.ttf is accepted (renaming is allowed): " + FontSha256);
            return path;
        }
        static string HashFont(string path)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "");
        }
    }
}
