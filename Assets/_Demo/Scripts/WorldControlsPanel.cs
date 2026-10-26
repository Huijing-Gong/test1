using UnityEngine;

// Shared overview and combat-only editor for both controls entry points.
public static class WorldControlsPanel
{
    static Vector2 scroll;
    public static bool Editing {get;private set;}
    public static void ShowOverview(){Editing=false;scroll=Vector2.zero;WorldInputBindings.CancelCapture();}
    public static void BeginEditing(){Editing=true;scroll=Vector2.zero;}
    public static bool TryBack(){if(!Editing)return false;ShowOverview();return true;}
    static readonly System.Collections.Generic.List<Rect> buttons=new System.Collections.Generic.List<Rect>();
    static int buttonFrame=-10;
    public static bool PointerOverButton(Vector2 point){if(Time.frameCount-buttonFrame>2)return false;point.y=Screen.height-point.y;return buttons.Exists(r=>r.Contains(point));}
    static void Track(Rect r){var a=GUI.matrix.MultiplyPoint3x4(new Vector3(r.x,r.y));var b=GUI.matrix.MultiplyPoint3x4(new Vector3(r.xMax,r.yMax));buttons.Add(Rect.MinMaxRect(a.x,a.y,b.x,b.y));}
    public static void RegisterBackButton(Rect r)=>Track(r);
    static bool Button(Rect r,string text,GUIStyle style){Track(r);return GUI.Button(r,text,style);}
    public static void Draw(Rect area,float scale=1)
    {
        var ink=new Color32(24,62,53,255);
        var label=new GUIStyle(GUI.skin.label){fontSize=Mathf.Max(11,Mathf.RoundToInt(17*scale)),wordWrap=true};
        label.normal.textColor=label.hover.textColor=label.active.textColor=label.focused.textColor=ink;
        var button=new GUIStyle(GUI.skin.button){fontSize=label.fontSize,wordWrap=false};
        buttons.Clear();buttonFrame=Time.frameCount;
        if(!Editing){DrawOverview(area,scale,label,button);return;}
        float row=44*scale,header=45*scale,footer=125*scale;
        GUI.Label(new Rect(area.x,area.y,area.width,header),"Choose a combat binding to change.",label);
        var view=new Rect(area.x,area.y+header,area.width,Mathf.Max(row*2,area.height-header-footer));
        int count=0;foreach(var action in WorldKeymap.Actions)if(action.Editable)count++;
        scroll=GUI.BeginScrollView(view,scroll,new Rect(0,0,view.width-22,count*row));
        int i=0;foreach(var action in WorldKeymap.Actions){
            if(!action.Editable)continue;
            var r=new Rect(0,i++*row,view.width-28,row-4*scale);float keyWidth=Mathf.Min(210*scale,r.width*.48f);
            GUI.Label(new Rect(0,r.y,r.width-keyWidth-8,r.height),action.label,label);
            bool previous=GUI.enabled;GUI.enabled=previous&&(!WorldInputBindings.Capturing||WorldInputBindings.CaptureAction==action.id);
            var bindingRect=new Rect(r.xMax-keyWidth,r.y,keyWidth-84*scale,r.height);var defaultRect=new Rect(r.xMax-80*scale,r.y,80*scale,r.height);
            Track(new Rect(bindingRect.x+view.x,bindingRect.y+view.y-scroll.y,bindingRect.width,bindingRect.height));
            Track(new Rect(defaultRect.x+view.x,defaultRect.y+view.y-scroll.y,defaultRect.width,defaultRect.height));
            if(GUI.Button(bindingRect,WorldInputBindings.CaptureAction==action.id?"Press…":WorldInputBindings.Label(action.id),button))WorldInputBindings.BeginCapture(action.id);
            if(GUI.Button(defaultRect,"Default",button)){WorldInputBindings.CancelCapture();WorldInputBindings.Rebind(action.id,action.defaultKey);}
            GUI.enabled=previous;
        }
        GUI.EndScrollView();
        float bottom=view.yMax+6*scale;var noticeLabel=new GUIStyle(label);
        if(WorldInputBindings.Conflict)noticeLabel.normal.textColor=new Color(.7f,.13f,.08f);
        GUI.Label(new Rect(area.x,bottom,area.width,46*scale),WorldInputBindings.Notice,noticeLabel);
        if(Button(new Rect(area.x,bottom+50*scale,145*scale,30*scale),WorldInputBindings.Capturing?"Cancel binding":"Restore defaults",button)){
            if(WorldInputBindings.Capturing)WorldInputBindings.CancelCapture();else WorldInputBindings.Reset();
        }
        if(Button(new Rect(area.x+155*scale,bottom+50*scale,150*scale,30*scale),"Back to controls",button))ShowOverview();
        GUI.Label(new Rect(area.x,bottom+86*scale,area.width,30*scale),"RMB / Esc · Back",label);
    }
    static void DrawOverview(Rect area,float scale,GUIStyle label,GUIStyle button)
    {
        bool narrow=area.width<700*scale;
        float canvasHeight=narrow?810*scale:620*scale;
        var view=new Rect(area.x,area.y,area.width,Mathf.Max(100,area.height-52*scale));
        scroll=GUI.BeginScrollView(view,scroll,new Rect(0,0,area.width-22,canvasHeight));
        float width=view.width-30;float keyboardWidth=narrow?width:width*.76f;
        float unit=keyboardWidth/15.3f,h=unit*.72f,gap=unit*.09f;
        GUI.Label(new Rect(0,0,keyboardWidth,28*scale),"KEYBOARD",label);
        string[][] rows={
            new[]{"Esc","F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12"},
            new[]{"Tab","Q","W","E","R","T","Y","U","I","O","P","[","]"},
            new[]{"Caps","A","S","D","F","G","H","J","K","L",";","'","Enter"},
            new[]{"Shift","Z","X","C","V","B","N","M",",",".","/","Shift"},
            new[]{"Ctrl","Alt","Space","Alt","Ctrl"}
        };
        string hovered=null;
        for(int row=0;row<rows.Length;row++){
            float x=0,y=32*scale+row*(h+gap);
            foreach(var key in rows[row]){
                float w=unit;if(key=="Tab"||key=="Caps"||key=="Enter")w=unit*1.4f;if(key=="Shift")w=unit*1.7f;if(key=="Ctrl")w=unit*1.4f;if(key=="Space")w=unit*8;
                var r=new Rect(x,y,w,h);string actionText=Describe(key);var previous=GUI.color;
                GUI.color=actionText.Length>0?new Color(.8f,.73f,.48f):new Color(.82f,.83f,.78f);GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=previous;
                var keyLabel=new GUIStyle(label){alignment=TextAnchor.MiddleCenter,fontSize=Mathf.Max(9,Mathf.RoundToInt(unit*.27f)),wordWrap=false};GUI.Label(r,key,keyLabel);
                if(r.Contains(Event.current.mousePosition))hovered=actionText.Length>0?key+" · "+actionText:key;
                x+=w+gap;
            }
        }
        float mouseY=narrow?32*scale+5*(h+gap)+12*scale:0,mouseX=narrow?0:keyboardWidth+24*scale;
        GUI.Label(new Rect(mouseX,mouseY,width-mouseX,28*scale),"MOUSE",label);
        GUI.Label(new Rect(mouseX,mouseY+35*scale,width-mouseX,110*scale),
            "LMB · "+Describe("LMB")+"\nRMB · Dodge / hold run\nMenus · RMB back\nMove mouse · Look",label);
        float top=narrow?mouseY+155*scale:Mathf.Max(200*scale,32*scale+5*(h+gap));
        GUI.Label(new Rect(0,top,width,35*scale),hovered??"Highlighted keys are active. Hover a key for its action.",label);
        top+=42*scale;
        int columns=narrow?1:3;float columnWidth=width/columns;
        string[] headings={"MOVEMENT","COMBAT","WORLD & INTERFACE"};
        string[][] lines={
            new[]{"W A S D / Arrows · Move","Shift / RMB · Sprint (hold)","Space · Jump / let go"},
            new[]{WorldInputBindings.Label("Attack")+" · Attack / combo / counter",WorldInputBindings.Label("Guard")+" · Guard (hold)",WorldInputBindings.Label("Dodge")+" + direction · Dodge"},
            new[]{"E · Interact    H · Recover","B · Backpack    U · Equipment","M · Map    J · Tasks","R · Recenter    F1 · Controls","F8 · Return from side world"}
        };
        for(int c=0;c<3;c++){
            float x=narrow?0:c*columnWidth,y=top+(narrow?c*155*scale:0);
            GUI.Label(new Rect(x,y,columnWidth-12,28*scale),headings[c],label);
            GUI.Label(new Rect(x,y+32*scale,columnWidth-12,130*scale),string.Join("\n",lines[c]),label);
        }
        GUI.EndScrollView();
        float footer=area.yMax-40*scale;
        if(Button(new Rect(area.x,footer,175*scale,34*scale),"Edit bindings",button))BeginEditing();
        GUI.Label(new Rect(area.x+190*scale,footer,area.width-190*scale,38*scale),"RMB / Esc · Back     Ctrl + click · HUD icons",label);
    }
    static string Describe(string display)
    {
        if(display=="Esc")return "Menu / Back";if(display=="Ctrl")return "Click HUD icons";if(display=="RMB")return "Dodge / hold sprint · Back in menus";if(display=="Shift")return "Dodge on press / Sprint while held";
        string result="";foreach(var a in WorldKeymap.Actions)if(WorldInputBindings.Label(a.id)==display)result+=(result.Length>0?" / ":"")+a.label;
        return result;
    }
}
