using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using VRC.SDK3.Components;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace AniqueMuseum
{
    [DefaultExecutionOrder(-80)]
    public sealed class MuseumFlatInput : MonoBehaviour
    {
        public static MuseumFlatInput Instance { get; private set; }
        public static bool IsActive => Instance != null && Instance.isActiveAndEnabled && MuseumXRPlayer.MobileFlatReady;
        public bool BlocksGameplayInput => paused || !focused;
        FPSCameraController player;
        UdonInteractor interactor;
        enum Role { None, Move, Look, Use, Run, Interact, Drop, Jump, Crouch, Menu }
        readonly MuseumTouchOverlayState overlay = new MuseumTouchOverlayState();
        public bool TouchControlsVisible => overlay.Visible;
        public static bool HasConnectedGamepad
        {
            get { foreach (var pad in Gamepad.all) if (pad.added && pad.enabled) return true; return false; }
        }
        readonly Dictionary<int, Role> roles = new Dictionary<int, Role>();
        readonly HashSet<int> alive = new HashSet<int>();
        readonly List<int> stale = new List<int>();
        Vector2 move, look;
        bool jump, interact, drop, use, run, paused, focused = true;
        VRCPickup usingPickup;
        Texture2D disc;
        GUIStyle label, button, heading;
        float scale;
        Vector2 origin;
        int menuSelection;
        float previousTimeScale = 1;
        Vector2 stickCenter = new Vector2(155,565);
        const float StickRadius = 84;
        Vector2 canvasSize = new Vector2(1280,720);
        Rect lastSafeArea;
        int lastWidth, lastHeight;
        bool portrait;
        Rect interactRect = new Rect(1070,425,178,68);
        Rect useRect = new Rect(1070,505,178,68);
        Rect dropRect = new Rect(910,625,150,66);
        Rect jumpRect = new Rect(1090,615,158,76);
        Rect runRect = new Rect(274,610,138,72);
        Rect crouchRect;
        Rect menuRect = new Rect(1150,14,98,56);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Application.platform != RuntimePlatform.Android && !Array.Exists(Environment.GetCommandLineArgs(), a=>a=="--flat-controls")) return;
            var player = FindAnyObjectByType<FPSCameraController>();
            if (player != null && player.GetComponent<MuseumFlatInput>() == null) player.gameObject.AddComponent<MuseumFlatInput>();
        }

        void Awake()
        {
            Instance = this; player = GetComponent<FPSCameraController>(); interactor = GetComponent<UdonInteractor>();
            player.flatControlled = true; player.lockCursor = false;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
            Screen.autorotateToPortrait = Screen.autorotateToPortraitUpsideDown = true;
            Screen.autorotateToLandscapeLeft = Screen.autorotateToLandscapeRight = true;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Application.targetFrameRate = 60;
            // Source world UI keeps its events. Replace only the legacy input module.
            if (EventSystem.current == null) new GameObject("Flat EventSystem").AddComponent<EventSystem>();
            foreach (var module in EventSystem.current.GetComponents<BaseInputModule>()) module.enabled = false;
            // Touch controls own their contacts; centre-ray UI dispatch prevents taps
            // on MOVE/USE from also clicking a world Canvas behind the overlay.
            disc = new Texture2D(64,64,TextureFormat.RGBA32,false) { name = "Touch joystick disc" };
            for (int y=0;y<64;y++) for(int x=0;x<64;x++)
                disc.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01(32-Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(32,32)))));
            disc.Apply();
        }
        public static object ReadLegacyInput(string name, bool axis, bool down)
        {
            var i=Instance;
            if(axis)
            {
                if(i==null) return 0f;
                if(name=="Horizontal") return i.move.x; if(name=="Vertical") return i.move.y;
                if(name=="Mouse X") return i.look.x; if(name=="Mouse Y") return i.look.y;
                return 0f;
            }
            if(i!=null)
            {
                if(name=="space") return i.jump; if(name=="e") return i.interact; if(name=="q") return i.drop;
                if(name=="left shift") return i.run; if(name=="mouse 0") return down ? i.use && i.usingPickup==null : i.use;
            }
            if(Keyboard.current!=null) foreach(var key in Keyboard.current.allKeys)
                if(string.Equals(key.name,name.Replace(" ",""),StringComparison.OrdinalIgnoreCase)) return down ? key.wasPressedThisFrame : key.isPressed;
            return false;
        }

        void OnEnable() { EnhancedTouchSupport.Enable(); }
        void Layout()
        {
            var safe = Screen.safeArea;
            // Reclassify only fresh touches after rotation or a system-bar/inset change.
            if (lastWidth != Screen.width || lastHeight != Screen.height || lastSafeArea != safe)
            {
                ClearInput();
                lastWidth = Screen.width; lastHeight = Screen.height; lastSafeArea = safe;
            }
            portrait = safe.height > safe.width;
            var reference = portrait ? new Vector2(720,1280) : new Vector2(1280,720);
            scale = Mathf.Max(.1f, Mathf.Min(safe.width/reference.x, safe.height/reference.y));
            canvasSize = new Vector2(safe.width/scale, safe.height/scale);
            origin = new Vector2(safe.x, Screen.height-safe.y-safe.height);
            LayoutControls(canvasSize, portrait);
        }
        void LayoutControls(Vector2 size, bool vertical)
        {
            canvasSize = size; portrait = vertical;
            float w = canvasSize.x, h = canvasSize.y;
            if (portrait)
            {
                stickCenter = new Vector2(145,h-220);
                interactRect = new Rect(w-250,h-370,220,70);
                useRect = new Rect(w-250,h-285,220,70);
                dropRect = new Rect(w-240,h-200,210,70);
                jumpRect = new Rect(w-240,h-115,210,85);
                runRect = new Rect(50,h-110,180,72);
                crouchRect = new Rect(50,h-410,180,72);
            }
            else
            {
                stickCenter = new Vector2(155,h-155);
                interactRect = new Rect(w-210,h-295,178,68);
                useRect = new Rect(w-210,h-215,178,68);
                dropRect = new Rect(w-370,h-95,150,66);
                jumpRect = new Rect(w-190,h-105,158,76);
                runRect = new Rect(274,h-110,138,72);
                crouchRect = new Rect(w-370,h-175,150,66);
            }
            menuRect = new Rect(w-130,14,98,56);
        }
        Vector2 ToCanvas(Vector2 screen) => new Vector2((screen.x-origin.x)/scale,(Screen.height-screen.y-origin.y)/scale);
        Rect MenuRow(int i) => new Rect((canvasSize.x-420)*.5f,canvasSize.y*.5f-150+i*85,420,65);
        Role Hit(Vector2 p)
        {
            if(menuRect.Contains(p)) return Role.Menu;
            if(interactRect.Contains(p)) return Role.Interact;
            if(useRect.Contains(p)) return Role.Use;
            if(dropRect.Contains(p)) return Role.Drop;
            if(jumpRect.Contains(p)) return Role.Jump;
            if(crouchRect.Contains(p)) return Role.Crouch;
            if(runRect.Contains(p)) return Role.Run;
            if(Vector2.Distance(p,stickCenter)<StickRadius+40 && !roles.ContainsValue(Role.Move)) return Role.Move;
            if((p.x>canvasSize.x*.42f || (portrait && p.y<canvasSize.y-400)) && !roles.ContainsValue(Role.Look)) return Role.Look;
            return Role.None;
        }
        void Update()
        {
            if (!MuseumXRPlayer.MobileFlatReady) { ClearInput(); return; }
            Layout(); move=look=Vector2.zero; jump=interact=drop=use=run=false;
            bool wasVisible = overlay.Visible;
            bool touching = false;
            foreach (var contact in Touch.activeTouches)
                if (contact.phase != TouchPhase.Ended && contact.phase != TouchPhase.Canceled) { touching = true; break; }
            overlay.Observe(HasConnectedGamepad, focused && touching, Time.unscaledTime);
            if (wasVisible && !overlay.Visible) ClearInput();
            if(!focused) { player.SetFlatInput(move,look,false,false); return; }
            var pad=Gamepad.current;
            if((pad!=null && pad.startButton.wasPressedThisFrame) || (Keyboard.current!=null && Keyboard.current.escapeKey.wasPressedThisFrame)) TogglePause();
            alive.Clear();
            foreach(var t in Touch.activeTouches)
            {
                int id=t.touchId; var p=ToCanvas(t.screenPosition);
                if(t.phase==TouchPhase.Ended || t.phase==TouchPhase.Canceled) { roles.Remove(id); continue; }
                alive.Add(id);
                if(t.phase==TouchPhase.Began)
                {
                    if(paused)
                    {
                        for(int i=0;i<4;i++) if(MenuRow(i).Contains(p)) {
                            if(i==2) { float x=p.x-MenuRow(i).x; MuseumViewSettings.SetFOV(x<100 ? MuseumViewSettings.FOV-5 : x>320 ? MuseumViewSettings.FOV+5 : MuseumViewSettings.DefaultFOV); }
                            else ActivateMenu(i); break; }
                        roles[id]=Role.None; continue;
                    }
                    // A first tap reveals hidden controls without accidentally
                    // pressing a previously invisible jump / use / menu button.
                    if (!wasVisible) { roles[id]=Role.None; continue; }
                    var role=Hit(p); roles[id]=role;
                    if(role==Role.Interact) interact=true;
                    if(role==Role.Drop) drop=true;
                    if(role==Role.Jump) jump=true;
                    if(role==Role.Crouch) player.ToggleCrouch();
                    if(role==Role.Menu) { TogglePause(); break; }
                }
                if(paused || !roles.TryGetValue(id,out var active)) continue;
                if(active==Role.Move) { var delta=(p-stickCenter)/StickRadius; move=Vector2.ClampMagnitude(new Vector2(delta.x,-delta.y),1); }
                if(active==Role.Look) look+=t.delta * (105f/Mathf.Max(1,Screen.safeArea.height));
                if(active==Role.Use) use=true;
                if(active==Role.Run) run=true;
            }
            stale.Clear(); foreach(var id in roles.Keys) if(!alive.Contains(id)) stale.Add(id);
            foreach(var id in stale) roles.Remove(id);
            if(paused)
            {
                if(pad!=null)
                {
                    if(pad.dpad.down.wasPressedThisFrame) menuSelection=(menuSelection+1)%4;
                    if(pad.dpad.up.wasPressedThisFrame) menuSelection=(menuSelection+3)%4;
                    if(menuSelection==2) {
                        if(pad.dpad.left.wasPressedThisFrame) MuseumViewSettings.SetFOV(MuseumViewSettings.FOV-5);
                        if(pad.dpad.right.wasPressedThisFrame) MuseumViewSettings.SetFOV(MuseumViewSettings.FOV+5);
                    }
                    if(pad.buttonSouth.wasPressedThisFrame) ActivateMenu(menuSelection);
                    else if(pad.buttonEast.wasPressedThisFrame) TogglePause();
                }
                player.SetFlatInput(Vector2.zero,Vector2.zero,false,false); return;
            }
            // Gamepad gameplay has one shared PC/Android owner. This class
            // continues to own touch contacts and its native-style pause menu.
            player.SetFlatInput(move,look,jump,run);
        }
        void LateUpdate()
        {
            if(!MuseumXRPlayer.MobileFlatReady || paused || !focused || interactor==null) { ReleaseUse(); return; }
            if(drop && player.playerCamera!=null) {
                ReleaseUse(); var view=player.playerCamera.transform;
                if(!interactor.TryDropHeldPickup()) interactor.TryPickupRay(new Ray(view.position,view.forward),0);
            }
            if(interact && player.playerCamera!=null)
            {
                ReleaseUse(); var camera=player.playerCamera.transform;
                interactor.TryInteractOnlyRay(new Ray(camera.position,camera.forward));
            }
            var held=VRCPickup.HeldPickup;
            if(!use || usingPickup!=held) ReleaseUse();
            if(use && held!=null && usingPickup==null) { usingPickup=held; interactor.TryUseHeldPickup(true); }
        }
        void ReleaseUse() { if(usingPickup!=null) usingPickup.UseUp(); usingPickup=null; }
        void TogglePause()
        {
            paused=!paused;
            if(paused) { previousTimeScale=Time.timeScale; Time.timeScale=0; ReleaseUse(); }
            else { Time.timeScale=previousTimeScale; MuseumViewSettings.Save(); }
            ClearInput();
        }
        void ActivateMenu(int index)
        {
            if(index==0) { if(paused) TogglePause(); }
            else if(index==1) MuseumDebug.SetEnabled(!MuseumDebug.Enabled);
            else if(index==2) MuseumViewSettings.SetFOV(MuseumViewSettings.DefaultFOV);
            else Application.Quit();
        }
        void ClearInput()
        {
            roles.Clear(); move=look=Vector2.zero; jump=interact=drop=use=run=false;
            if(player!=null) player.SetFlatInput(move,look,false,false);
            ReleaseUse();
        }
        void OnApplicationFocus(bool value) { focused=value; if(!value) ClearInput(); }
        void OnApplicationPause(bool value) { if(value) { focused=false; if(!paused && MuseumXRPlayer.MobileFlatReady) TogglePause(); ClearInput(); } else focused=true; }
        void OnDisable()
        {
            ClearInput(); EnhancedTouchSupport.Disable();
            if(paused) Time.timeScale=previousTimeScale;
            if(Instance==this) Instance=null;
        }
        void OnDestroy() { if(disc!=null) Destroy(disc); }
        void Box(Rect r,string text,bool held=false)
        {
            GUI.color = held ? new Color(.15f,.48f,.9f,.9f) : new Color(.04f,.06f,.09f,.7f);
            GUI.DrawTexture(r,Texture2D.whiteTexture); GUI.color=Color.white; GUI.Label(r,text,button);
        }
        void OnGUI()
        {
            if(player==null || !MuseumXRPlayer.MobileFlatReady) return; Layout();
            if(label==null)
            {
                label=new GUIStyle(GUI.skin.label){fontSize=22,alignment=TextAnchor.MiddleCenter,wordWrap=true}; label.normal.textColor=Color.white;
                button=new GUIStyle(label){fontStyle=FontStyle.Bold};
                heading=new GUIStyle(button){fontSize=32};
            }
            var matrix=GUI.matrix; var color=GUI.color;
            GUI.matrix=Matrix4x4.TRS(new Vector3(origin.x,origin.y,0),Quaternion.identity,new Vector3(scale,scale,1));
            if(paused)
            {
                GUI.color=new Color(0,0,0,.84f); GUI.DrawTexture(new Rect(0,0,canvasSize.x,canvasSize.y),Texture2D.whiteTexture); GUI.color=Color.white;
                GUI.Label(new Rect((canvasSize.x-420)*.5f,canvasSize.y*.5f-242,420,70),"PAUSED",heading);
                Box(MenuRow(0),"RESUME",menuSelection==0);
                Box(MenuRow(1),"DEBUG: "+(MuseumDebug.Enabled?"ON":"OFF"),menuSelection==1);
                var fov=MenuRow(2);
                Box(new Rect(fov.x,fov.y,100,fov.height),"- 5",menuSelection==2);
                Box(new Rect(fov.x+100,fov.y,220,fov.height),"FOV "+Mathf.RoundToInt(MuseumViewSettings.FOV)+" / RESET",menuSelection==2);
                Box(new Rect(fov.x+320,fov.y,100,fov.height),"+ 5",menuSelection==2);
                Box(MenuRow(3),"EXIT",menuSelection==3);
                GUI.Label(new Rect((canvasSize.x-640)*.5f,canvasSize.y*.5f+225,640,60),"Gamepad: D-pad selects / A confirms / B resumes",label);
            }
            else
            {
                if (overlay.Visible)
                {
                GUI.color=new Color(.02f,.04f,.08f,.55f); GUI.DrawTexture(new Rect(stickCenter.x-StickRadius,stickCenter.y-StickRadius,StickRadius*2,StickRadius*2),disc);
                GUI.color=new Color(.75f,.85f,1,.78f); var knob=stickCenter+new Vector2(move.x,-move.y)*StickRadius*.65f;
                GUI.DrawTexture(new Rect(knob.x-31,knob.y-31,62,62),disc); GUI.color=Color.white;
                Box(interactRect,"INTERACT / X"); Box(useRect,"USE / RT",use); Box(dropRect,"GRAB-DROP / Y"); Box(jumpRect,"JUMP / A",jump); Box(runRect,"RUN / LB",run); Box(menuRect,"MENU");
                Box(crouchRect, player.IsCrouched ? "STAND / B" : "CROUCH / B",player.IsCrouched);
                GUI.Label(new Rect(portrait ? (canvasSize.x-400)*.5f : canvasSize.x-550,130,400,42),"Drag here to look",label);
                }
                // Same physical screen centre as the camera ray, including asymmetric cutouts.
                var centre=new Vector2((Screen.width*.5f-origin.x)/scale,(Screen.height*.5f-origin.y)/scale);
                GUI.color=Color.black; GUI.DrawTexture(new Rect(centre.x-7,centre.y-2,14,4),Texture2D.whiteTexture); GUI.DrawTexture(new Rect(centre.x-2,centre.y-7,4,14),Texture2D.whiteTexture);
                GUI.color=Color.white; GUI.DrawTexture(new Rect(centre.x-6,centre.y-1,12,2),Texture2D.whiteTexture); GUI.DrawTexture(new Rect(centre.x-1,centre.y-6,2,12),Texture2D.whiteTexture);
                string text=interactor==null?"":UdonInteractor.GamepadPrompt(interactor.Prompt);
                GUI.Label(new Rect(centre.x-280,centre.y+28,560,90),text,label);
                if(MuseumDebug.Enabled) GUI.Label(new Rect(12,12,canvasSize.x-160,portrait ? 110 : 58),"DEBUG | Red: interactions; wire: trigger bounds | Pos: "+player.transform.position+" | Target: "+(interactor.HoverTarget!=null?interactor.HoverTarget.name:"-"),label);
            }
            GUI.matrix=matrix; GUI.color=color;
        }
    }
}
