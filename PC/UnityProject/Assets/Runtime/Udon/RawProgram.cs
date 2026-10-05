using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;
using VRC.Udon.ProgramSources;

namespace LocalVrcw.Udon
{
    public sealed class RawProgram
    {
        public byte[] bytecode;
        public OdinDocument document;
        public readonly Dictionary<string, uint> entryMap = new Dictionary<string, uint>();
        public readonly Dictionary<string, uint> symbolMap = new Dictionary<string, uint>();
        public readonly Dictionary<uint, OdinNode> heapMap = new Dictionary<uint, OdinNode>();
        public string[] exportedEntries, exportedSymbols;
        public RawProgram(SerializedUdonProgramAsset asset)
        {
            using var packed = new MemoryStream(asset.serializedProgramCompressedBytes, false);
            using var gzip = new GZipStream(packed, CompressionMode.Decompress);
            using var decoded = new MemoryStream();
            var buffer = new byte[16384]; int size;
            while ((size = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (decoded.Length + size > 16 * 1024 * 1024) throw new InvalidDataException("Program decompression limit");
                decoded.Write(buffer, 0, size);
            }
            document = new OdinDocument(decoded.ToArray());
            var root = document.roots.Single(n => n.Named("ByteCode") != null);
            bytecode = root.Named("ByteCode").First.raw;
            ReadTable(root.Named("EntryPoints"), entryMap, out exportedEntries);
            ReadTable(root.Named("SymbolTable"), symbolMap, out exportedSymbols);
            var dump = root.Named("Heap").First.Named("HeapDump").First;
            foreach (var item in dump.children.Where(n => n.arrayIndex >= 0))
            {
                uint address = Convert.ToUInt32(item.Named("Item1").scalar);
                var box = item.Named("Item2");
                var value = box.Named("Value") ?? box.First ?? box;
                heapMap.Add(address, value);
            }
            ValidateBytecode();
        }
        static void ReadTable(OdinNode field, Dictionary<string, uint> table, out string[] exports)
        {
            var payload = field.First;
            foreach (var element in payload.Named("Symbols").First.children.Where(n => n.arrayIndex >= 0))
            {
                var symbol = element.First;
                table.Add((string)symbol.Named("Name").scalar, Convert.ToUInt32(symbol.Named("Address").scalar));
            }
            exports = payload.Named("ExportedSymbols").First.children.Where(n => n.arrayIndex >= 0).Select(n => (string)n.scalar).ToArray();
        }
        void ValidateBytecode()
        {
            var boundaries = new HashSet<uint>();
            for (uint pc = 0; pc < bytecode.Length;)
            {
                boundaries.Add(pc); uint op = UdonVM.ReadU32(bytecode, pc); pc += 4;
                if (!Enum.IsDefined(typeof(UdonVM.Op), op)) throw new InvalidDataException("Unknown Udon opcode");
                if (UdonVM.HasOperand((UdonVM.Op)op))
                {
                    uint operand = UdonVM.ReadU32(bytecode, pc); pc += 4;
                    if ((op == 1 || op == 6 || op == 8) && !heapMap.ContainsKey(operand))
                        throw new InvalidDataException("Invalid Udon heap operand");
                }
            }
            foreach (uint entry in entryMap.Values)
                if (!boundaries.Contains(entry)) throw new InvalidDataException("Invalid Udon entry");
            for (uint pc = 0; pc < bytecode.Length;)
            {
                uint op = UdonVM.ReadU32(bytecode, pc); pc += 4;
                if (!UdonVM.HasOperand((UdonVM.Op)op)) continue;
                uint operand = UdonVM.ReadU32(bytecode, pc); pc += 4;
                // Both 0xFFFFFFFF (indirect return) and 0xFFFFFFFC (direct halt)
                // occur in the approved native bytecode.
                if ((op == 4 || op == 5) && operand != uint.MaxValue && operand != 0xFFFFFFFC && !boundaries.Contains(operand))
                    throw new InvalidDataException("Invalid Udon branch");
            }
        }
    }

    // Explicit allowlist: serialized CLR names cannot cause arbitrary assembly loading.
    public static class RuntimeTypes
    {
        static readonly Dictionary<string, Type> types = new Dictionary<string, Type>();
        static RuntimeTypes()
        {
            Type[] allowed = { typeof(object), typeof(string), typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
                typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(char), typeof(decimal), typeof(Type),
                typeof(UnityEngine.Object), typeof(GameObject), typeof(Transform), typeof(Component), typeof(Behaviour), typeof(Renderer),
                typeof(MeshRenderer), typeof(Material), typeof(AudioSource), typeof(AudioClip), typeof(Animator), typeof(Collider),
                typeof(BoxCollider), typeof(Rigidbody), typeof(Camera), typeof(Light), typeof(CanvasGroup), typeof(Canvas), typeof(RectTransform),
                typeof(ParticleSystem), typeof(Texture), typeof(Texture2D), typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Color),
                typeof(Quaternion), typeof(LayerMask), typeof(RaycastHit), typeof(Collision), typeof(Space), typeof(FogMode), typeof(HumanBodyBones),
                typeof(CollisionDetectionMode), typeof(KeyCode), typeof(ForceMode), typeof(QueryTriggerInteraction), typeof(UnityEngine.UI.Toggle),
                typeof(AnimatorStateInfo), typeof(Time), typeof(Mathf), typeof(UnityEngine.Random), typeof(Debug), typeof(Resources),
                typeof(Physics), typeof(RenderSettings), typeof(Input), typeof(Application), typeof(Screen), typeof(PlayerPrefs), typeof(Array), typeof(Convert) };
            foreach (var type in allowed) types[type.FullName] = type;
            types["VRC.Udon.UdonBehaviour"] = typeof(UdonRuntime);
            types["VRC.Udon.Common.Interfaces.IUdonEventReceiver"] = typeof(UdonRuntime);
            types["VRC.SDK3.Components.VRCPickup"] = typeof(VRC.SDK3.Components.VRCPickup);
            types["VRC.SDKBase.VRC_Pickup"] = typeof(VRC.SDK3.Components.VRCPickup);
            types["VRC.SDKBase.VRCPlayerApi"] = typeof(LocalPlayer);
            types["VRC.SDKBase.VRCPlayerApi+TrackingData"] = typeof(TrackingData);
        }
        public static Type Resolve(string serialized)
        {
            if (serialized == null) return null;
            string name = serialized.Split(',')[0];
            if (name.EndsWith("[]", StringComparison.Ordinal))
            {
                Type element = Resolve(name.Substring(0, name.Length - 2));
                return element?.MakeArrayType();
            }
            if (types.TryGetValue(name, out var type)) return type;
            // Known VRC enum values are opaque Int32 data, not SDK implementations.
            if (name.StartsWith("VRC.") && (name.Contains("+TrackingDataType") || name.Contains("PickupHand") ||
                name.EndsWith("EventTiming") || name.EndsWith("NetworkEventTarget") || name.EndsWith("HandType"))) return typeof(int);
            return null;
        }
        public static Type Encoded(string encoded) => types.Values.Distinct().FirstOrDefault(t =>
            t.FullName.Replace(".", "").Replace("+", "") == encoded);
    }
}
