using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static LocalVrcw.Udon.UdonExternImpl;

namespace LocalVrcw.Udon
{
    public static class UdonExterns
    {
        public static int unimplemented, pickupExternCalls;
        public static readonly HashSet<string> unimplementedSigs = new HashSet<string>();
        static readonly Dictionary<string, Abi> signatures = new Dictionary<string, Abi>();
        public sealed class Abi { public int arguments, instance; public bool returns, writeback; public int Count => arguments + instance + (returns ? 1 : 0); }
        public static Abi Signature(string signature)
        {
            if (signatures.TryGetValue(signature, out var cached)) return cached;
            string[] parts = signature.Split(new[] { "__" }, StringSplitOptions.None);
            if (parts.Length < 3 || parts.Length > 4) throw new System.IO.InvalidDataException("Malformed extern ABI " + signature);
            string cls = parts[0].TrimEnd('.'), method = parts[1];
            string result = parts[parts.Length - 1];
            int arguments = parts.Length == 4 && parts[2].Length > 0 ? parts[2].Split('_').Length : 0;
            if (cls == "VRCSDKBaseVRCPlayerApi" && method == "GetPickupInHand") arguments = 1;
            bool stat = method.StartsWith("op_", StringComparison.Ordinal) || method == "ctor" || cls.EndsWith("Networking") ||
                cls.EndsWith("Utilities") || cls == "VRCEconomyStore" || cls.EndsWith("Serialization") || cls.EndsWith("EventTiming") ||
                cls == "UnityEngineTime" || cls == "UnityEngineMathf" || cls == "UnityEngineRandom" || cls == "UnityEngineDebug" ||
                cls == "UnityEnginePhysics" || cls == "UnityEngineRenderSettings" || cls == "UnityEngineResources" ||
                cls == "UnityEngineApplication" || cls == "UnityEngineScreen" || cls == "UnityEngineInput" || cls == "UnityEnginePlayerPrefs" ||
                cls == "SystemConvert" || cls == "SystemMath" || method == "Parse" ||
                (cls == "SystemString" && (method == "IsNullOrEmpty" || method == "Concat")) ||
                (cls == "UnityEngineObject" && (method == "Destroy" || method == "Instantiate")) ||
                (cls == "VRCSDKBaseVRCPlayerApi" && (method == "GetPlayers" || method == "GetPlayerCount" || method == "GetPlayerById")) ||
                (cls == "SystemArray" && method == "IndexOf");
            Type type = RuntimeTypes.Encoded(cls);
            if (type != null && !method.StartsWith("op_", StringComparison.Ordinal) && method != "ctor")
            {
                if (method.StartsWith("get_") || method.StartsWith("set_"))
                {
                    string name = method.Substring(4);
                    var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                    var accessor = method.StartsWith("get_") ? property?.GetGetMethod() : property?.GetSetMethod();
                    var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                    if (accessor != null) stat = accessor.IsStatic;
                    else if (field != null) stat = field.IsStatic;
                }
                else
                {
                    var candidates = type.GetMethods().Where(m => m.Name == method && m.GetParameters().Length == arguments).ToArray();
                    if (candidates.Length > 0 && candidates.All(m => m.IsStatic)) stat = true;
                }
            }
            var abi = new Abi { arguments = arguments, instance = stat ? 0 : 1, returns = result != "SystemVoid" };
            if (result == "T" || result == "TArray") abi.arguments++; // The ABI carries the generic System.Type argument.
            abi.writeback = !stat && (cls == "UnityEngineVector3" || cls == "UnityEngineVector2" ||
                cls == "UnityEngineVector4" || cls == "UnityEngineColor") && method.StartsWith("set_");
            signatures.Add(signature, abi);
            return abi;
        }

        public static bool TryExtern(UdonVM vm, string sig, out uint? newPc, uint pc)
        {
            newPc = null; var abi = Signature(sig);
            int count = abi.Count;
            if (sig.StartsWith("VRCUdonCommonInterfacesIUdonEventReceiver.__SendCustomEvent", StringComparison.Ordinal))
            {
                uint[] stack = vm.stack.ToArray();
                bool explicitReceiver = stack.Length > abi.arguments && vm.Get(stack[abi.arguments]) is UdonRuntime;
                count = abi.arguments + (explicitReceiver ? 1 : 0);
            }
            if (vm.stack.Count < count) { vm.runtime.LogError("EXTERN underflow " + sig + " want=" + count + " have=" + vm.stack.Count); return false; }
            uint[] addresses = new uint[count]; object[] args = new object[count];
            for (int i = count - 1; i >= 0; i--) { addresses[i] = vm.stack.Pop(); args[i] = vm.Get(addresses[i]); }
            object value = Invoke(sig, vm.runtime, vm, args);
            if (value is UdonExternImpl.PhysicsQueryResult query)
            { vm.Set(addresses[query.hitArgumentIndex], query.hit); value = query.didHit; }
            if (abi.writeback) vm.Set(addresses[0], value);
            else if (abi.returns) vm.Set(addresses[count - 1], value);
            return true;
        }
        static object Invoke(string sig, UdonRuntime rt, UdonVM vm, object[] args)
        {
            // ---- operator / numeric ----
            if (sig.StartsWith("SystemInt32.__op_")) return OpInt(sig, args);
            if (sig.StartsWith("SystemSingle.__op_")) return OpFloat(sig, args);
            if (sig.StartsWith("SystemDouble.__op_")) return OpDouble(sig, args);
            if (sig.StartsWith("SystemInt64.__op_")) return OpLong(sig, args);
            if (sig.StartsWith("SystemBoolean.__op_")) return OpBool(sig, args);
            if (sig.StartsWith("SystemString.__op_")) return OpString(sig, args);
            if (sig.StartsWith("SystemObject.__op_")) return OpObject(sig, args);
            if (sig == "SystemObject.__Equals__SystemObject__SystemBoolean") return OpObject(sig, args);
            if (sig.StartsWith("UnityEngineObject.__op_")) return OpUnityObject(sig, args);
            if (sig.StartsWith("UnityEngineVector3.__op_")) return OpVector3(sig, args);
            if (sig.StartsWith("UnityEngineQuaternion.__op_")) return OpQuaternion(sig, args);
            if (sig.StartsWith("UnityEngineColor.__op_")) return OpColor(sig, args);

            // ---- array constructors: XArray.__ctor__(length) -> new X[len] ----
            if (sig.Contains("Array.__ctor__"))
            {
                int len = args.Length > 0 ? UdonExternImpl.ToInt(args[0]) : 0;
                if (len < 0 || len > 65536) throw new System.IO.InvalidDataException("Array allocation limit");
                string encoded = sig.Substring(0, sig.IndexOf("Array.__ctor__", StringComparison.Ordinal));
                Type element = RuntimeTypes.Encoded(encoded);
                if (encoded == "VRCSDKBaseVRCPlayerApi") element = typeof(LocalPlayer);
                return Array.CreateInstance(element ?? typeof(object), len);
            }

            // ---- common type helpers ----
            string s = sig;
            if (s.StartsWith("SystemInt32.")) return SysInt(s, rt, args);
            if (s.StartsWith("SystemSingle.")) return SysFloat(s, rt, args);
            if (s.StartsWith("SystemString.")) return SysString(s, rt, args);
            if (s.StartsWith("SystemBoolean.")) return SysBool(s, rt, args);
            if (s.StartsWith("SystemArray.")) return SysArray(s, rt, args);
            if (s.StartsWith("SystemObjectArray.")) return SysArray(s, rt, args);
            if (s.StartsWith("SystemStringArray.")) return SysArray(s, rt, args);
            if (s.StartsWith("SystemSingleArray.")) return SysArray(s, rt, args);
            if (s.StartsWith("SystemInt32Array.")) return SysArray(s, rt, args);
            if (s.StartsWith("SystemBooleanArray.")) return SysArray(s, rt, args);
            if (s.StartsWith("UnityEngineVector3Array.")) return SysArray(s, rt, args);
            // Udon emits concrete Unity array signatures (for example
            // UnityEngineTexture2DArray.__Get) rather than SystemObjectArray
            // for serialized Texture2D[] fields. They have the same Get/Set
            // semantics; routing them here preserves the source array element
            // instead of returning null to Material.SetTexture.
            if (s.StartsWith("UnityEngineTexture2DArray.")) return SysArray(s, rt, args);
            if (s.StartsWith("VRCSDKBaseVRCPlayerApiArray.")) return SysArray(s, rt, args);
            if (s.StartsWith("SystemType.")) return SysType(s, rt, args);
            if (s.StartsWith("System.Math")) return SysMath(s, rt, args);
            if (s.StartsWith("SystemConvert.")) return Convert2(s, rt, args);

            if (s.StartsWith("UnityEngineGameObject.")) return UGo(s, rt, args);
            if (s.StartsWith("UnityEngineTransform.")) return UTransform(s, rt, args);
            if (s.StartsWith("UnityEngineComponent.")) return UComponent(s, rt, args);
            if (s.StartsWith("UnityEngineBehaviour.")) return UBehaviour(s, rt, args);
            if (s.StartsWith("UnityEngineAnimator.")) return UAnimator(s, rt, args);
            if (s.StartsWith("UnityEngineAnimatorStateInfo.")) return UAnimatorStateInfo(s, rt, args);
            if (s.StartsWith("UnityEngineAudioSource.")) return UAudio(s, rt, args);
            if (s.StartsWith("UnityEngineRenderer.")) return URenderer(s, rt, args);
            if (s.StartsWith("UnityEngineTexture.") || s.StartsWith("UnityEngineTexture2D.")) return UTexture(s, rt, args);
            if (s.StartsWith("UnityEngineMaterial.")) return UMaterial(s, rt, args);
            if (s.StartsWith("UnityEngineLight.")) return ULight(s, rt, args);
            if (s.StartsWith("UnityEngineCollider.") || s.StartsWith("UnityEngineBoxCollider."))
                return UCollider(s, rt, args);
            if (s.StartsWith("UnityEngineRigidbody.")) return URigidbody(s, rt, args);
            if (s.StartsWith("UnityEngineCamera.")) return UCamera(s, rt, args);
            if (s.StartsWith("UnityEngineVector3.")) return UVector3(s, rt, args);
            if (s.StartsWith("UnityEngineQuaternion.")) return UQuaternion(s, rt, args);
            if (s.StartsWith("UnityEngineColor.")) return UColor(s, rt, args);
            if (s.StartsWith("UnityEngineVector2.")) return UVector2(s, rt, args);
            if (s.StartsWith("UnityEngineVector4.")) return UVector4(s, rt, args);
            if (s.StartsWith("UnityEngineLayerMask.")) return ULayerMask(s, rt, args);
            if (s.StartsWith("UnityEngineTime.")) return UTime(s, rt, args);
            if (s.StartsWith("UnityEngineMathf.")) return UMathf(s, rt, args);
            if (s.StartsWith("UnityEngineRandom.")) return URandom(s, rt, args);
            if (s.StartsWith("UnityEngineDebug.")) return UDebug(s, rt, args);
            if (s.StartsWith("UnityEngineObject.")) return UObject(s, rt, args);
            if (s.StartsWith("UnityEngineResources.")) return UResources(s, rt, args);
            if (s.StartsWith("UnityEnginePhysics.")) return UPhysics(s, rt, args);
            if (s.StartsWith("UnityEngineParticleSystem.")) return UParticles(s, rt, args);
            if (s.StartsWith("UnityEngineAnimation.")) return UAnimation(s, rt, args);
            if (s.StartsWith("UnityEngineRectTransform.")) return URectTransform(s, rt, args);
            if (s.StartsWith("UnityEngineCanvas.")) return UCanvas(s, rt, args);
            if (s.StartsWith("UnityEngineRenderSettings.")) return URenderSettings(s, rt, args);
            if (s.StartsWith("UnityEngineRaycastHit.")) return URaycastHit(s, rt, args);
            if (s.StartsWith("UnityEngineRect.")) return null;
            if (s.StartsWith("UnityEngineApplication.")) return UApplication(s, rt, args);
            if (s.StartsWith("UnityEngineScreen.")) return UScreen(s, rt, args);
            if (s.StartsWith("UnityEngineInput.")) return UInput(s, rt, args);
            if (s.StartsWith("UnityEnginePlayerPrefs.")) return UPlayerPrefs(s, rt, args);

            if (s.StartsWith("UnityEngineUIToggle.")) return UToggle(s, rt, args);

            // ---- VRC ----
            if (s.StartsWith("VRCUdonCommonInterfacesIUdonEventReceiver.")) return VUdonEvent(s, rt, args);
            if (s.StartsWith("VRCUdonUdonBehaviour.")) return VUdonBehaviour(s, rt, args);
            if (s.StartsWith("VRCUdonCommonEnumsEventTiming.")) return VEventTiming(s, rt, args);
            if (s.StartsWith("VRCEconomyStore.")) return VEconomyStore(s, rt, args);
            if (s.StartsWith("VRCSDKBaseNetworking.")) return VNetworking(s, rt, args);
            if (s.StartsWith("VRCSDKBaseVRCPlayerApi.")) return VPlayerApi(s, rt, args);
            if (s.StartsWith("VRCSDKBaseVRCPlayerApiTrackingData.")) return VTrackingData(s, rt, args);
            if (s.StartsWith("VRCSDKBaseUtilities.")) return VUtilities(s, rt, args);
            if (s.StartsWith("VRCSDKBaseVRCInput.")) return VInput(s, rt, args);
            if (s.StartsWith("VRCSDK3ComponentsVRCPickup.")) return VPickup(s, rt, args);
            if (s.StartsWith("VRCSDKBaseVRCCustomAction.")) return VCustomAction(s, rt, args);
            if (s.StartsWith("VRCUdonSerialization.")) return VSerialization(s, rt, args);
            if (s.StartsWith("VRCUdonCommonSerializationResult.")) return VSerializationResult(s, rt, args);

            unimplemented++;
            if (unimplementedSigs.Count < 200) unimplementedSigs.Add(s);
            return null;
        }


    }
}

