using System;
using System.Linq;
using UnityEngine;

namespace WayfarerInventory
{
    public sealed partial class InventoryLab
    {
        // Shared layout in a 1920 x 1080 reference space. HUD exclusion wins over centering.
        const float PanelWidth=1440, PanelHeight=860, Padding=40, Gap=32;
        static readonly Color SlotFill=new Color32(239,234,216,255);
        static readonly Color SelectedFill=new Color32(226,224,195,255);
        static readonly Color Separator=new Color32(187,193,167,255);
        Rect panelRect,leftColumn,middleColumn,rightColumn,footerRect;
        string selectedSlot="Main hand";
        float referenceWidth;

        void OpenCharacter(string id)
        {
            if(controlsOpen)WorldInputBindings.CancelCapture();controlsOpen=false;companionsOpen=false;open=false;characterOpen=true;confirmingRecall=false;
            string slot=InventoryModel.SlotFor(id);
            if(slot=="") { selectedSlot="Main hand"; selected=model.AvailableInSlot(selectedSlot).FirstOrDefault()?.Id ?? ""; }
            else {selected=id;selectedSlot=slot;}
        }
        void SelectSlot(string slot)
        {
            selectedSlot=slot;
            selected=model.EquippedInSlot(slot);
            if(selected=="")selected=model.AvailableInSlot(slot).FirstOrDefault()?.Id ?? "";
        }
        static void ThinBorder(Rect r,Color color)
        {
            Fill(new Rect(r.x,r.y,r.width,1),color);Fill(new Rect(r.x,r.yMax-1,r.width,1),color);
            Fill(new Rect(r.x,r.y,1,r.height),color);Fill(new Rect(r.xMax-1,r.y,1,r.height),color);
        }
        void Selectable(Rect r,bool selectedState)
        {
            RoundFill(r,selectedState?SelectedFill:SlotFill,6);
            RoundFrame(r,selectedState?Gold:Separator,6);
            RoundFrame(Inset(r,4),selectedState?Gold:Surface,4);
        }
        bool Launcher(Rect r,Texture icon,string caption)
        {
            bool hover=!confirmingRecall && r.Contains(Event.current.mousePosition);
            if(icon)
            {
                // Fit visible pixels, not the unequal transparent margins of the source art.
                Rect uv=icon==bag?new Rect(328f/1254,300f/1254,598f/1254,627f/1254):new Rect(201f/1254,107f/1254,931f/1254,1040f/1254);
                float aspect=uv.width*icon.width/(uv.height*icon.height);
                float h=64,w=h*aspect;if(w>64){h*=64/w;w=64;}
                GUI.DrawTextureWithTexCoords(new Rect(r.center.x-w/2,r.center.y-h/2,w,h),icon,uv,true);
            }
            bool hit=!confirmingRecall && GUI.Button(r,GUIContent.none,GUIStyle.none);
            if(hover)
            {
                var tip=new Rect(Mathf.Min(r.x,referenceWidth-264),r.yMax+8,240,48);
                Fill(tip,Surface);Border(tip,Gold);
                GUI.Label(new Rect(tip.x+16,tip.y+8,208,32),caption,small);
            }
            return hit;
        }
        void OnGUI()
        {
            if(worldFlow && !worldFlow.InventoryAvailable)return;
            Styles();
            var matrix=GUI.matrix;var enabled=GUI.enabled;var background=GUI.backgroundColor;
            GUI.backgroundColor=new Color32(24,62,53,255);
            // Fit the entire HUD + window on small/wide screens.
            float scale=Mathf.Min(Screen.width/1920f,Screen.height/1080f);
            GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
            float width=Screen.width/scale,height=Screen.height/scale;
            referenceWidth=width;
            var bagRect=new Rect(width-240,24,80,80);
            var settingsRect=new Rect(width-128,24,80,80);
            var characterRect=new Rect(width-352,24,80,80);
            if(IsOpen)
            {
                Fill(new Rect(0,0,width,height),new Color(.035f,.08f,.065f,.64f));
                panelRect=new Rect((width-PanelWidth)/2,Mathf.Max((height-PanelHeight)/2,bagRect.yMax+24),PanelWidth,PanelHeight);
                GUI.enabled=!confirmingRecall;
                if(controlsOpen)DrawControls();
                else {
                DrawSkeleton();
                if(companionsOpen)DrawCompanionsBody();else {if(characterOpen)DrawCharacterBody();else DrawInventoryBody();DrawDetails();}
                DrawFooter();
                }
                GUI.enabled=enabled;
            }
            else if(!worldFlow)GUI.Label(new Rect(32,32,400,32),"WASD  Move",small);
            // HUD stays above the overlay, and outside the panel's bounds.
            if(Launcher(characterRect,characterIcon,"Character · "+WorldInputBindings.Label("Character")))OpenCharacter(selected);
            if(Launcher(bagRect,bag,"Bag · "+WorldInputBindings.Label("Bag")))ShowBag();
            DrawSettingsLauncher(settingsRect);
            if(confirmingRecall)DrawRecallConfirmation(width,height);
            GUI.enabled=enabled;GUI.matrix=matrix;GUI.backgroundColor=background;
            SyncWorldLock();
        }
        void DrawSkeleton()
        {
            RoundFill(panelRect,Surface,12);RoundFrame(panelRect,Gold,12);RoundFrame(Inset(panelRect,4),Gold,8);
            float x=panelRect.x+Padding,y=panelRect.y,w=panelRect.width-Padding*2;
            RoundFill(new Rect(panelRect.x+5,y+5,panelRect.width-10,76),Ink,8);
            Fill(new Rect(panelRect.x+5,y+69,panelRect.width-10,12),Ink);
            GUI.Label(new Rect(x,y+16,w-96,48),companionsOpen?"COMPANIONS":characterOpen?"CHARACTER":"INVENTORY",TextStyle(title,Surface));
            if(ActionButton(new Rect(panelRect.xMax-Padding-40,y+20,40,40),"X"))Close();
            Fill(new Rect(panelRect.x+4,y+82,panelRect.width-8,2),Gold);
            float tabsY=y+100;
            string[] tabs={"BAG","CHARACTER","COMPANIONS"};
            for(int i=0;i<3;i++)
            {
                var r=new Rect(x+i*(272+16),tabsY,272,52);
                bool active=i==2?companionsOpen:i==1?characterOpen:!characterOpen&&!companionsOpen;
                RoundFill(r,active?Ink:SlotFill,6);RoundFrame(r,active?Gold:Separator,6);
                GUI.Label(r,tabs[i],TextStyle(button,active?Surface:Muted,TextAnchor.MiddleCenter));
                if(active){Fill(new Rect(r.x,r.yMax-5,r.width,5),Gold);Diamond(new Vector2(r.center.x,r.yMax),7,Gold);}
                if(GUI.Button(r,GUIContent.none,GUIStyle.none))
                {
                    if(i==2)ShowCompanions();else if(i==1)OpenCharacter(selected);else ShowBag();
                }
            }
            Fill(new Rect(x,tabsY+52,w,1),Separator);
            footerRect=new Rect(x,panelRect.yMax-16-48,w,48);
            float bodyY=tabsY+52+24, bodyHeight=footerRect.y-bodyY-16;
            leftColumn=new Rect(x,bodyY,280,bodyHeight);
            rightColumn=new Rect(x+w-400,bodyY,400,bodyHeight);
            middleColumn=new Rect(leftColumn.xMax+Gap,bodyY,rightColumn.x-leftColumn.xMax-Gap*2,bodyHeight);
            Fill(new Rect(leftColumn.xMax+16,bodyY,1,bodyHeight),Separator);
            Fill(new Rect(middleColumn.xMax+16,bodyY,1,bodyHeight),Separator);
        }
        void DrawFooter()
        {
            Fill(new Rect(footerRect.x,footerRect.y,footerRect.width,1),Gold);
            string hint=notice!=""?notice:companionsOpen?"Equip or recall a travelling companion.":characterOpen?"Select equipment to view details.":"Select an item to view details.";
            GUI.Label(new Rect(footerRect.x,footerRect.y+16,footerRect.width-320,32),hint,subtitle);
            GUI.Label(new Rect(footerRect.xMax-304,footerRect.y+16,304,32),"RMB / ESC  BACK",rightText);
        }
        void DrawInventoryBody()
        {
            string[] categories={"All","Equipment","Supplies"};
            for(int i=0;i<categories.Length;i++)
            {
                string cat=categories[i];
                var r=new Rect(leftColumn.x,leftColumn.y+i*(48+16),280,48);
                RoundFill(r,category==cat?Ink:SlotFill);RoundFrame(r,category==cat?Gold:Separator);
                GUI.Label(new Rect(r.x+16,r.y+8,192,32),cat,TextStyle(body,category==cat?Surface:Ink));
                GUI.Label(new Rect(r.xMax-56,r.y+8,40,32),model.VisibleCount(cat).ToString(),TextStyle(rightText,category==cat?Surface:Muted,TextAnchor.MiddleRight));
                if(GUI.Button(r,GUIContent.none,GUIStyle.none))category=cat;
            }
            var entries=model.Visible(category).ToList();
            if(!entries.Any(e=>e.Id==selected))selected=entries.FirstOrDefault()?.Id ?? "";
            GUI.Label(new Rect(middleColumn.x,middleColumn.y,middleColumn.width,32),category.ToUpper()+" \u00b7 "+entries.Count+" / 24",body);
            // Explicit requested 12px grid gap is the sole grid-spacing exception.
            float gridScale=Mathf.Min(1,middleColumn.width/636f),cell=96*gridScale,gap=12*gridScale;
            float startX=middleColumn.center.x-(6*cell+5*gap)/2,startY=middleColumn.y+48;
            for(int i=0;i<24;i++)
            {
                var r=new Rect(startX+(i%6)*(cell+gap),startY+(i/6)*(cell+gap),cell,cell);
                var entry=i<entries.Count?entries[i]:null;
                Selectable(r,entry!=null && selected==entry.Id);
                if(entry==null)continue;
                if(icons.TryGetValue(entry.Id,out var icon)&&icon)
                    GUI.DrawTexture(new Rect(r.x+16,r.y+16,r.width-32,r.height-32),icon,ScaleMode.ScaleToFit);
                if(entry.Equipped)Check(new Vector2(r.x+16,r.y+16),18);
                if(entry.Category=="Supplies")
                    GUI.Label(new Rect(r.xMax-48,r.yMax-32,40,24),"x"+entry.Quantity,rightText);
                if(GUI.Button(r,GUIContent.none,GUIStyle.none))selected=entry.Id;
            }
        }
        void DrawDetails()
        {
            RoundFrame(rightColumn,Separator,10);
            Rect content=Inset(rightColumn,24);
            var entry=model.Visible("All").FirstOrDefault(e=>e.Id==selected);
            if(entry==null)
            {
                GUI.Label(new Rect(content.x,content.y,content.width,40),characterOpen?selectedSlot:"No items",itemName);
                if(characterOpen)GUI.Label(new Rect(content.x,content.y+56,content.width,48),"No equipment",description);
                return;
            }
            int index=Array.IndexOf(Ids,entry.Id);
            float x=content.x,y=content.y,w=content.width;
            var iconBox=new Rect(x+(w-128)/2,y,128,128);
            RoundFill(iconBox,SlotFill);RoundFrame(iconBox,Gold);RoundFrame(Inset(iconBox,5),Gold,4);
            if(icons.TryGetValue(entry.Id,out var icon)&&icon)GUI.DrawTexture(new Rect(iconBox.x+16,iconBox.y+16,96,96),icon,ScaleMode.ScaleToFit);
            GUI.Label(new Rect(x,y+144,w,72),entry.Name,itemName);
            string slot=InventoryModel.SlotFor(entry.Id);
            string kind=entry.Category=="Supplies"?"Consumable":slot=="Main hand"?"Weapon":"Equipment";
            GUI.Label(new Rect(x,y+216,w,24),slot==""?kind:kind+" \u00b7 "+slot,tag);
            if(entry.Equipped)
            {
                var badge=new Rect(x,y+248,152,32);Fill(badge,Mint);Check(new Vector2(x+18,y+264),16);
                GUI.Label(new Rect(x+36,y+248,112,32),"EQUIPPED",TextStyle(tag,Ink));
            }
            OrnamentRule(new Rect(x,y+296,w,16));
            string shortDescription=entry.Id=="beacon"?"Return to camp.":"";
            DrawDescription(new Rect(x,y+328,w,48),shortDescription);
            if(index>=1 && index<=5)GUI.Label(new Rect(x,y+376,w,40),EquipmentEffect(index),description);
            if(characterOpen)
            {
                string currentId=model.EquippedInSlot(selectedSlot);
                int currentIndex=Array.IndexOf(Ids,currentId);
                string current=currentIndex>=0?Names[currentIndex]:"Empty";
                if(currentId!="" && currentId!=entry.Id)
                    GUI.Label(new Rect(x,y+424,w,24),"Equipped: "+current,subtitle);
            }
            float buttonY=content.yMax-56;
            string state=entry.Category=="Supplies"?"Owned: "+entry.Quantity:"";
            GUI.Label(new Rect(x,buttonY-32,w,24),state,subtitle);
            string action=characterOpen?(entry.Equipped?"UNEQUIP":"EQUIP"):
                entry.Category=="Supplies"?"USE":"GO TO CHARACTER";
            if(ActionButton(new Rect(x,buttonY,w,56),action))
            {
                if(characterOpen)ToggleSelected();
                else if(entry.Category=="Supplies")confirmingRecall=true;
                else OpenCharacter(selected);
            }
        }
        void DrawDescription(Rect r,string text)
        {
            // Explicit 24px line step for 16px description text (1.5 line height).
            string line="";float y=r.y;
            foreach(string word in text.Split(' '))
            {
                string candidate=line==""?word:line+" "+word;
                if(line!="" && description.CalcSize(new GUIContent(candidate)).x>r.width)
                {GUI.Label(new Rect(r.x,y,r.width,24),line,description);y+=24;line=word;}
                else line=candidate;
            }
            if(line!="")GUI.Label(new Rect(r.x,y,r.width,24),line,description);
        }
        void DrawRecallConfirmation(float width,float height)
        {
            Fill(new Rect(0,0,width,height),new Color(0,0,0,.65f));
            var r=new Rect(width/2-280,height/2-144,560,288);
            Fill(r,Surface);Border(r,Gold);
            GUI.Label(new Rect(r.x+32,r.y+32,496,48),"RETURN TO CAMP?",title);
            GUI.Label(new Rect(r.x+32,r.y+96,496,48),"Uses 1 beacon.",body);
            if(ActionButton(new Rect(r.x+32,r.yMax-80,232,48),"CANCEL"))confirmingRecall=false;
            if(ActionButton(new Rect(r.x+296,r.yMax-80,232,48),"RETURN"))
            {
                bool used=Recall();
                if(!worldFlow)notice=used?"Returned to camp. One beacon used.":"Unable to return. No beacon used.";
                confirmingRecall=false;
            }
        }
    }
}
