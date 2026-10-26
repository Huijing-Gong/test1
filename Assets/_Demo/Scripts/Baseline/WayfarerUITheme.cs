using UnityEngine;
using System.Collections.Generic;

/// Shared visual language for the runtime HUD: deep green panels, warm paper
/// highlights, teal edges and restrained gold interaction states.
public static class WayfarerUITheme
{
    static GUISkin skin;
    static Texture2D panel, page, button, hover, active;
    static GUIStyle title, eyebrow, journalTitle, journalEyebrow, openingTitle, openingStatus, caption, paperBand, selectedButton, marker, controlKeys, controlText, noticePanel, noticeText;
    static bool journalPanel;
    public static readonly Color Paper = new Color32(245,233,198,255);
    public static readonly Color Gold = new Color32(186,155,87,255);
    public static readonly Color Ink = new Color32(23,57,50,255);
    public static GUIStyle JournalBodyStyle => controlText;
    public static GUIStyle NoticePanelStyle => noticePanel;
    public static GUIStyle NoticeTextStyle => noticeText;
    public static void KeepLabelColor(GUIStyle style)
    {
        Color color = style.normal.textColor;
        style.hover.textColor = style.active.textColor = style.focused.textColor = color;
        style.onNormal.textColor = style.onHover.textColor = style.onActive.textColor = style.onFocused.textColor = color;
    }
    sealed class Motion { public float value, time; public int frame = -1; }
    static readonly Dictionary<string,Motion> motions = new Dictionary<string,Motion>();
    static Matrix4x4 savedMatrix;
    static Color savedColor;
    static bool savedEnabled;
    static Color savedRevealColor;
    static bool savedRevealEnabled;

    public static void Apply()
    {
        if (skin == null) Build();
        GUI.skin = skin;
    }

    static void Build()
    {
        skin = Object.Instantiate(GUI.skin);
        panel = CutPanel(new Color32(20,49,43,248),Gold,true);
        page = Solid(new Color32(246,235,205,255));
        button = CutPanel(new Color32(28,58,50,255),new Color32(67,94,76,255),false);
        hover = CutPanel(new Color32(48,77,60,255),Gold,false);
        active = CutPanel(new Color32(210,183,122,255),Paper,false);
        Style(skin.box, panel, Paper, 20);
        Style(skin.window, panel, Paper, 20);
        Style(skin.button, button, Paper, 16);
        skin.button.padding = new RectOffset(24,16,5,5);
        skin.button.margin = new RectOffset(0,0,4,4);
        skin.button.alignment = TextAnchor.MiddleLeft;
        skin.button.hover.background = hover; skin.button.active.background = active;
        skin.button.hover.textColor = Paper; skin.button.active.textColor = Ink;
        skin.button.focused.background = hover; skin.button.focused.textColor = Paper;
        Style(skin.label, null, new Color32(238, 235, 215, 255), 0);
        skin.label.wordWrap = true;
        skin.label.padding = new RectOffset(0,0,3,3);
        title = new GUIStyle(skin.label) { fontSize=28,fontStyle=FontStyle.Bold };
        title.normal.textColor=Paper;
        eyebrow = new GUIStyle(skin.label) { fontSize=13,fontStyle=FontStyle.Bold };
        eyebrow.normal.textColor=Gold;
        journalTitle = new GUIStyle(title); journalTitle.normal.textColor=Ink;
        journalEyebrow = new GUIStyle(eyebrow); journalEyebrow.normal.textColor=new Color32(128,96,43,255);
        openingTitle = new GUIStyle(title) { fontSize=32 };
        openingStatus = new GUIStyle(skin.label) { fontSize=16,wordWrap=true };
        openingStatus.normal.textColor=new Color32(230,224,204,255);
        caption = new GUIStyle(skin.label) { fontSize=14 };
        caption.normal.textColor=Paper;
        paperBand=new GUIStyle(skin.box);
        paperBand.normal.background=hover;
        selectedButton=new GUIStyle(skin.button);
        selectedButton.normal.background=active;
        selectedButton.hover.background=active;
        selectedButton.active.background=active;
        selectedButton.normal.textColor=Ink;
        selectedButton.hover.textColor=Ink;
        selectedButton.active.textColor=Ink;
        marker=new GUIStyle(eyebrow); marker.normal.textColor=Gold;
        controlKeys=new GUIStyle(skin.label){fontSize=20,fontStyle=FontStyle.Bold,wordWrap=true};
        controlKeys.normal.textColor=Ink;
        controlText=new GUIStyle(skin.label){fontSize=19,wordWrap=true};
        controlText.normal.textColor=new Color32(48,69,58,255);
        noticePanel=new GUIStyle(skin.box);
        noticePanel.normal.background=CutPanel(new Color32(18,49,43,184),new Color32(186,155,87,210),true);
        noticeText=new GUIStyle(skin.label){fontSize=18,wordWrap=true,alignment=TextAnchor.MiddleCenter};
        noticeText.normal.textColor=Paper;
        // Non-interactive surfaces must never inherit the stock skin's hover state.
        Freeze(skin.box); Freeze(skin.window); Freeze(skin.label);
        Freeze(title); Freeze(eyebrow); Freeze(journalTitle); Freeze(journalEyebrow); Freeze(openingTitle); Freeze(openingStatus); Freeze(caption); Freeze(paperBand); Freeze(selectedButton); Freeze(marker); Freeze(controlKeys); Freeze(controlText); Freeze(noticePanel); Freeze(noticeText);
        ConfigureScrollbars();
        skin.horizontalSlider.normal.background = Solid(new Color32(24, 75, 66, 255));
        skin.horizontalSliderThumb.normal.background = Solid(new Color32(218, 177, 78, 255));
    }

    static void Freeze(GUIStyle style)
    {
        foreach(var state in new[]{style.hover,style.active,style.focused,style.onNormal,style.onHover,style.onActive,style.onFocused})
        {
            state.background=style.normal.background;
#if UNITY_EDITOR
            state.scaledBackgrounds=style.normal.scaledBackgrounds;
#endif
            state.textColor=style.normal.textColor;
        }
    }

    static void ConfigureScrollbars()
    {
        // A seven-unit flat rail with no chunky end arrows. Wheel/drag still use
        // Unity's built-in scroll handling and the original content bounds.
        var rail=Solid(new Color32(38,65,54,255));
        var thumb=Solid(new Color32(148,139,92,255));
        var thumbHover=Solid(new Color32(186,168,112,255));
        skin.verticalScrollbar=new GUIStyle { fixedWidth=7, margin=new RectOffset(9,0,3,3) };
        skin.verticalScrollbar.normal.background=rail; Freeze(skin.verticalScrollbar);
        skin.verticalScrollbarThumb=new GUIStyle { fixedWidth=7, padding=new RectOffset(0,0,12,12) };
        skin.verticalScrollbarThumb.normal.background=thumb; Freeze(skin.verticalScrollbarThumb);
        skin.verticalScrollbarThumb.hover.background=thumbHover;
        skin.verticalScrollbarThumb.active.background=thumbHover;
        skin.verticalScrollbarUpButton=new GUIStyle { fixedWidth=7,fixedHeight=0 };
        skin.verticalScrollbarDownButton=new GUIStyle { fixedWidth=7,fixedHeight=0 };
        skin.horizontalScrollbar=new GUIStyle { fixedHeight=7,margin=new RectOffset(3,3,9,0) };
        skin.horizontalScrollbar.normal.background=rail; Freeze(skin.horizontalScrollbar);
        skin.horizontalScrollbarThumb=new GUIStyle { fixedHeight=7,padding=new RectOffset(12,12,0,0) };
        skin.horizontalScrollbarThumb.normal.background=thumb; Freeze(skin.horizontalScrollbarThumb);
        skin.horizontalScrollbarThumb.hover.background=thumbHover;
        skin.horizontalScrollbarThumb.active.background=thumbHover;
        skin.horizontalScrollbarLeftButton=new GUIStyle { fixedWidth=0,fixedHeight=7 };
        skin.horizontalScrollbarRightButton=new GUIStyle { fixedWidth=0,fixedHeight=7 };
    }

    static void Style(GUIStyle style, Texture2D bg, Color color, int border)
    {
        if (bg != null) { style.normal.background = bg; style.hover.background = bg; }
        style.normal.textColor = color; style.hover.textColor = color; style.fontSize = Mathf.Max(style.fontSize, 16);
        style.border = new RectOffset(border, border, border, border);
        style.padding = new RectOffset(12, 12, 7, 7);
    }

    static Texture2D Solid(Color color)
    {
        var t = new Texture2D(1, 1, TextureFormat.RGBA32, false); t.name = "Wayfarer UI theme swatch";
        t.SetPixel(0, 0, color); t.Apply(); Object.DontDestroyOnLoad(t); return t;
    }

    // Transform only the UI panel. Motion continues while gameplay is paused.
    public static bool BeginPanel(string key, Rect rect, bool visible, float paperSplit = 0)
    {
        Apply();
        if(!motions.TryGetValue(key,out var m)) motions[key]=m=new Motion();
        if(m.frame!=Time.frameCount)
        {
            if(m.frame>=0 && Time.frameCount-m.frame>2)m.value=0;
            float dt=m.frame<0?0:Mathf.Min(Time.unscaledTime-m.time,.05f);
            float enterDuration=key=="opening-menu"?.42f:.24f;
            m.value=Mathf.MoveTowards(m.value,visible?1:0,dt/(visible?enterDuration:.16f));
            m.frame=Time.frameCount; m.time=Time.unscaledTime;
        }
        if(m.value<=0 && !visible)return false;
        savedMatrix=GUI.matrix; savedColor=GUI.color; savedEnabled=GUI.enabled;
        float ease=1-Mathf.Pow(1-m.value,3);
        GUI.matrix=savedMatrix*Matrix4x4.Translate(new Vector3(0,(1-ease)*(key=="opening-menu"?34f:18f),0));
        GUI.color=new Color(savedColor.r,savedColor.g,savedColor.b,savedColor.a*m.value);
        GUI.enabled=savedEnabled && visible && m.value>.65f;
        GUI.Box(rect,GUIContent.none);
        journalPanel=paperSplit>0 || key=="journey-menu";
        if(journalPanel) DrawJournalPages(rect,paperSplit>0?paperSplit:.36f);
        return true;
    }
    public static void EndPanel()
    { GUI.matrix=savedMatrix; GUI.color=savedColor; GUI.enabled=savedEnabled; journalPanel=false; }

    public static void Header(Rect r,string name,string section)
    {
        Apply();
        float labelWidth=journalPanel?Mathf.Min(r.width-56,r.width*.36f-32):r.width-56;
        GUI.Label(new Rect(r.x+28,r.y+20,labelWidth,19),section,journalPanel?journalEyebrow:eyebrow);
        GUI.Label(new Rect(r.x+28,r.y+43,labelWidth,42),name,journalPanel?journalTitle:title);
        Rule(new Rect(r.x+28,r.y+105,r.width-56,1));
    }
    public static void OpeningHeader(Rect r,string status)
    {
        Apply();
        float x=r.x+36;
        BeginOpeningReveal(.02f,.1f);
        GUI.Label(new Rect(x,r.y+30,r.width-88,20),"THE WAYFARER'S JOURNAL",eyebrow);
        EndOpeningReveal();
        BeginOpeningReveal(.05f,.12f);
        GUI.Label(new Rect(x,r.y+52,r.width-72,42),"Lake Domain",openingTitle);
        EndOpeningReveal();
        BeginOpeningReveal(.1f,.13f);
        GUI.Label(new Rect(x,r.y+96,r.width-72,26),status,openingStatus);
        EndOpeningReveal();
        BeginOpeningReveal(.12f,.12f);
        Rule(new Rect(x,r.y+138,r.width-72,1));
        EndOpeningReveal();
    }
    public static void BeginOpeningReveal(float delay,float duration=.16f)
    {
        savedRevealColor=GUI.color; savedRevealEnabled=GUI.enabled;
        float progress=0;
        if(motions.TryGetValue("opening-menu",out var motion))
        {
            float elapsed=motion.value*.42f;
            float t=Mathf.Clamp01((elapsed-delay)/Mathf.Max(.01f,duration));
            progress=t*t*(3-2*t);
        }
        GUI.color=new Color(GUI.color.r,GUI.color.g,GUI.color.b,GUI.color.a*progress);
        if(progress<.94f) GUI.enabled=false;
    }
    public static void EndOpeningReveal()
    { GUI.color=savedRevealColor; GUI.enabled=savedRevealEnabled; }
    public static void Footer(Rect r,string text)
    {
        Rule(new Rect(r.x+28,r.yMax-39,r.width-56,1));
        GUI.Label(new Rect(r.x+30,r.yMax-31,r.width-60,22),text,caption);
    }
    public static void Section(string text)
    { GUILayout.Space(8); GUILayout.Label("◆  "+text,journalPanel?journalEyebrow:eyebrow); GUILayout.Space(3); }
    public static bool Button(string text,params GUILayoutOption[] options)
    {
        Rect r=GUILayoutUtility.GetRect(new GUIContent(text),skin.button,options);
        bool hit=GUI.Button(r,text);
        if(Event.current.type==EventType.Repaint)
        {
            bool over=r.Contains(Event.current.mousePosition)&&GUI.enabled;
            float offset=over?2*Mathf.Sin(Time.unscaledTime*5):0;
            GUI.Label(new Rect(r.x+7+offset,r.y+(r.height-22)/2,18,22),over?"›":"·",marker);
        }
        return hit;
    }
    public static bool TabButton(string text,bool selected,params GUILayoutOption[] options)
    {
        var style=selected?selectedButton:skin.button;
        Rect r=GUILayoutUtility.GetRect(new GUIContent(text),style,options);
        bool hit=GUI.Button(r,text,style);
        return hit;
    }
    public static void ControlKeys(string text){GUILayout.Label(text,controlKeys);}
    public static void ControlText(string text){GUILayout.Label(text,controlText);}
    public static void DrawJournalPages(Rect r, float split=.36f)
    {
        // P4's open-journal silhouette: two quiet parchment leaves, a narrow
        // stitched spine, and the original dark-green cover framing them.
        float top=r.y+12, bottom=r.yMax-49, left=r.x+12, width=r.width-24;
        float leftWidth=width*Mathf.Clamp(split,.25f,.70f), gutter=12;
        Rect leftPage=new Rect(left,top,leftWidth,bottom-top);
        Rect rightPage=new Rect(left+leftWidth+gutter,top,width-leftWidth-gutter,bottom-top);
        GUI.DrawTexture(leftPage,page,ScaleMode.StretchToFill);
        GUI.DrawTexture(rightPage,page,ScaleMode.StretchToFill);
        Color previous=GUI.color;
        GUI.color=new Color32(185,158,106,100);
        GUI.DrawTexture(new Rect(leftPage.x+10,leftPage.y+18,leftPage.width-20,1),Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rightPage.x+10,rightPage.y+18,rightPage.width-20,1),Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(leftPage.x+10,leftPage.yMax-18,leftPage.width-20,1),Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rightPage.x+10,rightPage.yMax-18,rightPage.width-20,1),Texture2D.whiteTexture);
        GUI.color=new Color32(166,137,82,180);
        GUI.DrawTexture(new Rect(left+leftWidth+4,top+8,1,bottom-top-16),Texture2D.whiteTexture);
        GUI.color=new Color32(255,249,225,125);
        GUI.DrawTexture(new Rect(left+leftWidth+6,top+8,1,bottom-top-16),Texture2D.whiteTexture);
        GUI.color=previous;
    }
    static void Rule(Rect r)
    { Color c=GUI.color; GUI.color*=Gold; GUI.DrawTexture(r,Texture2D.whiteTexture); GUI.color=c; }

    // Tiny shared nine-slice textures, with antialiased cut corners and gold keylines.
    static Texture2D CutPanel(Color fill,Color edge,bool doubleLine)
    {
        const int size=80;
        var t=new Texture2D(size,size,TextureFormat.RGBA32,false) {name="Wayfarer cut-edge UI",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
        var pixels=new Color[size*size];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            Color sum=Color.clear;
            for(int sy=0;sy<2;sy++)for(int sx=0;sx<2;sx++)
            {
                float px=x+.25f+sx*.5f,py=y+.25f+sy*.5f;
                float d=Mathf.Min(Mathf.Min(px,py),Mathf.Min(size-px,size-py));
                d=Mathf.Min(d,(px+py-12)*.7071f);
                d=Mathf.Min(d,(2*size-px-py-12)*.7071f);
                Color c=d<0?Color.clear:d<1.5f?edge:doubleLine&&d>4&&d<5?Color.Lerp(fill,edge,.4f):fill;
                sum+=c*.25f;
            }
            pixels[y*size+x]=sum;
        }
        t.SetPixels(pixels);t.Apply(false,true);return t;
    }
}
