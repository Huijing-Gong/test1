using UnityEngine;
public sealed partial class BaselineFlow
{
    void DrawP4DynamicControls(Rect image)
    {
        Color previous=GUI.color;
        float alpha=p4Closing?1f-Mathf.Clamp01((Time.unscaledTime-p4MotionStarted)/.18f):1f;
        Rect paper=P4Rect(image,820,194,588,588);
        for(int i=0;i<32;i++){
            Color color=Color.Lerp(new Color32(247,235,204,255),new Color32(253,243,216,255),i/31f);
            color.a=alpha;GUI.color=color;GUI.DrawTexture(new Rect(paper.x+i*paper.width/32f,paper.y,paper.width/32f+.5f,paper.height),Texture2D.whiteTexture);
        }
        GUI.color=new Color(1,1,1,alpha);
        float factor=image.width/1600f;
        var title=new GUIStyle(GUI.skin.label){fontSize=Mathf.Max(12,Mathf.RoundToInt(24*factor)),fontStyle=FontStyle.Bold};
        title.normal.textColor=new Color32(33,76,65,255);WayfarerUITheme.KeepLabelColor(title);
        GUI.Label(P4Rect(image,850,207,520,38),WorldControlsPanel.Editing?"EDIT COMBAT BINDINGS":"CONTROLS",title);
        bool enabled=GUI.enabled;GUI.enabled=enabled&&!p4Closing;
        WorldControlsPanel.Draw(P4Rect(image,850,254,530,505),Mathf.Max(.65f,factor));
        GUI.enabled=enabled;
        if(!p4BookmarkTop)p4BookmarkTop=Resources.Load<Texture2D>("P4Journal/BookmarkTop");
        if(p4BookmarkTop)GUI.DrawTexture(P4Rect(image,1324,112,55,177),p4BookmarkTop,ScaleMode.StretchToFill,true);
        GUI.color=previous;
    }
}
