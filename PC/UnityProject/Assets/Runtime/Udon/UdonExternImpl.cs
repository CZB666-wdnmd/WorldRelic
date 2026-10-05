using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalVrcw.Udon
{
    /// <summary>Udon EXTERN implementations (operators + common Unity/VRChat APIs).</summary>
    public static partial class UdonExternImpl
    {
        // ================= helpers =================
        // Odin stores authored SDK enum constants as UInt64, not boxed Int32.
        // Keep explicit primitive-only numeric conversion; never silently turn
        // a valid hand/tracking enum (1 or 2) into zero or activate CLR types.
        static int AInt(object o) => o is int i ? i : o is float f ? (int)f :
            o is uint u ? unchecked((int)u) : o is ulong ul ? unchecked((int)ul) :
            o is long l ? unchecked((int)l) : o is short s ? s : o is ushort us ? us :
            o is byte by ? by : o is sbyte sb ? sb : o is double d ? (int)d : o is bool b ? (b ? 1 : 0) : 0;
        public static int ToInt(object o) => AInt(o);
        static long ALong(object o) => o is long l ? l : AInt(o);
        static float AFloat(object o) => o is float f ? f : o is int i ? i : o is uint u ? u : o is double d ? (float)d : 0f;
        static double ADouble(object o) => o is double d ? d : AFloat(o);
        static bool ABool(object o) => o is bool b ? b : o is int i ? i != 0 : o != null;
        static string AStr(object o) => o as string ?? (o == null ? null : o.ToString());
        static Vector3 AV3(object o) => o is Vector3 v ? v : Vector3.zero;
        static Quaternion AQuat(object o) => o is Quaternion q ? q : Quaternion.identity;
        static Color AColor(object o) => o is Color c ? c : Color.white;
        static Vector2 AV2(object o) => o is Vector2 v ? v : Vector2.zero;
        static Vector4 AV4(object o) => o is Vector4 v ? v : Vector4.zero;
        static Component AComp(object o) => o as Component;
        // Scene migration stores component references as their owning GameObject.
        // Resolve the concrete component requested by each Unity extern.
        static T AComponent<T>(object o) where T : Component
        {
            if (o is T direct) return direct;
            GameObject go = o as GameObject;
            if (go != null) return go.GetComponent<T>();
            Component c = o as Component;
            return c == null ? null : c.GetComponent<T>();
        }
        static GameObject AGo(object o) => o as GameObject ?? (o as Component)?.gameObject;
        static UnityEngine.Object AObj(object o) => o as UnityEngine.Object;

        // ================= operators =================
        internal static object OpInt(string sig, object[] a)
        {
            int x = AInt(a[0]), y = a.Length > 1 ? AInt(a[1]) : 0;
            if (sig.Contains("__op_Addition")) return x + y;
            if (sig.Contains("__op_Subtraction")) return x - y;
            if (sig.Contains("__op_Multiplication")) return x * y;
            if (sig.Contains("__op_Division")) return x / y;
            if (sig.Contains("__op_Equality")) return x == y;
            if (sig.Contains("__op_Inequality")) return x != y;
            if (sig.Contains("__op_LessThanOrEqual")) return x <= y;
            if (sig.Contains("__op_GreaterThanOrEqual")) return x >= y;
            if (sig.Contains("__op_LessThan")) return x < y;
            if (sig.Contains("__op_GreaterThan")) return x > y;
            if (sig.Contains("__op_UnaryNegation")) return -x;
            if (sig.Contains("__op_Modulus") || sig.Contains("__op_Remainder")) return x % y;
            // Udon exports C# integral bit operators under the historical
            // "Logical*" names. These drive source pickup effect masks (left
            // bit 1 / right bit 2), so returning x here makes held-use a
            // silent no-op.
            if (sig.Contains("__op_LogicalAnd")) return x & y;
            if (sig.Contains("__op_LogicalOr")) return x | y;
            if (sig.Contains("__op_LogicalXor")) return x ^ y;
            if (sig.Contains("__op_LeftShift")) return x << y;
            if (sig.Contains("__op_RightShift")) return x >> y;
            return x;
        }

        internal static object OpFloat(string sig, object[] a)
        {
            float x = AFloat(a[0]), y = a.Length > 1 ? AFloat(a[1]) : 0;
            if (sig.Contains("__op_Addition")) return x + y;
            if (sig.Contains("__op_Subtraction")) return x - y;
            if (sig.Contains("__op_Multiplication")) return x * y;
            if (sig.Contains("__op_Division")) return x / y;
            if (sig.Contains("__op_Equality")) return x == y;
            if (sig.Contains("__op_Inequality")) return x != y;
            if (sig.Contains("__op_LessThanOrEqual")) return x <= y;
            if (sig.Contains("__op_GreaterThanOrEqual")) return x >= y;
            if (sig.Contains("__op_LessThan")) return x < y;
            if (sig.Contains("__op_GreaterThan")) return x > y;
            if (sig.Contains("__op_UnaryNegation")) return -x;
            if (sig.Contains("__op_Modulus")) return x % y;
            return x;
        }

        internal static object OpDouble(string sig, object[] a)
        {
            double x = ADouble(a[0]), y = ADouble(a[1]);
            if (sig.Contains("__op_Addition")) return x + y;
            if (sig.Contains("__op_Subtraction")) return x - y;
            if (sig.Contains("__op_Multiplication")) return x * y;
            if (sig.Contains("__op_Division")) return x / y;
            if (sig.Contains("__op_Equality")) return x == y;
            if (sig.Contains("__op_Inequality")) return x != y;
            if (sig.Contains("__op_GreaterThanOrEqual")) return x >= y;
            if (sig.Contains("__op_LessThanOrEqual")) return x <= y;
            if (sig.Contains("__op_GreaterThan")) return x > y;
            if (sig.Contains("__op_LessThan")) return x < y;
            return x;
        }

        internal static object OpLong(string sig, object[] a)
        {
            long x = ALong(a[0]), y = ALong(a[1]);
            if (sig.Contains("__op_Addition")) return x + y;
            if (sig.Contains("__op_Subtraction")) return x - y;
            if (sig.Contains("__op_Multiplication")) return x * y;
            if (sig.Contains("__op_Division")) return x / y;
            if (sig.Contains("__op_Equality")) return x == y;
            if (sig.Contains("__op_Inequality")) return x != y;
            return x;
        }

        internal static object OpBool(string sig, object[] a)
        {
            bool x = ABool(a[0]);
            if (sig.Contains("__op_UnaryNegation") || sig.Contains("__op_LogicalNot")) return !x;
            if (a.Length > 1)
            {
                bool y = ABool(a[1]);
                if (sig.Contains("__op_Equality")) return x == y;
                if (sig.Contains("__op_Inequality")) return x != y;
            }
            return x;
        }

        internal static object OpString(string sig, object[] a)
        {
            string x = AStr(a[0]);
            if (sig.Contains("__op_Addition")) return x + AStr(a[1]);
            if (sig.Contains("__op_Equality")) return x == AStr(a[1]);
            if (sig.Contains("__op_Inequality")) return x != AStr(a[1]);
            return x;
        }

        internal static object OpObject(string sig, object[] a)
        {
            bool equal = ReferenceEquals(a[0], a[1]) || (a[0] != null && a[0].Equals(a[1]));
            return sig.Contains("__op_Inequality") ? !equal : equal;
        }
        internal static object OpUnityObject(string sig, object[] a)
        {
            bool equal = AObj(a[0]) == AObj(a[1]);
            return sig.Contains("__op_Inequality") ? !equal : equal;
        }

        internal static object OpVector3(string sig, object[] a)
        {
            Vector3 x = AV3(a[0]);
            if (a.Length > 1)
            {
                Vector3 y = AV3(a[1]);
                if (sig.Contains("__op_Addition")) return x + y;
                if (sig.Contains("__op_Subtraction")) return x - y;
                if (sig.Contains("__op_Equality")) return x == y;
                if (sig.Contains("__op_Inequality")) return x != y;
            }
            // Unity value-type operators are exported as __op_Multiply
            // (whereas primitive numeric operators use __op_Multiplication).
            // Accept both spellings. Returning the unscaled vector here made
            // the bed arrival helper add full right/forward unit vectors for
            // every scale operation and placed the player inside the closet.
            if (sig.Contains("__op_Multiply") || sig.Contains("__op_Multiplication"))
            {
                if (a[1] is float f || a[1] is int) return x * AFloat(a[1]);
                return Vector3.Scale(x, AV3(a[1]));
            }
            if (sig.Contains("__op_Division")) return x / AFloat(a[1]);
            if (sig.Contains("__op_UnaryNegation")) return -x;
            return x;
        }

        internal static object OpQuaternion(string sig, object[] a)
        {
            Quaternion x = AQuat(a[0]);
            if (a.Length > 1)
            {
                Quaternion y = AQuat(a[1]);
                if (sig.Contains("__op_Multiply") || sig.Contains("__op_Multiplication"))
                    return a[1] is Vector3 ? x * AV3(a[1]) : x * y;
                if (sig.Contains("__op_Equality")) return x == y;
            }
            return x;
        }

        internal static object OpColor(string sig, object[] a)
        {
            Color x = AColor(a[0]);
            if (a.Length > 1)
            {
                Color y = AColor(a[1]);
                if (sig.Contains("__op_Addition")) return x + y;
                if (sig.Contains("__op_Subtraction")) return x - y;
                if (sig.Contains("__op_Multiply") || sig.Contains("__op_Multiplication"))
                {
                    if (a[1] is float || a[1] is int) return x * AFloat(a[1]);
                    return x * y;
                }
            }
            return x;
        }
    }
}

