using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LocalVrcw.Udon
{
    // Reads data only. No BinaryFormatter, arbitrary CLR object construction or type activation.
    public sealed class OdinNode
    {
        public byte tag;
        public string name, typeName;
        public int parent = -1, arrayIndex = -1, referenceId = -1, width;
        public long count;
        public byte[] raw = Array.Empty<byte>();
        public object scalar;
        public readonly List<OdinNode> children = new List<OdinNode>();
        public OdinNode Named(string field) => children.Find(n => n.name == field);
        public OdinNode First => children.Count == 0 ? null : children[0];
        public bool IsReference => tag == 1 || tag == 2;
        public bool IsStruct => tag == 3 || tag == 4;
        public bool IsInternal => tag == 9 || tag == 10;
        public bool IsExternal => tag == 11 || tag == 12;
        public bool IsNull => tag == 45 || tag == 46;
    }

    public sealed class OdinDocument
    {
        public readonly List<OdinNode> nodes = new List<OdinNode>();
        public readonly List<OdinNode> roots = new List<OdinNode>();
        public readonly Dictionary<int, OdinNode> references = new Dictionary<int, OdinNode>();
        readonly Dictionary<int, string> types = new Dictionary<int, string>();
        readonly BinaryReader reader;
        sealed class Frame { public OdinNode node; public int id, next; }

        public OdinDocument(byte[] bytes)
        {
            if (bytes == null || bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("Odin size limit");
            reader = new BinaryReader(new MemoryStream(bytes, false));
            var stack = new Stack<Frame>();
            while (reader.BaseStream.Position < bytes.Length)
            {
                byte tag = reader.ReadByte();
                if (tag == 49)
                {
                    if (reader.BaseStream.Position != bytes.Length) throw new InvalidDataException("Trailing Odin bytes");
                    break;
                }
                if (tag == 5 || tag == 7)
                {
                    if (stack.Count == 0 || (tag == 7) != (stack.Peek().node.tag == 6))
                        throw new InvalidDataException("Unbalanced Odin container");
                    Frame closed = stack.Pop();
                    // Dictionary arrays declare pair counts: two children per pair.
                    // The schema reader validates element shape instead of assuming
                    // that every StartArray is a CLR positional array.
                    continue;
                }
                if (nodes.Count >= 500000 || stack.Count > 128) throw new InvalidDataException("Odin graph limit");
                var n = new OdinNode { tag = tag };
                bool push = false, consume = true;
                if (tag >= 1 && tag <= 4)
                {
                    if (tag == 1 || tag == 3) n.name = ReadString();
                    n.typeName = ReadType();
                    if (tag <= 2) n.referenceId = reader.ReadInt32();
                    push = true;
                }
                else if (tag == 6)
                {
                    n.count = reader.ReadInt64();
                    if (n.count < 0 || n.count > 500000) throw new InvalidDataException("Odin array limit");
                    push = true; consume = false;
                }
                else if (tag == 8)
                {
                    n.count = reader.ReadInt32(); n.width = reader.ReadInt32();
                    if (n.count < 0 || n.width < 1 || n.width > 16) throw new InvalidDataException("Odin primitive array shape");
                    n.raw = Exact(checked((int)n.count * n.width));
                }
                else if ((tag >= 9 && tag <= 46) || tag == 50 || tag == 51)
                {
                    bool named = tag <= 46 ? (tag % 2 == 1) : tag == 50;
                    if (named) n.name = ReadString();
                    byte baseTag = named ? tag : (byte)(tag - 1);
                    long start = reader.BaseStream.Position;
                    switch (baseTag)
                    {
                        case 9: case 11: n.scalar = reader.ReadInt32(); break;
                        case 13: case 41: n.scalar = Exact(16); break;
                        case 15: n.scalar = reader.ReadSByte(); break;
                        case 17: n.scalar = reader.ReadByte(); break;
                        case 19: n.scalar = reader.ReadInt16(); break;
                        case 21: n.scalar = reader.ReadUInt16(); break;
                        case 23: n.scalar = reader.ReadInt32(); break;
                        case 25: n.scalar = reader.ReadUInt32(); break;
                        case 27: n.scalar = reader.ReadInt64(); break;
                        case 29: n.scalar = reader.ReadUInt64(); break;
                        case 31: n.scalar = reader.ReadSingle(); break;
                        case 33: n.scalar = reader.ReadDouble(); break;
                        case 35: n.scalar = new decimal(new[] { reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32() }); break;
                        case 37: n.scalar = (char)reader.ReadUInt16(); break;
                        case 39: case 50: n.scalar = ReadString(); break;
                        case 43: n.scalar = reader.ReadByte() != 0; break;
                        case 45: break;
                        default: throw new InvalidDataException("Unsupported Odin scalar " + tag);
                    }
                    long end = reader.BaseStream.Position;
                    reader.BaseStream.Position = start;
                    n.raw = Exact(checked((int)(end - start)));
                }
                else throw new InvalidDataException("Unsupported Odin tag " + tag);
                if (stack.Count > 0)
                {
                    Frame f = stack.Peek(); n.parent = f.id; f.node.children.Add(n);
                    if (consume && f.node.tag == 6) n.arrayIndex = f.next++;
                }
                else roots.Add(n);
                int id = nodes.Count; nodes.Add(n);
                if (n.IsReference && n.referenceId >= 0)
                {
                    if (references.ContainsKey(n.referenceId)) throw new InvalidDataException("Duplicate Odin reference");
                    references.Add(n.referenceId, n);
                }
                if (push) stack.Push(new Frame { node = n, id = id });
            }
            if (stack.Count != 0) throw new InvalidDataException("Unclosed Odin graph");
            reader.Dispose();
        }

        byte[] Exact(int size)
        {
            if (size < 0 || size > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("Truncated Odin payload");
            return reader.ReadBytes(size);
        }
        string ReadString()
        {
            byte flag = reader.ReadByte(); int count = reader.ReadInt32();
            if (count < 0 || count > 8 * 1024 * 1024 || flag > 1) throw new InvalidDataException("Odin string limit/encoding");
            byte[] raw = Exact(checked(count * (flag + 1)));
            if (flag == 1) return Encoding.Unicode.GetString(raw);
            var chars = new char[count]; for (int i = 0; i < count; i++) chars[i] = (char)raw[i];
            return new string(chars);
        }
        string ReadType()
        {
            byte tag = reader.ReadByte();
            if (tag == 46) return null;
            if (tag != 47 && tag != 48) throw new InvalidDataException("Odin type record");
            int id = reader.ReadInt32();
            if (tag == 47)
            {
                string name = ReadString(); types.Add(id, name); return name;
            }
            if (!types.TryGetValue(id, out string existing)) throw new InvalidDataException("Unknown Odin type ID");
            return existing;
        }

        // AST fingerprint, independently reproducible in the development oracle.
        public string Fingerprint()
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
            foreach (var n in nodes)
            {
                writer.Write(n.tag); writer.Write(n.parent); writer.Write(n.arrayIndex);
                WriteString(writer, n.name); WriteString(writer, n.typeName);
                writer.Write(n.referenceId); writer.Write(n.count); writer.Write(n.width);
                writer.Write(n.raw.Length); writer.Write(n.raw);
            }
            writer.Flush(); using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "");
        }
        static void WriteString(BinaryWriter writer, string value)
        {
            if (value == null) { writer.Write(-1); return; }
            byte[] bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes);
        }
    }
}
