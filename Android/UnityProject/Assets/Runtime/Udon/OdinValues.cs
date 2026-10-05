using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LocalVrcw.Udon
{
    public sealed class OdinValues
    {
        readonly OdinDocument document;
        readonly UnityEngine.Object[] external;
        readonly UdonRuntime owner;
        readonly Dictionary<OdinNode, object> values = new Dictionary<OdinNode, object>();
        readonly HashSet<OdinNode> resolving = new HashSet<OdinNode>();
        public OdinValues(OdinDocument document, UnityEngine.Object[] external, UdonRuntime owner)
        { this.document = document; this.external = external; this.owner = owner; }

        public object Read(OdinNode n)
        {
            if (n == null || n.IsNull) return null;
            if (n.IsExternal)
            {
                int index = Convert.ToInt32(n.scalar);
                if (index < 0 || external == null || index >= external.Length) throw new InvalidDataException("External reference out of range");
                var value = external[index];
                if (value is VRC.Udon.UdonBehaviour source) return source.RuntimeBridge as UdonRuntime;
                return value;
            }
            if (n.IsInternal)
            {
                int index = Convert.ToInt32(n.scalar);
                if (!document.references.TryGetValue(index, out var target)) throw new InvalidDataException("Unresolved internal reference");
                return Read(target);
            }
            if (values.TryGetValue(n, out var existing)) return existing;
            if (!resolving.Add(n)) throw new InvalidDataException("Unsupported cyclic value graph");
            object result;
            try
            {
                string name = n.typeName?.Split(',')[0];
                if (!n.IsReference && !n.IsStruct && n.tag != 6 && n.tag != 8)
                    result = IsDeclaredFogMode(n) ? (object)Convert.ToInt32(n.scalar) : n.scalar;
                else if (name == "System.RuntimeType")
                {
                    string target = Read(n.First) as string;
                    result = RuntimeTypes.Resolve(target) ?? throw new InvalidDataException("Type outside runtime allowlist: " + target);
                }
                else if (name == "VRC.Udon.Common.UdonGameObjectComponentHeapReference")
                {
                    Type type = Read(n.First) as Type;
                    if (owner == null) throw new InvalidDataException("Heap self reference without owner");
                    if (type == typeof(UdonRuntime)) result = owner;
                    else if (type == typeof(GameObject)) result = owner.gameObject;
                    else result = type == null ? null : owner.GetComponent(type);
                }
                else if (name != null && name.EndsWith("[]", StringComparison.Ordinal)) result = ReadArray(n, RuntimeTypes.Resolve(name));
                else if (n.tag == 6) result = ReadArray(n, typeof(object[]));
                else if (n.tag == 8) result = n.raw;
                else if (n.typeName != null && n.typeName.StartsWith("System.Runtime.CompilerServices.StrongBox", StringComparison.Ordinal)) result = Read(n.Named("Value"));
                else if (name == "UnityEngine.Vector2") result = new Vector2(Float(n, 0), Float(n, 1));
                else if (name == "UnityEngine.Vector3") result = new Vector3(Float(n, 0), Float(n, 1), Float(n, 2));
                else if (name == "UnityEngine.Vector4") result = new Vector4(Float(n, 0), Float(n, 1), Float(n, 2), Float(n, 3));
                else if (name == "UnityEngine.Quaternion") result = new Quaternion(Float(n, 0), Float(n, 1), Float(n, 2), Float(n, 3));
                else if (name == "UnityEngine.Color") result = new Color(Float(n, 0), Float(n, 1), Float(n, 2), Float(n, 3));
                else if (name == "VRC.SDKBase.VRCPlayerApi+TrackingData") result = new TrackingData { position = (Vector3)Read(n.children[0]), rotation = (Quaternion)Read(n.children[1]) };
                else if (name == "UnityEngine.LayerMask") result = Convert.ToInt32(Read(n.First));
                else if (RuntimeTypes.Resolve(name)?.IsEnum == true || RuntimeTypes.Resolve(name) == typeof(int)) result = Convert.ToInt32(Read(n.First));
                else if (name == "UnityEngine.RaycastHit") result = default(RaycastHit);
                else if (name == "UnityEngine.AnimatorStateInfo" && n.children.Count == 0) result = default(AnimatorStateInfo);
                else if (name == "VRC.Udon.Common.UdonInputEventArgs") result = new InputEventArgs
                { eventType = Convert.ToInt32(Read(n.Named("eventType"))), boolValue = Convert.ToBoolean(Read(n.Named("boolValue"))),
                    floatValue = Convert.ToSingle(Read(n.Named("floatValue"))), handType = Convert.ToInt32(Read(n.Named("handType"))) };
                else throw new InvalidDataException("Unsupported serialized value: " + n.typeName + " tag=" + n.tag);
                values[n] = result;
                return result;
            }
            finally { resolving.Remove(n); }
        }
        bool IsDeclaredFogMode(OdinNode n)
        {
            // Odin writes enum values as UInt64 even for an Int32-backed enum.
            // Keep the wire tree unchanged, but use the same Int32 representation
            // as the RenderSettings extern. Do not relax general Object.Equals
            // or convert unrelated UInt64 counters/constants.
            if (n.name != "Value" || n.parent < 0) return false;
            var parent = document.nodes[n.parent];
            if (parent.typeName?.StartsWith("System.Runtime.CompilerServices.StrongBox`1[[UnityEngine.FogMode,", StringComparison.Ordinal) == true)
                return true;
            if (parent.tag != 6 || parent.parent < 0) return false;
            return document.nodes[parent.parent].typeName?.StartsWith(
                "VRC.Udon.Common.UdonVariable`1[[UnityEngine.FogMode,", StringComparison.Ordinal) == true;
        }
        float Float(OdinNode n, int index) => Convert.ToSingle(Read(n.children[index]));
        Array ReadArray(OdinNode n, Type arrayType)
        {
            if (arrayType == null || !arrayType.IsArray) throw new InvalidDataException("Unsupported array type: " + n.typeName);
            var payload = n.tag == 6 || n.tag == 8 ? n : n.First;
            if (payload == null) throw new InvalidDataException("Missing array payload");
            Type element = arrayType.GetElementType();
            Array array = Array.CreateInstance(element, checked((int)payload.count));
            values[n] = array; // Preserve aliases, including an array referencing itself.
            if (payload.tag == 8)
            {
                if (!element.IsPrimitive || Buffer.ByteLength(array) != payload.raw.Length)
                    throw new InvalidDataException("Primitive array element size mismatch");
                Buffer.BlockCopy(payload.raw, 0, array, 0, payload.raw.Length);
            }
            else
                foreach (var child in payload.children.Where(c => c.arrayIndex >= 0))
                {
                    object value = Read(child);
                    if (value != null && element.IsEnum) value = Enum.ToObject(element, value);
                    array.SetValue(value, child.arrayIndex);
                }
            return array;
        }
    }
    public sealed class InputEventArgs { public int eventType, handType; public bool boolValue; public float floatValue; }
}
