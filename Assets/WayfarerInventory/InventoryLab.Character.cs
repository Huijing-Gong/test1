using System.Linq;
using UnityEngine;

namespace WayfarerInventory
{
    public sealed partial class InventoryLab
    {
        static readonly string[] Slots={"Main hand","Off hand","Relic","Necklace","Charm","Sigil"};
        void DrawCharacterBody()
        {
            GUI.Label(new Rect(leftColumn.x,leftColumn.y,280,32),"EQUIPMENT",TextStyle(button,Ink));
            OrnamentRule(new Rect(leftColumn.x,leftColumn.y+32,280,16));
            var entries=model.AvailableInSlot(selectedSlot);
            if(!entries.Any(e=>e.Id==selected))selected=entries.FirstOrDefault()?.Id ?? "";
            for(int i=0;i<entries.Count;i++)
            {
                var e=entries[i];var r=new Rect(leftColumn.x,leftColumn.y+48+i*(64+8),280,64);
                RoundFill(r,e.Id==selected?Ink:SlotFill);RoundFrame(r,e.Id==selected?Gold:Separator);RoundFrame(Inset(r,4),e.Id==selected?Gold:Surface,4);
                if(icons.TryGetValue(e.Id,out var icon)&&icon)
                    GUI.DrawTexture(new Rect(r.x+8,r.y+8,48,48),icon,ScaleMode.ScaleToFit);
                var nameStyle=TextStyle(subtitle,e.Id==selected?Surface:Ink);
                GUI.Label(new Rect(r.x+72,r.y,160,64),e.Name,nameStyle);
                if(e.Equipped)Check(new Vector2(r.xMax-24,r.center.y),22);
                if(GUI.Button(r,GUIContent.none,GUIStyle.none))selected=e.Id;
            }
            float modelHeight=middleColumn.height-64;
            GroundShadow(new Rect(middleColumn.center.x-160,middleColumn.y+modelHeight-64,320,48));
            if(characterPreview && characterPreview.Image)
                GUI.DrawTexture(new Rect(middleColumn.center.x-modelHeight*.4f,middleColumn.y,modelHeight*.8f,modelHeight),characterPreview.Image,ScaleMode.ScaleToFit,true);
            for(int i=0;i<6;i++)
            {
                string slot=Slots[i];float sx=i<3?middleColumn.x:middleColumn.xMax-80;
                float sy=middleColumn.y+48+(i%3)*144;
                var r=new Rect(sx,sy,80,80);
                Selectable(r,selectedSlot==slot);
                string equipped=model.EquippedInSlot(slot);
                if(equipped!="" && icons.TryGetValue(equipped,out var icon)&&icon)
                    GUI.DrawTexture(new Rect(sx+16,sy+16,48,48),icon,ScaleMode.ScaleToFit);
                else
                {
                    EmptySlotIcon(new Rect(sx+16,sy+16,48,48),i);
                }
                var label=new GUIStyle(tag){alignment=TextAnchor.MiddleCenter};
                GUI.Label(new Rect(sx-16,sy+88,112,24),slot,label);
                if(GUI.Button(r,GUIContent.none,GUIStyle.none))SelectSlot(slot);
            }
            float rotateY=middleColumn.yMax-48,cx=middleColumn.center.x;
            if(ActionButton(new Rect(cx-128,rotateY,56,48),"<"))characterPreview?.Rotate(-30);
            var centerStyle=new GUIStyle(subtitle){alignment=TextAnchor.MiddleCenter};
            GUI.Label(new Rect(cx-48,rotateY,96,48),"ROTATE",centerStyle);
            if(ActionButton(new Rect(cx+72,rotateY,56,48),">"))characterPreview?.Rotate(30);
        }
        static string SlotSilhouette(int i)
        {
            string[] names={"Icon_Eq_Weapon","Icon_Eq_Shield","Icon_Eq_Trinket","Icon_Eq_neck","Icon_Eq_Jewelery","Icon_Eq_ring"};
            return names[i];
        }
    }
}
