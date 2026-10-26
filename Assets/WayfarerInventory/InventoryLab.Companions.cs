using UnityEngine;
namespace WayfarerInventory
{
    public sealed partial class InventoryLab
    {
        bool companionsOpen;
        public void ShowCompanions(){if(worldFlow&&!worldFlow.InventoryAvailable)return;if(controlsOpen)WorldInputBindings.CancelCapture();controlsOpen=false;open=false;characterOpen=false;companionsOpen=true;confirmingRecall=false;notice="";if(characterPreview)characterPreview.Show(false,false);SyncWorldLock();}
        void DrawCompanionsBody()
        {
            var rect=new Rect(leftColumn.x,leftColumn.y,footerRect.width,footerRect.y-leftColumn.y-16);Fill(rect,Surface);
            var paimon=player?PaimonCompanion.FindFor(player):null;var wardrobe=paimon?paimon.GetComponent<CompanionWardrobe>():null;
            GUI.Label(new Rect(rect.x,rect.y,rect.width,42),"TRAVELLING COMPANIONS",title);
            GUI.Label(new Rect(rect.x,rect.y+50,rect.width,55),"Choose one friend to accompany you. Switching does not change your character or equipment.",body);
            if(!wardrobe){GUI.Label(new Rect(rect.x,rect.y+130,rect.width,60),"Companions are preparing. Close and reopen the backpack after entering the world.",body);return;}
            float gap=28,w=(rect.width-gap)/2,y=rect.y+124;
            Card(new Rect(rect.x,y,w,252),CompanionWardrobe.Friend.Paimon,"PAIMON","A floating travelling friend. Independent gestures, expressions, star effects and gameplay reactions.");
            Card(new Rect(rect.x+w+gap,y,w,252),CompanionWardrobe.Friend.Amberwing,"NIAOLONG","A sleepy pillow spirit. Pillow hugs, wishes, soft turns and playful tosses.");
            if(ActionButton(new Rect(rect.x,y+284,360,48),"RECALL COMPANION"))wardrobe.Select(CompanionWardrobe.Friend.None);
            void Card(Rect r,CompanionWardrobe.Friend friend,string name,string note){bool equipped=wardrobe.Selected==friend;RoundFill(r,SlotFill,8);RoundFrame(r,equipped?Gold:Separator,8);GUI.Label(new Rect(r.x+24,r.y+20,r.width-48,44),name,itemName);GUI.Label(new Rect(r.x+24,r.y+78,r.width-48,94),note,body);if(ActionButton(new Rect(r.x+24,r.yMax-68,r.width-48,44),equipped?"EQUIPPED - RECALL":"EQUIP / SWITCH"))wardrobe.Toggle(friend);}
        }
    }
}
