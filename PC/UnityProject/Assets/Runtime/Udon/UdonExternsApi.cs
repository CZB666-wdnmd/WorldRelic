using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalVrcw.Udon
{
    /// <summary>UnityEngine.* Udon extern implementations.</summary>
    public static partial class UdonExternImpl
    {
        internal static object UToggle(string sig, UdonRuntime rt, object[] a)
        {
            var toggle = a.Length > 0 ? AComponent<UnityEngine.UI.Toggle>(a[0]) : null;
            if (sig.Contains("__get_isOn")) return toggle != null && toggle.isOn;
            if (sig.Contains("__set_isOn") && toggle != null && a.Length > 1)
                toggle.isOn = ABool(a[1]);
            return null;
        }
        static readonly HashSet<string> sysArrayBoundsWarnings = new HashSet<string>();

        internal static object SysInt(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("__ToString")) return AInt(a[0]).ToString();
            if (sig.Contains("__Parse")) return int.Parse(AStr(a[0]));
            return AInt(a.Length > 0 ? a[0] : 0);
        }
        internal static object SysFloat(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("__ToString")) return AFloat(a[0]).ToString();
            if (sig.Contains("__Parse")) return float.Parse(AStr(a[0]), System.Globalization.CultureInfo.InvariantCulture);
            return AFloat(a.Length > 0 ? a[0] : 0);
        }
        internal static object SysString(string sig, UdonRuntime rt, object[] a)
        {
            string s = AStr(a.Length > 0 ? a[0] : null);
            if (sig.Contains("__IsNullOrEmpty")) return string.IsNullOrEmpty(s);
            if (sig.Contains("__get_Length")) return s == null ? 0 : s.Length;
            if (sig.Contains("__Contains")) return s != null && s.Contains(AStr(a[1]));
            if (sig.Contains("__StartsWith")) return s != null && s.StartsWith(AStr(a[1]));
            if (sig.Contains("__EndsWith")) return s != null && s.EndsWith(AStr(a[1]));
            if (sig.Contains("__IndexOf")) return s == null ? -1 : s.IndexOf(AStr(a[1]), StringComparison.Ordinal);
            if (sig.Contains("__Substring")) return s.Substring(AInt(a[1]));
            if (sig.Contains("__Replace")) return s.Replace(AStr(a[1]), AStr(a[2]));
            if (sig.Contains("__ToLower")) return s == null ? null : s.ToLower();
            if (sig.Contains("__ToUpper")) return s == null ? null : s.ToUpper();
            if (sig.Contains("__Trim")) return s == null ? null : s.Trim();
            if (sig.Contains("__Concat")) return AStr(a[0]) + AStr(a[1]);
            return s;
        }
        internal static object SysBool(string sig, UdonRuntime rt, object[] a) { return ABool(a.Length > 0 ? a[0] : false); }
        internal static object SysArray(string sig, UdonRuntime rt, object[] a)
        {
            Array arr = a[0] as Array;
            if (sig.Contains("__get_Length") || sig.Contains("get_Length")) return arr == null ? 0 : arr.Length;
            // MOSUpdateManager uses IndexOf to distinguish a new
            // ManualObjectSync registration from an existing one. Returning
            // the array object here (the old generic fall-through) coerces
            // to zero and incorrectly makes an empty list look populated.
            if (sig.Contains("__IndexOf__"))
            {
                if (arr == null) return -1;
                object value = a.Length > 1 ? a[1] : null;
                for (int index = arr.GetLowerBound(0); index <= arr.GetUpperBound(0); index++)
                    if (Equals(arr.GetValue(index), value)) return index;
                return -1;
            }
            if (sig.Contains("__CopyTo__"))
            {
                Array target = a.Length > 1 ? a[1] as Array : null;
                int targetIndex = a.Length > 2 ? AInt(a[2]) : 0;
                if (arr != null && target != null && targetIndex >= target.GetLowerBound(0) &&
                    targetIndex + arr.Length <= target.GetLowerBound(0) + target.Length)
                    Array.Copy(arr, arr.GetLowerBound(0), target, targetIndex, arr.Length);
                return null;
            }
            if (sig.Contains("__Get__"))
            {
                if (arr == null) return null;
                int index = AInt(a[1]);
                if (index < arr.GetLowerBound(0) || index > arr.GetUpperBound(0))
                {
                    string owner = rt == null ? "<null>" : rt.gameObject.name + " [" + rt.programHash + "]";
                    string warningKey = owner + "|" + sig + "|" + index + "|" + arr.Length;
                    if (sysArrayBoundsWarnings.Add(warningKey))
                        Debug.LogWarning("[Udon] array get out of range: " + owner + " " + sig + " index=" + index + " length=" + arr.Length, rt);
                    return null;
                }
                return arr.GetValue(index);
            }
            if (sig.Contains("__Set__"))
            {
                if (arr != null)
                {
                    int index = AInt(a[1]);
                    if (index >= arr.GetLowerBound(0) && index <= arr.GetUpperBound(0))
                        arr.SetValue(a[2], index);
                }
                return null;
            }
            return arr;
        }
        internal static object SysType(string sig, UdonRuntime rt, object[] a) { return null; }
        internal static object SysMath(string sig, UdonRuntime rt, object[] a) { return 0; }

        // ---------- UnityEngine.GameObject ----------
        internal static Action<UdonRuntime, GameObject, bool> ActiveObserverForVerification;
        internal static object UGo(string sig, UdonRuntime rt, object[] a)
        {
            GameObject go = AGo(a[0]);
            if (go == null) return null;
            if (sig.Contains("__get_activeSelf")) return go.activeSelf;
            if (sig.Contains("__get_activeInHierarchy")) return go.activeInHierarchy;
            if (sig.Contains("__SetActive")) { bool active = ABool(a[1]); ActiveObserverForVerification?.Invoke(rt, go, active); go.SetActive(active); return null; }
            if (sig.Contains("__get_transform")) return go.transform;
            if (sig.Contains("__get_name")) return go.name;
            if (sig.Contains("__set_name")) { go.name = AStr(a[1]); return null; }
            if (sig.Contains("__get_layer")) return go.layer;
            if (sig.Contains("__set_layer")) { go.layer = AInt(a[1]); return null; }
            if (sig.Contains("__CompareTag")) return go.CompareTag(AStr(a[1]));
            if (sig.Contains("__GetComponent"))
            {
                // Generic Udon GetComponent<T> calls still push the resolved
                // System.Type constant when the source program contains one.
                // Use it when present; fall back only for an incomplete type
                // export. (8fe's Rigidbody/VRCPickup/Collider setup relies on
                // this exact form.)
                Type requestedType = a.Length > 1 ? a[1] as Type : null;
                if (sig.Contains("SystemType") || requestedType != null)
                    return PickComponentByType(go, requestedType);
                return PickComponentT(go);
            }
            if (sig.Contains("__Destroy")) { UnityEngine.Object.Destroy(go); return null; }
            return null;
        }

        // UdonSharp's generic GetComponent<T> carries no usable type info in our
        // export; return the most useful component: a Renderer when present (the
        // dominant use is material access), otherwise the first non-structural one.
        static Component PickComponentT(GameObject go)
        {
            if (go == null) return null;
            var r = go.GetComponent<Renderer>();
            if (r != null) return r;
            foreach (var comp in go.GetComponents<Component>())
                if (!(comp is Transform) && !(comp is MeshFilter))
                    return comp;
            return go.GetComponent<Component>();
        }

        static Component PickComponentByType(GameObject go, Type type)
        {
            if (go == null) return null;
            // A null type is an incomplete extraction. Preserve the old
            // generic fallback in that case, but never let it override a
            // source-resolved System.Type token.
            return type != null ? go.GetComponent(type) : PickComponentT(go);
        }

        // ---------- UnityEngine.Transform ----------
        internal static object UTransform(string sig, UdonRuntime rt, object[] a)
        {
            Transform t = AComponent<Transform>(a[0]);
            if (t == null) return null;
            if (sig.Contains("__get_position")) return t.position;
            if (sig.Contains("__set_position")) { t.position = AV3(a[1]); return null; }
            if (sig.Contains("__get_localPosition")) return t.localPosition;
            if (sig.Contains("__set_localPosition")) { t.localPosition = AV3(a[1]); return null; }
            if (sig.Contains("__get_rotation")) return t.rotation;
            if (sig.Contains("__set_rotation")) { t.rotation = AQuat(a[1]); return null; }
            if (sig.Contains("__get_localRotation")) return t.localRotation;
            if (sig.Contains("__set_localRotation")) { t.localRotation = AQuat(a[1]); return null; }
            if (sig.Contains("__get_localScale")) return t.localScale;
            if (sig.Contains("__set_localScale")) { t.localScale = AV3(a[1]); return null; }
            if (sig.Contains("__get_parent")) return t.parent;
            if (sig.Contains("__set_parent")) { t.SetParent(AComponent<Transform>(a[1]), true); return null; }
            if (sig.Contains("__get_forward")) return t.forward;
            if (sig.Contains("__get_right")) return t.right;
            if (sig.Contains("__get_up")) return t.up;
            if (sig.Contains("__Translate")) { t.Translate(AV3(a[1])); return null; }
            if (sig.Contains("__Rotate")) { if (a[1] is Vector3) t.Rotate(AV3(a[1])); return null; }
            if (sig.Contains("__LookAt")) { t.LookAt(AV3(a[1])); return null; }
            if (sig.Contains("__get_childCount")) return t.childCount;
            if (sig.Contains("__GetChild")) return t.GetChild(AInt(a[1]));
            if (sig.Contains("__IsChildOf"))
            {
                Transform parent = a.Length > 1 ? AComponent<Transform>(a[1]) : null;
                return parent != null && t.IsChildOf(parent);
            }
            if (sig.Contains("__get_gameObject")) return t.gameObject;
            if (sig.Contains("__get_hasChanged")) return t.hasChanged;
            if (sig.Contains("__get_lossyScale")) return t.lossyScale;
            if (sig.Contains("__InverseTransformPoint")) return t.InverseTransformPoint(AV3(a[1]));
            if (sig.Contains("__TransformPoint")) return t.TransformPoint(AV3(a[1]));
            if (sig.Contains("__InverseTransformDirection")) return t.InverseTransformDirection(AV3(a[1]));
            if (sig.Contains("__TransformDirection")) return t.TransformDirection(AV3(a[1]));
            if (sig.Contains("__InverseTransformVector")) return t.InverseTransformVector(AV3(a[1]));
            if (sig.Contains("__TransformVector")) return t.TransformVector(AV3(a[1]));
            if (sig.Contains("__SetPositionAndRotation")) { t.SetPositionAndRotation(AV3(a[1]), AQuat(a[2])); return null; }
            return null;
        }

        // ---------- UnityEngine.Component / Behaviour ----------
        internal static object UComponent(string sig, UdonRuntime rt, object[] a)
        {
            Component c = AComp(a[0]);
            GameObject go = AGo(a[0]);
            if (c == null && go == null) return null;
            if (sig.Contains("__get_gameObject")) return go;
            if (sig.Contains("__get_transform")) return go == null ? null : go.transform;
            // Keep the plural form ahead of GetComponent: its signature is a
            // prefix match for the singular name. ElevatorButtonStateVisual
            // (027bc…) uses this exact API to enumerate the original Udon
            // behaviours on its own button and locate ElevatorButton by the
            // source __refl_typeid value.
            if (sig.Contains("__GetComponents"))
            {
                Type requestedType = a.Length > 1 ? a[1] as Type : null;
                return requestedType != null ? go.GetComponents(requestedType) : go.GetComponents<Component>();
            }
            if (sig.Contains("__GetComponent"))
            {
                Type requestedType = a.Length > 1 ? a[1] as Type : null;
                return sig.Contains("SystemType") || requestedType != null
                    ? PickComponentByType(go, requestedType)
                    : PickComponentT(go);
            }
            return null;
        }
        internal static object UBehaviour(string sig, UdonRuntime rt, object[] a)
        {
            Behaviour b = AComponent<Behaviour>(a[0]);
            if (b == null) return null;
            if (b is UdonRuntime bridge) return UdonExternImpl.VUdonEvent(sig, rt, a);
            if (sig.Contains("__get_enabled")) return b.enabled;
            if (sig.Contains("__set_enabled")) { b.enabled = ABool(a[1]); return null; }
            if (sig.Contains("__get_isActiveAndEnabled")) return b.isActiveAndEnabled;
            return null;
        }

        // Unity logs a warning (and still does no useful work) when an Udon
        // export addresses an Animator parameter that was stripped from the
        // reconstructed controller. Treat that case as a no-op, matching the
        // tolerant behaviour of the original network-side call.
        static bool HasAnimatorParameter(Animator an, string name, AnimatorControllerParameterType expected)
        {
            if (an == null || string.IsNullOrEmpty(name)) return false;
            var parameters = an.parameters;
            for (int i = 0; i < parameters.Length; i++)
                if (parameters[i].name == name && parameters[i].type == expected) return true;
            return false;
        }

        // ---------- UnityEngine.Animator ----------
        internal static object UAnimator(string sig, UdonRuntime rt, object[] a)
        {
            Animator an = AComponent<Animator>(a[0]);
            if (an == null) return null;
            if (sig.Contains("__SetTrigger"))
            {
                string name = AStr(a[1]);
                if (HasAnimatorParameter(an, name, AnimatorControllerParameterType.Trigger)) an.SetTrigger(name);
                return null;
            }
            if (sig.Contains("__ResetTrigger"))
            {
                string name = AStr(a[1]);
                if (HasAnimatorParameter(an, name, AnimatorControllerParameterType.Trigger)) an.ResetTrigger(name);
                return null;
            }
            if (sig.Contains("__SetBool"))
            {
                string name = AStr(a[1]);
                if (HasAnimatorParameter(an, name, AnimatorControllerParameterType.Bool)) an.SetBool(name, ABool(a[2]));
                return null;
            }
            if (sig.Contains("__GetBool"))
            {
                string name = AStr(a[1]);
                return HasAnimatorParameter(an, name, AnimatorControllerParameterType.Bool) && an.GetBool(name);
            }
            if (sig.Contains("__SetFloat"))
            {
                string name = AStr(a[1]);
                if (HasAnimatorParameter(an, name, AnimatorControllerParameterType.Float)) an.SetFloat(name, AFloat(a[2]));
                return null;
            }
            if (sig.Contains("__GetFloat"))
            {
                string name = AStr(a[1]);
                return HasAnimatorParameter(an, name, AnimatorControllerParameterType.Float) ? an.GetFloat(name) : 0f;
            }
            if (sig.Contains("__SetInteger"))
            {
                string name = AStr(a[1]);
                if (HasAnimatorParameter(an, name, AnimatorControllerParameterType.Int)) an.SetInteger(name, AInt(a[2]));
                return null;
            }
            if (sig.Contains("__GetInteger"))
            {
                string name = AStr(a[1]);
                return HasAnimatorParameter(an, name, AnimatorControllerParameterType.Int) ? an.GetInteger(name) : 0;
            }
            // ec421 (Uraraka's pickup) uses the full overloads below to
            // switch its two source animators between Idle and Float.  Do
            // not discard their layer/time arguments: that leaves the state
            // machine visually unchanged even though the Udon state bits
            // have changed.
            if (sig.Contains("__CrossFadeInFixedTime"))
            {
                an.CrossFadeInFixedTime(AStr(a[1]), AFloat(a[2]), AInt(a[3]), AFloat(a[4]));
                return null;
            }
            if (sig.Contains("__Play"))
            {
                an.Play(AStr(a[1]), a.Length > 2 ? AInt(a[2]) : -1, a.Length > 3 ? AFloat(a[3]) : float.NegativeInfinity);
                return null;
            }
            if (sig.Contains("__get_speed")) return an.speed;
            if (sig.Contains("__set_speed")) { an.speed = AFloat(a[1]); return null; }
            if (sig.Contains("__get_playbackTime")) return an.playbackTime;
            if (sig.Contains("__set_playbackTime")) { an.playbackTime = AFloat(a[1]); return null; }
            if (sig.Contains("__GetCurrentAnimatorStateInfo")) return an.GetCurrentAnimatorStateInfo(0);
            if (sig.Contains("__Rebind")) { an.Rebind(); return null; }
            return null;
        }

        // ---------- UnityEngine.AnimatorStateInfo ----------
        internal static object UAnimatorStateInfo(string sig, UdonRuntime rt, object[] a)
        {
            if (a.Length == 0 || !(a[0] is AnimatorStateInfo info)) return null;
            if (sig.Contains("__get_fullPathHash")) return info.fullPathHash;
            return null;
        }

        // ---------- UnityEngine.AudioSource ----------
        internal static Action<UdonRuntime, AudioSource, string> AudioObserverForVerification;
        internal static object UAudio(string sig, UdonRuntime rt, object[] a)
        {
            AudioSource aso = AComponent<AudioSource>(a[0]);
            if (aso == null) return null;
            if (sig.Contains("__PlayOneShot")) { aso.PlayOneShot(a[1] as AudioClip); if (RuntimeTestGate.Enabled) AudioObserverForVerification?.Invoke(rt, aso, "PlayOneShot"); return null; }
            if (sig.Contains("__Play")) { aso.Play(); if (RuntimeTestGate.Enabled) AudioObserverForVerification?.Invoke(rt, aso, "Play"); return null; }
            if (sig.Contains("__Stop")) { aso.Stop(); if (RuntimeTestGate.Enabled) AudioObserverForVerification?.Invoke(rt, aso, "Stop"); return null; }
            if (sig.Contains("__Pause")) { aso.Pause(); return null; }
            if (sig.Contains("__get_clip")) return aso.clip;
            if (sig.Contains("__set_clip")) { aso.clip = a[1] as AudioClip; return null; }
            if (sig.Contains("__get_playOnAwake")) return aso.playOnAwake;
            if (sig.Contains("__set_playOnAwake")) { aso.playOnAwake = ABool(a[1]); return null; }
            if (sig.Contains("__get_isPlaying")) return aso.isPlaying;
            if (sig.Contains("__get_volume")) return aso.volume;
            if (sig.Contains("__set_volume")) { aso.volume = AFloat(a[1]); return null; }
            if (sig.Contains("__get_pitch")) return aso.pitch;
            if (sig.Contains("__set_pitch")) { aso.pitch = AFloat(a[1]); return null; }
            if (sig.Contains("__get_loop")) return aso.loop;
            if (sig.Contains("__set_loop")) { aso.loop = ABool(a[1]); return null; }
            if (sig.Contains("__get_spatialBlend")) return aso.spatialBlend;
            if (sig.Contains("__set_spatialBlend")) { aso.spatialBlend = AFloat(a[1]); return null; }
            if (sig.Contains("__get_time")) return aso.time;
            if (sig.Contains("__set_time")) { aso.time = AFloat(a[1]); return null; }
            return null;
        }

        // ---------- UnityEngine.Renderer / Material ----------
        internal static object URenderer(string sig, UdonRuntime rt, object[] a)
        {
            Renderer r = AComponent<Renderer>(a[0]);
            if (r == null) return null;
            // Test plural accessors first: "__get_materials" also contains
            // the singular "__get_material" prefix. The source phone-charge
            // program indexes this returned Material[] to select its screen.
            if (sig.Contains("__get_materials")) return r.materials;
            if (sig.Contains("__set_materials")) { r.materials = a[1] as Material[]; return null; }
            if (sig.Contains("__get_material")) return r.material;
            if (sig.Contains("__get_sharedMaterial")) return r.sharedMaterial;
            if (sig.Contains("__set_material")) { r.material = a[1] as Material; return null; }
            if (sig.Contains("__set_sharedMaterial")) { r.sharedMaterial = a[1] as Material; return null; }
            if (sig.Contains("__get_enabled")) return r.enabled;
            if (sig.Contains("__set_enabled")) { r.enabled = ABool(a[1]); return null; }
            if (sig.Contains("__get_bounds")) return r.bounds;
            return null;
        }

        internal static object UMaterial(string sig, UdonRuntime rt, object[] a)
        {
            Material m = a[0] as Material;
            if (m == null) return null;
            if (sig.Contains("__set_color")) { m.color = AColor(a[1]); return null; }
            if (sig.Contains("__get_color")) return m.color;
            if (sig.Contains("__set_mainTexture")) { m.mainTexture = a[1] as Texture; return null; }
            if (sig.Contains("__get_mainTexture")) return m.mainTexture;
            if (sig.Contains("__set_mainTextureOffset")) { m.mainTextureOffset = AV2(a[1]); return null; }
            if (sig.Contains("__get_mainTextureOffset")) return m.mainTextureOffset;
            if (sig.Contains("__set_mainTextureScale")) { m.mainTextureScale = AV2(a[1]); return null; }
            if (sig.Contains("__SetFloat")) { m.SetFloat(AStr(a[1]), AFloat(a[2])); return null; }
            if (sig.Contains("__GetFloat")) return m.GetFloat(AStr(a[1]));
            if (sig.Contains("__SetColor")) { m.SetColor(AStr(a[1]), AColor(a[2])); return null; }
            if (sig.Contains("__GetColor")) return m.GetColor(AStr(a[1]));
            if (sig.Contains("__SetTextureScale")) { m.SetTextureScale(AStr(a[1]), AV2(a[2])); return null; }
            if (sig.Contains("__SetTextureOffset")) { m.SetTextureOffset(AStr(a[1]), AV2(a[2])); return null; }
            if (sig.Contains("__SetVector")) { m.SetVector(AStr(a[1]), AV4(a[2])); return null; }
            if (sig.Contains("__SetTexture")) { m.SetTexture(AStr(a[1]), a[2] as Texture); return null; }
            if (sig.Contains("__EnableKeyword")) { m.EnableKeyword(AStr(a[1])); return null; }
            if (sig.Contains("__DisableKeyword")) { m.DisableKeyword(AStr(a[1])); return null; }
            return null;
        }

        // Atlas-driven exhibits query Texture.width/height through the base
        // UnityEngine.Texture extern, not Texture2D.  Leaving that base type
        // unhandled silently converted both dimensions to zero and produced a
        // zero-sized _MainTex_ST for the first monitor cell.
        internal static object UTexture(string sig, UdonRuntime rt, object[] a)
        {
            Texture texture = a.Length == 0 ? null : a[0] as Texture;
            if (texture == null) return null;
            if (sig.Contains("__get_width")) return texture.width;
            if (sig.Contains("__get_height")) return texture.height;
            if (sig.Contains("__get_name")) return texture.name;
            return null;
        }

        // ---------- UnityEngine.Light / Collider / Rigidbody / Camera ----------
        internal static object ULight(string sig, UdonRuntime rt, object[] a)
        {
            Light l = AComponent<Light>(a[0]);
            if (l == null) return null;
            if (sig.Contains("__get_color")) return l.color;
            if (sig.Contains("__set_color")) { l.color = AColor(a[1]); return null; }
            if (sig.Contains("__get_intensity")) return l.intensity;
            if (sig.Contains("__set_intensity")) { l.intensity = AFloat(a[1]); return null; }
            if (sig.Contains("__get_range")) return l.range;
            if (sig.Contains("__set_range")) { l.range = AFloat(a[1]); return null; }
            if (sig.Contains("__get_enabled")) return l.enabled;
            if (sig.Contains("__set_enabled")) { l.enabled = ABool(a[1]); return null; }
            return null;
        }

        internal static object UCollider(string sig, UdonRuntime rt, object[] a)
        {
            Collider c = AComponent<Collider>(a[0]);
            if (c == null) return null;
            if (sig.Contains("__get_size"))
            {
                var box = c as BoxCollider;
                return box == null ? Vector3.one : box.size;
            }
            if (sig.Contains("__get_center"))
            {
                var box = c as BoxCollider;
                return box == null ? Vector3.zero : box.center;
            }
            if (sig.Contains("__ClosestPoint")) return c.ClosestPoint(AV3(a[1]));
            if (sig.Contains("__get_enabled")) return c.enabled;
            if (sig.Contains("__set_enabled")) { c.enabled = ABool(a[1]); return null; }
            if (sig.Contains("__get_isTrigger")) return c.isTrigger;
            if (sig.Contains("__set_isTrigger")) { c.isTrigger = ABool(a[1]); return null; }
            if (sig.Contains("__get_gameObject")) return c.gameObject;
            return null;
        }

        internal static object URigidbody(string sig, UdonRuntime rt, object[] a)
        {
            Rigidbody rb = AComponent<Rigidbody>(a[0]);
            if (rb == null) return null;
            if (sig.Contains("__get_position")) return rb.position;
            if (sig.Contains("__set_position")) { rb.position = AV3(a[1]); return null; }
            if (sig.Contains("__get_rotation")) return rb.rotation;
            if (sig.Contains("__set_rotation")) { rb.rotation = AQuat(a[1]); return null; }
            if (sig.Contains("__get_velocity")) return rb.linearVelocity;
            if (sig.Contains("__set_velocity")) { rb.linearVelocity = AV3(a[1]); return null; }
            if (sig.Contains("__get_angularVelocity")) return rb.angularVelocity;
            if (sig.Contains("__set_angularVelocity")) { rb.angularVelocity = AV3(a[1]); return null; }
            if (sig.Contains("__get_isKinematic")) return rb.isKinematic;
            if (sig.Contains("__set_isKinematic")) { rb.isKinematic = ABool(a[1]); return null; }
            if (sig.Contains("__get_useGravity")) return rb.useGravity;
            if (sig.Contains("__set_useGravity")) { rb.useGravity = ABool(a[1]); return null; }
            if (sig.Contains("__set_collisionDetectionMode"))
            {
                rb.collisionDetectionMode = (CollisionDetectionMode)AInt(a[1]);
                return null;
            }
            if (sig.Contains("__WakeUp")) { rb.WakeUp(); return null; }
            if (sig.Contains("__get_drag")) return rb.linearDamping;
            if (sig.Contains("__set_drag")) { rb.linearDamping = AFloat(a[1]); return null; }
            if (sig.Contains("__AddForce")) { rb.AddForce(AV3(a[1])); return null; }
            if (sig.Contains("__MovePosition")) { rb.MovePosition(AV3(a[1])); return null; }
            if (sig.Contains("__MoveRotation")) { rb.MoveRotation(AQuat(a[1])); return null; }
            return null;
        }

        internal static object UCamera(string sig, UdonRuntime rt, object[] a)
        {
            Camera c = AComponent<Camera>(a[0]);
            if (c == null) return null;
            if (sig.Contains("__get_fieldOfView")) return c.fieldOfView;
            if (sig.Contains("__set_fieldOfView")) { c.fieldOfView = AFloat(a[1]); return null; }
            if (sig.Contains("__get_enabled")) return c.enabled;
            if (sig.Contains("__set_enabled")) { c.enabled = ABool(a[1]); return null; }
            return null;
        }        // ---------- UnityEngine.Vector3 / Quaternion / Color / Vector2 ----------
        internal static object UVector3(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("__ctor__")) return new Vector3(AFloat(a[0]), AFloat(a[1]), AFloat(a[2]));
            Vector3 v = AV3(a[0]);
            if (sig.Contains("__get_x")) return v.x;
            if (sig.Contains("__get_y")) return v.y;
            if (sig.Contains("__get_z")) return v.z;
            if (sig.Contains("__set_x")) { v.x = AFloat(a[1]); return v; }
            if (sig.Contains("__set_y")) { v.y = AFloat(a[1]); return v; }
            if (sig.Contains("__set_z")) { v.z = AFloat(a[1]); return v; }
            if (sig.Contains("__get_magnitude")) return v.magnitude;
            if (sig.Contains("__get_sqrMagnitude")) return v.sqrMagnitude;
            if (sig.Contains("__get_normalized")) return v.normalized;
            if (sig.Contains("__Normalize")) { v.Normalize(); return v; }
            if (sig.Contains("__Dot")) return Vector3.Dot(v, AV3(a[1]));
            if (sig.Contains("__Cross")) return Vector3.Cross(v, AV3(a[1]));
            if (sig.Contains("__Distance")) return Vector3.Distance(v, AV3(a[1]));
            if (sig.Contains("__Lerp")) return Vector3.Lerp(v, AV3(a[1]), AFloat(a[2]));
            if (sig.Contains("__MoveTowards")) return Vector3.MoveTowards(v, AV3(a[1]), AFloat(a[2]));
            if (sig.Contains("__Scale")) return Vector3.Scale(v, AV3(a[1]));
            if (sig.Contains("__Slerp")) return Vector3.Slerp(v, AV3(a[1]), AFloat(a[2]));
            if (sig.Contains("__Project")) return Vector3.Project(v, AV3(a[1]));
            if (sig.Contains("__get_zero")) return Vector3.zero;
            if (sig.Contains("__get_one")) return Vector3.one;
            if (sig.Contains("__get_up")) return Vector3.up;
            if (sig.Contains("__get_forward")) return Vector3.forward;
            if (sig.Contains("__get_right")) return Vector3.right;
            if (sig.Contains("__get_down")) return Vector3.down;
            return v;
        }

        internal static object UQuaternion(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("__Euler"))
            {
                if (a.Length > 0 && a[0] is Vector3) return Quaternion.Euler(AV3(a[0]));
                if (a.Length > 1) return Quaternion.Euler(AFloat(a[0]), AFloat(a[1]), AFloat(a[2]));
                return Quaternion.identity;
            }
            Quaternion q = AQuat(a[0]);
            if (sig.Contains("__get_eulerAngles")) return q.eulerAngles;
            if (sig.Contains("__get_identity")) return Quaternion.identity;
            if (sig.Contains("__get_w")) return q.w;
            if (sig.Contains("__get_x")) return q.x;
            if (sig.Contains("__get_y")) return q.y;
            if (sig.Contains("__get_z")) return q.z;
            if (sig.Contains("__LookRotation")) return Quaternion.LookRotation(AV3(a[0]));
            if (sig.Contains("__Slerp")) return Quaternion.Slerp(q, AQuat(a[1]), AFloat(a[2]));
            if (sig.Contains("__FromToRotation")) return Quaternion.FromToRotation(AV3(a[0]), AV3(a[1]));
            if (sig.Contains("__op_Multiply"))
                return a.Length > 1 && a[1] is Vector3 ? q * AV3(a[1]) : q * AQuat(a[1]);
            return q;
        }

        internal static object UColor(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("__ctor__")) return Color.white;
            Color c = AColor(a[0]);
            if (sig.Contains("__get_r")) return c.r;
            if (sig.Contains("__get_g")) return c.g;
            if (sig.Contains("__get_b")) return c.b;
            if (sig.Contains("__get_a")) return c.a;
            if (sig.Contains("__set_r")) { c.r = AFloat(a[1]); return c; }
            if (sig.Contains("__set_g")) { c.g = AFloat(a[1]); return c; }
            if (sig.Contains("__set_b")) { c.b = AFloat(a[1]); return c; }
            if (sig.Contains("__set_a")) { c.a = AFloat(a[1]); return c; }
            if (sig.Contains("__get_white")) return Color.white;
            if (sig.Contains("__get_black")) return Color.black;
            if (sig.Contains("__get_clear")) return Color.clear;
            if (sig.Contains("__Lerp")) return Color.Lerp(c, AColor(a[1]), AFloat(a[2]));
            return c;
        }

        internal static object UVector2(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("__ctor__")) return new Vector2(AFloat(a[0]), AFloat(a[1]));
            Vector2 v = AV2(a[0]);
            if (sig.Contains("__get_x")) return v.x;
            if (sig.Contains("__get_y")) return v.y;
            if (sig.Contains("__get_magnitude")) return v.magnitude;
            if (sig.Contains("__get_sqrMagnitude")) return v.sqrMagnitude;
            if (sig.Contains("__get_normalized")) return v.normalized;
            if (sig.Contains("__Dot")) return Vector2.Dot(v, AV2(a[1]));
            if (sig.Contains("__Distance")) return Vector2.Distance(v, AV2(a[1]));
            if (sig.Contains("__Lerp")) return Vector2.Lerp(v, AV2(a[1]), AFloat(a[2]));
            return v;
        }

        internal static object UVector4(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("__ctor__")) return new Vector4(AFloat(a[0]), AFloat(a[1]), AFloat(a[2]), AFloat(a[3]));
            Vector4 v = AV4(a[0]);
            if (sig.Contains("__get_x")) return v.x;
            if (sig.Contains("__get_y")) return v.y;
            if (sig.Contains("__get_z")) return v.z;
            if (sig.Contains("__get_w")) return v.w;
            return v;
        }

        // ---------- UnityEngine.Time / Mathf / Random / Debug / Object / misc ----------
        internal static object UTime(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("__get_time")) return Time.time;
            if (sig.Contains("__get_deltaTime")) return Time.deltaTime;
            if (sig.Contains("__get_unscaledTime")) return Time.unscaledTime;
            if (sig.Contains("__get_fixedTime")) return Time.fixedTime;
            if (sig.Contains("__get_fixedDeltaTime")) return Time.fixedDeltaTime;
            if (sig.Contains("__get_timeScale")) return Time.timeScale;
            if (sig.Contains("__get_frameCount")) return Time.frameCount;
            return 0f;
        }

        internal static object UMathf(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("__Abs")) return Mathf.Abs(AFloat(a[0]));
            if (sig.Contains("__Max")) return Mathf.Max(AFloat(a[0]), AFloat(a[1]));
            if (sig.Contains("__Min")) return Mathf.Min(AFloat(a[0]), AFloat(a[1]));
            if (sig.Contains("__Clamp01")) return Mathf.Clamp01(AFloat(a[0]));
            if (sig.Contains("__Clamp")) return Mathf.Clamp(AFloat(a[0]), AFloat(a[1]), AFloat(a[2]));
            if (sig.Contains("__Lerp")) return Mathf.Lerp(AFloat(a[0]), AFloat(a[1]), AFloat(a[2]));
            if (sig.Contains("__LerpUnclamped")) return Mathf.LerpUnclamped(AFloat(a[0]), AFloat(a[1]), AFloat(a[2]));
            if (sig.Contains("__Pow")) return Mathf.Pow(AFloat(a[0]), AFloat(a[1]));
            if (sig.Contains("__Sqrt")) return Mathf.Sqrt(AFloat(a[0]));
            if (sig.Contains("__Floor")) return Mathf.Floor(AFloat(a[0]));
            if (sig.Contains("__Ceil")) return Mathf.Ceil(AFloat(a[0]));
            if (sig.Contains("__Round")) return Mathf.Round(AFloat(a[0]));
            if (sig.Contains("__Sin")) return Mathf.Sin(AFloat(a[0]));
            if (sig.Contains("__Cos")) return Mathf.Cos(AFloat(a[0]));
            if (sig.Contains("__Tan")) return Mathf.Tan(AFloat(a[0]));
            if (sig.Contains("__Asin")) return Mathf.Asin(AFloat(a[0]));
            if (sig.Contains("__Acos")) return Mathf.Acos(AFloat(a[0]));
            if (sig.Contains("__Atan")) return Mathf.Atan(AFloat(a[0]));
            if (sig.Contains("__Atan2")) return Mathf.Atan2(AFloat(a[0]), AFloat(a[1]));
            if (sig.Contains("__Exp")) return Mathf.Exp(AFloat(a[0]));
            if (sig.Contains("__Log")) return Mathf.Log(AFloat(a[0]));
            if (sig.Contains("__Sign")) return Mathf.Sign(AFloat(a[0]));
            if (sig.Contains("__Repeat")) return Mathf.Repeat(AFloat(a[0]), AFloat(a[1]));
            if (sig.Contains("__PingPong")) return Mathf.PingPong(AFloat(a[0]), AFloat(a[1]));
            if (sig.Contains("__MoveTowards")) return Mathf.MoveTowards(AFloat(a[0]), AFloat(a[1]), AFloat(a[2]));
            if (sig.Contains("__PI")) return Mathf.PI;
            if (sig.Contains("__Deg2Rad")) return Mathf.Deg2Rad;
            if (sig.Contains("__Rad2Deg")) return Mathf.Rad2Deg;
            if (sig.Contains("__FloorToInt")) return Mathf.FloorToInt(AFloat(a[0]));
            if (sig.Contains("__CeilToInt")) return Mathf.CeilToInt(AFloat(a[0]));
            if (sig.Contains("__RoundToInt")) return Mathf.RoundToInt(AFloat(a[0]));
            return 0f;
        }

        internal static object URandom(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("__get_value")) return UnityEngine.Random.value;
            // Random.Range has distinct integer and float overloads.  The
            // integer version is max-exclusive and is used by source Udon
            // texture-sheet/state indices (for example HeroNameBoard).  The
            // previous float-only bridge could leave a fractional heap value
            // that later mixed C# rounding with Udon integer remainder.
            if (sig.Contains("__Range__SystemInt32_SystemInt32__SystemInt32"))
                return UnityEngine.Random.Range(AInt(a[0]), AInt(a[1]));
            if (sig.Contains("__Range")) return UnityEngine.Random.Range(AFloat(a[0]), AFloat(a[1]));
            if (sig.Contains("__get_insideUnitSphere")) return UnityEngine.Random.insideUnitSphere;
            return 0f;
        }

        internal static object UDebug(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("__Log")) { Debug.Log(AStr(a[0])); return null; }
            if (sig.Contains("__LogWarning")) { Debug.LogWarning(AStr(a[0])); return null; }
            if (sig.Contains("__LogError")) { Debug.LogError(AStr(a[0])); return null; }
            return null;
        }

        internal static object UObject(string sig, UdonRuntime rt, object[] a)
        {
            UnityEngine.Object o = AObj(a[0]);
            if (sig.Contains("__get_name")) return o == null ? "" : o.name;
            if (sig.Contains("__set_name")) { if (o != null) o.name = AStr(a[1]); return null; }
            if (sig.Contains("__GetInstanceID")) return o == null ? 0 : o.GetHashCode();
            if (sig.Contains("__Destroy")) { if (o != null) UnityEngine.Object.Destroy(o); return null; }
            return o;
        }

        internal static object UResources(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("__Load")) return Resources.Load(AStr(a[0]));
            return null;
        }

        /// <summary>Return payload for a Udon Physics query with an out hit argument.</summary>
        internal sealed class PhysicsQueryResult
        {
            internal bool didHit;
            internal RaycastHit hit;
            // Logical argument index of the source `out RaycastHit` slot.
            internal int hitArgumentIndex;
        }

        internal static object UPhysics(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("__Raycast__"))
            {
                Vector3 origin = AV3(a[0]);
                Vector3 direction = AV3(a[1]);
                float distance = AFloat(a[3]);
                int layerMask = AInt(a[4]);
                RaycastHit hit;
                bool didHit = Physics.Raycast(origin, direction, out hit, distance, layerMask);
                return new PhysicsQueryResult { didHit = didHit, hit = hit, hitArgumentIndex = 2 };
            }
            if (sig.Contains("__SphereCast__"))
            {
                RaycastHit hit;
                bool didHit = Physics.SphereCast(AV3(a[0]), AFloat(a[1]), AV3(a[2]), out hit,
                    AFloat(a[4]), AInt(a[5]));
                return new PhysicsQueryResult { didHit = didHit, hit = hit, hitArgumentIndex = 3 };
            }
            return false;
        }

        internal static object ULayerMask(string sig, UdonRuntime rt, object[] a)
        {
            // The exported Udon bytecode converts serialized LayerMask values
            // immediately before Physics queries.  A LayerMask is stored as
            // its source integer bit field in this reconstruction.
            if (sig.Contains("__op_Implicit")) return AInt(a[0]);
            return AInt(a[0]);
        }

        internal static object UParticles(string sig, UdonRuntime rt, object[] a)
        {
            ParticleSystem ps = AComponent<ParticleSystem>(a[0]);
            if (ps == null) return null;
            if (sig.Contains("__Play")) { ps.Play(); return null; }
            if (sig.Contains("__Stop")) { ps.Stop(); return null; }
            if (sig.Contains("__Pause")) { ps.Pause(); return null; }
            if (sig.Contains("__get_isPlaying")) return ps.isPlaying;
            if (sig.Contains("__Clear")) { ps.Clear(); return null; }
            return null;
        }

        internal static object UAnimation(string sig, UdonRuntime rt, object[] a)
        {
            Animation an = AComponent<Animation>(a[0]);
            if (an == null) return null;
            if (sig.Contains("__Play")) { an.Play(AStr(a[1])); return null; }
            if (sig.Contains("__Stop")) { an.Stop(); return null; }
            if (sig.Contains("__get_isPlaying")) return an.isPlaying;
            return null;
        }

        internal static object URectTransform(string sig, UdonRuntime rt, object[] a)
        {
            RectTransform r = AComponent<RectTransform>(a[0]);
            if (r == null) return null;
            if (sig.Contains("__get_anchoredPosition")) return r.anchoredPosition;
            if (sig.Contains("__set_anchoredPosition")) { r.anchoredPosition = AV2(a[1]); return null; }
            if (sig.Contains("__get_sizeDelta")) return r.sizeDelta;
            if (sig.Contains("__set_sizeDelta")) { r.sizeDelta = AV2(a[1]); return null; }
            return null;
        }

        internal static object UApplication(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("__Quit")) { Application.Quit(); return null; }
            return null;
        }

        internal static object UInput(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("__GetKey")) return Input.GetKey(AStr(a[0]));
            if (sig.Contains("__GetKeyDown")) return Input.GetKeyDown(AStr(a[0]));
            if (sig.Contains("__GetAxis")) return Input.GetAxis(AStr(a[0]));
            return 0f;
        }

        internal static object UScreen(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("__get_width")) return Screen.width;
            if (sig.Contains("__get_height")) return Screen.height;
            return 0;
        }

        internal static object UPlayerPrefs(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("__GetInt")) return PlayerPrefs.GetInt(AStr(a[0]), AInt(a[1]));
            if (sig.Contains("__SetInt")) { PlayerPrefs.SetInt(AStr(a[0]), AInt(a[1])); return null; }
            if (sig.Contains("__GetFloat")) return PlayerPrefs.GetFloat(AStr(a[0]), AFloat(a[1]));
            if (sig.Contains("__SetFloat")) { PlayerPrefs.SetFloat(AStr(a[0]), AFloat(a[1])); return null; }
            if (sig.Contains("__GetString")) return PlayerPrefs.GetString(AStr(a[0]), AStr(a[1]));
            if (sig.Contains("__SetString")) { PlayerPrefs.SetString(AStr(a[0]), AStr(a[1])); return null; }
            if (sig.Contains("__HasKey")) return PlayerPrefs.HasKey(AStr(a[0]));
            if (sig.Contains("__DeleteKey")) { PlayerPrefs.DeleteKey(AStr(a[0])); return null; }
            return null;
        }

        internal static object UCanvas(string sig, UdonRuntime rt, object[] a)
        {
            Canvas c = AComponent<Canvas>(a[0]);
            if (c == null) return null;
            if (sig.Contains("__get_enabled")) return c.enabled;
            if (sig.Contains("__set_enabled")) { c.enabled = ABool(a[1]); return null; }
            return null;
        }

        internal static object URenderSettings(string sig, UdonRuntime rt, object[] a)
        {
            if (sig.Contains("__get_fogColor")) return RenderSettings.fogColor;
            if (sig.Contains("__set_fogColor")) { RenderSettings.fogColor = AColor(a[0]); return null; }
            if (sig.Contains("__get_fogDensity")) return RenderSettings.fogDensity;
            if (sig.Contains("__set_fogDensity")) { RenderSettings.fogDensity = AFloat(a[0]); return null; }
            if (sig.Contains("__get_fogStartDistance")) return RenderSettings.fogStartDistance;
            if (sig.Contains("__set_fogStartDistance")) { RenderSettings.fogStartDistance = AFloat(a[0]); return null; }
            if (sig.Contains("__get_fogEndDistance")) return RenderSettings.fogEndDistance;
            if (sig.Contains("__set_fogEndDistance")) { RenderSettings.fogEndDistance = AFloat(a[0]); return null; }
            if (sig.Contains("__get_fogMode")) return (int)RenderSettings.fogMode;
            if (sig.Contains("__set_fogMode")) { RenderSettings.fogMode = (FogMode)AInt(a[0]); return null; }
            // Test the exact fog flag only after the fog-* properties above: their
            // Udon signatures all share the "__get_fog" / "__set_fog" prefix.
            if (sig.Contains("__get_fog__SystemBoolean")) return RenderSettings.fog;
            if (sig.Contains("__set_fog__SystemBoolean")) { RenderSettings.fog = ABool(a[0]); return null; }
            if (sig.Contains("__get_ambientLight")) return RenderSettings.ambientLight;
            if (sig.Contains("__set_ambientLight")) { RenderSettings.ambientLight = AColor(a[0]); return null; }
            return null;
        }

        internal static object URaycastHit(string sig, UdonRuntime rt, object[] a)
        {
            RaycastHit h = (RaycastHit)(a[0] is RaycastHit r ? r : default);
            if (sig.Contains("__get_point")) return h.point;
            if (sig.Contains("__get_normal")) return h.normal;
            if (sig.Contains("__get_collider")) return h.collider;
            if (sig.Contains("__get_distance")) return h.distance;
            if (sig.Contains("__get_transform")) return h.transform;
            return null;
        }
    }
}
