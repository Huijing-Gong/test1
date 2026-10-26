using UnityEngine;
namespace WayfarerInventory
{
    public sealed partial class InventoryLab
    {
        bool controlsOpen; Texture2D settingsGear;
        public bool ControlsOpen=>controlsOpen;
        public void ShowControls(){if(worldFlow&&!worldFlow.InventoryAvailable)return;Close();WorldControlsPanel.ShowOverview();controlsOpen=true;SyncWorldLock();}
        void DrawSettingsLauncher(Rect r)
        {
            if(!settingsGear){
                settingsGear=new Texture2D(96,96,TextureFormat.RGBA32,false){name="Controls gear",hideFlags=HideFlags.HideAndDontSave};
                var pixels=new Color[96*96];
                for(int y=0;y<96;y++)for(int x=0;x<96;x++){
                    float dx=x-47.5f,dy=y-47.5f,d=Mathf.Sqrt(dx*dx+dy*dy),a=Mathf.Atan2(dy,dx);
                    float outer=Mathf.Cos(a*8)>.35f?43:35;
                    bool solid=(d<outer&&d>23)||(d<18&&d>11);pixels[y*96+x]=solid?Surface:Color.clear;
                }
                settingsGear.SetPixels(pixels);settingsGear.Apply();
            }
            bool hover=r.Contains(Event.current.mousePosition);var old=GUI.color;GUI.color=hover?Gold:Surface;
            GUI.DrawTexture(new Rect(r.center.x-26,r.center.y-26,52,52),settingsGear);GUI.color=old;
            if(!confirmingRecall&&GUI.Button(r,GUIContent.none,GUIStyle.none))ShowControls();
            if(hover){var tip=new Rect(r.xMax-280,r.yMax+8,280,48);Fill(tip,Surface);Border(tip,Gold);GUI.Label(Inset(tip,8),"Controls · "+WorldInputBindings.Label("Controls"),small);}
        }
        void DrawControls()
        {
            var p=panelRect;float x=p.x+40,y=p.y;
            RoundFill(p,Surface,12);RoundFrame(p,Gold,12);RoundFrame(Inset(p,4),Gold,8);
            RoundFill(new Rect(p.x+5,y+5,p.width-10,76),Ink,8);Fill(new Rect(p.x+5,y+69,p.width-10,12),Ink);
            GUI.Label(new Rect(x,y+16,900,48),WorldControlsPanel.Editing?"EDIT COMBAT BINDINGS":"CONTROLS",TextStyle(title,Surface));
            if(ActionButton(new Rect(p.xMax-80,y+20,40,40),"X"))Close();
            Fill(new Rect(p.x+4,y+82,p.width-8,2),Gold);
            WorldControlsPanel.Draw(new Rect(x,y+104,p.width-80,p.height-132),1.05f);
            WorldControlsPanel.RegisterBackButton(new Rect(p.xMax-80,y+20,40,40));
        }
    }
}
