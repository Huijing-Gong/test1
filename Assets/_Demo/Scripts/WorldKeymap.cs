using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// One profile shared by both settings pages and the main-world input readers.
public sealed class WorldKeymap
{
    public sealed class Action
    {
        public readonly string id,label,group,defaultKey;public readonly bool interfaceAction;
        public bool Editable=>group=="COMBAT"&&id!="Dodge";
        public Action(string id,string label,string group,string key,bool ui=false){this.id=id;this.label=label;this.group=group;defaultKey=key;interfaceAction=ui;}
    }
    public static readonly Action[] Actions={
        new Action("Forward","Move forward / climb up","MOVEMENT","W"),new Action("Back","Move back / climb down","MOVEMENT","S"),
        new Action("Left","Move left","MOVEMENT","A"),new Action("Right","Move right","MOVEMENT","D"),
        new Action("Sprint","Sprint (hold)","MOVEMENT","ShiftOrMouseRight"),new Action("Jump","Jump / let go","MOVEMENT","Space"),
        new Action("Dodge","Dodge (press) + direction","COMBAT","ShiftOrMouseRight"),new Action("Guard","Guard (hold)","COMBAT","Q"),
        new Action("Attack","Attack / combo / counter","COMBAT","MouseLeft"),
        new Action("Interact","Interact / advance dialogue","WORLD","E"),new Action("Recover","Recover / rest","WORLD","H"),
        new Action("Bag","Backpack","INTERFACE","B",true),new Action("Character","Character equipment","INTERFACE","U",true),
        new Action("Map","Map","INTERFACE","M",true),new Action("Journal","Tasks","INTERFACE","J",true),
        new Action("Recenter","Recenter camera","WORLD","R"),new Action("Controls","Controls / key bindings","INTERFACE","F1",true),
        new Action("ReturnWorld","Return from side world","INTERFACE","F8",true)
    };
    [Serializable] public sealed class Entry {public string action,binding;}
    [Serializable] public sealed class SaveData {public int version=3;public Entry[] entries;}
    readonly Dictionary<string,string> keys=new Dictionary<string,string>();
    public WorldKeymap(){Reset();}
    public void Reset(){keys.Clear();foreach(var a in Actions)keys[a.id]=a.defaultKey;}
    public string GetBinding(string action)=>keys.TryGetValue(action,out var value)?value:"";
    public static Action Find(string id){foreach(var action in Actions)if(action.id==id)return action;return null;}
    public static Key MovementAlias(string id){switch(id){case "Forward":return Key.UpArrow;case "Back":return Key.DownArrow;case "Left":return Key.LeftArrow;case "Right":return Key.RightArrow;default:return Key.None;}}
    public static string Normalize(string key)
    {
        if(key=="RightShift")return "LeftShift";if(key=="RightAlt")return "LeftAlt";if(key=="RightCtrl")return "LeftCtrl";return key;
    }
    static bool Valid(Action action,string key,out string error)
    {
        error="";
        if(action==null){error="Unknown action.";return false;}
        if(key=="ShiftOrMouseRight"&&(action.id=="Dodge"||action.id=="Sprint"))return true;
        if(key=="LeftShift"||key=="MouseRight"){error="Shift and RMB are shared dodge / hold-to-sprint controls.";return false;}
        if(key=="Escape"||key=="LeftCtrl"){error="Esc and Ctrl are reserved for closing pages and clicking HUD icons.";return false;}
        if(action.Editable&&(key=="UpArrow"||key=="DownArrow"||key=="LeftArrow"||key=="RightArrow")){error="Arrow keys are fixed movement controls.";return false;}
        if(action.interfaceAction&&(key=="MouseLeft"||key=="MouseRight")){error="Left/right click are reserved for navigating interface pages.";return false;}
        if(key=="MouseLeft"||key=="MouseRight"||key=="MouseMiddle"||key=="MouseForward"||key=="MouseBack")return true;
        if(Enum.TryParse(key,out Key parsed)&&parsed!=Key.None&&Enum.IsDefined(typeof(Key),parsed))return true;
        error="Unsupported key.";return false;
    }
    public bool TrySet(string action,string binding,out string error)
    {
        var definition=Find(action);if(definition!=null&&!definition.Editable){error="Basic controls are fixed. Only combat bindings can be edited.";return false;}
        binding=Normalize(binding);if(!Valid(Find(action),binding,out error))return false;
        foreach(var other in Actions)if(other.id!=action&&keys[other.id]==binding){error=Display(binding)+" is already used by "+other.label+". Choose another key.";return false;}
        keys[action]=binding;return true;
    }
    public string ToJson(){var entries=new Entry[Actions.Length];for(int i=0;i<entries.Length;i++)entries[i]=new Entry{action=Actions[i].id,binding=keys[Actions[i].id]};return JsonUtility.ToJson(new SaveData{entries=entries});}
    public bool LoadJson(string json)
    {
        try{
            var data=JsonUtility.FromJson<SaveData>(json);if(data==null||(data.version!=1&&data.version!=2&&data.version!=3)||data.entries==null||data.entries.Length==0)return false;
            var candidate=new WorldKeymap();var seen=new HashSet<string>();
            foreach(var entry in data.entries){if(entry==null||!seen.Add(entry.action))return false;var action=Find(entry.action);if(action==null)continue;var binding=Normalize(entry.binding);if(data.version<3&&(action.id=="Dodge"||action.id=="Sprint"))continue;if(data.version<3&&(binding=="LeftShift"||binding=="MouseRight"))binding=action.defaultKey;if(!Valid(action,binding,out _))return false;if(!action.Editable&&binding!=action.defaultKey)return false;candidate.keys[action.id]=binding;}
            // A removed RMB binding may fall back onto another old custom binding.
            // Reset only that collision group to defaults, retaining unrelated controls.
            if(data.version<3)for(int pass=0;pass<Actions.Length;pass++)
                for(int i=0;i<Actions.Length;i++)for(int j=i+1;j<Actions.Length;j++)
                {
                    var a=Actions[i];var b=Actions[j];var key=candidate.keys[a.id];
                    if(key!=candidate.keys[b.id]||key=="ShiftOrMouseRight")continue;
                    candidate.keys[a.id]=a.defaultKey;candidate.keys[b.id]=b.defaultKey;
                }
            var used=new HashSet<string>();foreach(var a in Actions){if(a.id=="Dodge"&&candidate.keys[a.id]=="ShiftOrMouseRight"&&candidate.keys["Sprint"]=="ShiftOrMouseRight")continue;if(!used.Add(candidate.keys[a.id]))return false;}
            keys.Clear();foreach(var pair in candidate.keys)keys.Add(pair.Key,pair.Value);return true;
        }catch(ArgumentException){return false;}catch(NullReferenceException){return false;}
    }
    public static string Display(string binding)
    {
        switch(binding){case "ShiftOrMouseRight":return "Shift / RMB";case "LeftShift":return "Shift";case "LeftAlt":return "Alt";case "LeftCtrl":return "Ctrl";case "MouseLeft":return "LMB";case "MouseRight":return "RMB";case "MouseMiddle":return "MMB";case "MouseBack":return "Mouse 4";case "MouseForward":return "Mouse 5";default:return binding;}
    }
}

public static class WorldInputBindings
{
    public const string StorageKey="WorldControls.Bindings.v1";
    static WorldKeymap profile;
    public static WorldKeymap Profile {get{if(profile==null){profile=new WorldKeymap();if(PlayerPrefs.HasKey(StorageKey))profile.LoadJson(PlayerPrefs.GetString(StorageKey));}return profile;}}
    public static string Label(string action)=>WorldKeymap.Display(Profile.GetBinding(action));
    public static string CaptureAction {get;private set;}
    public static bool Capturing=>CaptureAction!=null;
    public static string Notice {get;private set;}="Click a binding, then press a key or mouse button.";
    public static bool Conflict {get;private set;}
    static int captureFrame=-1,consumedFrame=-1,pointerFrame=-10;
    public static bool ConsumedInterfaceInput=>Capturing||consumedFrame==Time.frameCount;
    public static bool PointerModifierHeld=>Keyboard.current!=null&&(Keyboard.current.leftCtrlKey.isPressed||Keyboard.current.rightCtrlKey.isPressed);
    public static bool GameplaySuppressed=>ConsumedInterfaceInput||PointerModifierHeld||Time.frameCount<=pointerFrame;
    public static void MarkPointerFrame(){pointerFrame=Time.frameCount+1;}
    public static void ConsumeInterfaceClick(){consumedFrame=Time.frameCount;MarkPointerFrame();}
    public static void Save(){PlayerPrefs.SetString(StorageKey,Profile.ToJson());PlayerPrefs.Save();}
    public static void Reload(){profile=null;}
    public static void Reset(){CancelCapture();Profile.Reset();Save();Conflict=false;Notice="Default bindings restored.";}
    public static bool Rebind(string action,string key){bool ok=Profile.TrySet(action,key,out var error);Conflict=!ok;Notice=ok?WorldKeymap.Find(action).label+" → "+Label(action):error;if(ok)Save();return ok;}
    public static void BeginCapture(string action){CaptureAction=action;captureFrame=Time.frameCount;Conflict=false;Notice="Press a new binding for "+WorldKeymap.Find(action).label+". Esc cancels.";}
    public static void CancelCapture(){if(Capturing){CaptureAction=null;consumedFrame=Time.frameCount;Notice="Rebinding cancelled; original binding kept.";Conflict=false;}}
    public static void TickCapture()
    {
        if(!Capturing||Time.frameCount<=captureFrame)return;
        if(Mouse.current!=null&&Mouse.current.rightButton.wasPressedThisFrame){CancelCapture();return;}
        var k=Keyboard.current;if(k!=null&&k.escapeKey.wasPressedThisFrame){CancelCapture();return;}
        string key=null;
        if(k!=null)foreach(var control in k.allKeys)if(control.wasPressedThisFrame){key=control.keyCode.ToString();break;}
        var m=Mouse.current;if(key==null&&m!=null){if(m.leftButton.wasPressedThisFrame&&!WorldControlsPanel.PointerOverButton(m.position.ReadValue()))key="MouseLeft";else if(m.middleButton.wasPressedThisFrame)key="MouseMiddle";else if(m.backButton.wasPressedThisFrame)key="MouseBack";else if(m.forwardButton.wasPressedThisFrame)key="MouseForward";}
        if(key==null)return;consumedFrame=Time.frameCount;if(Rebind(CaptureAction,key))CaptureAction=null;
    }
    static bool Read(string id,bool edge)
    {
        var action=WorldKeymap.Find(id);if(action==null||ConsumedInterfaceInput||(!action.interfaceAction&&GameplaySuppressed))return false;
        var alias=WorldKeymap.MovementAlias(id);if(alias!=Key.None&&Keyboard.current!=null&&(edge?Keyboard.current[alias].wasPressedThisFrame:Keyboard.current[alias].isPressed))return true;
        var key=Profile.GetBinding(id);var mouse=Mouse.current;UnityEngine.InputSystem.Controls.ButtonControl button=null;
        if(key=="ShiftOrMouseRight")return edge?KingOfGraphic.Hero.HeroDashInput.Pressed:KingOfGraphic.Hero.HeroDashInput.Held;
        switch(key){case "MouseLeft":button=mouse?.leftButton;break;case "MouseRight":button=mouse?.rightButton;break;case "MouseMiddle":button=mouse?.middleButton;break;case "MouseBack":button=mouse?.backButton;break;case "MouseForward":button=mouse?.forwardButton;break;default:
            if(Keyboard.current!=null&&Enum.TryParse(key,out Key code)){
                button=Keyboard.current[code];
                if(key=="LeftShift"&&(edge?Keyboard.current.rightShiftKey.wasPressedThisFrame:Keyboard.current.rightShiftKey.isPressed))return true;
                if(key=="LeftAlt"&&(edge?Keyboard.current.rightAltKey.wasPressedThisFrame:Keyboard.current.rightAltKey.isPressed))return true;
            }break;}
        return button!=null&&(edge?button.wasPressedThisFrame:button.isPressed);
    }
    public static bool Released(string id)
    {
        var action=WorldKeymap.Find(id);if(action==null||ConsumedInterfaceInput||(!action.interfaceAction&&GameplaySuppressed))return false;
        var key=Profile.GetBinding(id);var mouse=Mouse.current;UnityEngine.InputSystem.Controls.ButtonControl b=null;
        if(key=="ShiftOrMouseRight")return KingOfGraphic.Hero.HeroDashInput.Released;
        switch(key){case "MouseLeft":b=mouse?.leftButton;break;case "MouseRight":b=mouse?.rightButton;break;case "MouseMiddle":b=mouse?.middleButton;break;case "MouseBack":b=mouse?.backButton;break;case "MouseForward":b=mouse?.forwardButton;break;default:if(Keyboard.current!=null&&Enum.TryParse(key,out Key code))b=Keyboard.current[code];break;}
        return b!=null&&b.wasReleasedThisFrame;
    }
    public static bool Pressed(string id)=>Read(id,true);
    public static bool Held(string id)=>Read(id,false);
    public static Vector2 Movement=>Vector2.ClampMagnitude(new Vector2((Held("Right")?1:0)-(Held("Left")?1:0),(Held("Forward")?1:0)-(Held("Back")?1:0)),1);
}
