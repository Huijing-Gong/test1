using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WayfarerInventory
{
    // Shared presentation: the main world supplies authoritative saved state.
    [DefaultExecutionOrder(250)]
    public sealed partial class InventoryLab : MonoBehaviour
    {
        public Transform player;
        public BaselineFlow worldFlow;
        BaselineLoadout loadout;
        public bool IsOpen => open || characterOpen || controlsOpen || companionsOpen;
        readonly InventoryModel model = new InventoryModel();
        readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        static readonly string[] Ids = { "sword", "island", "ridge", "guard", "trialA", "trialB", "beacon" };
        static readonly string[] Names = { "Practice sword", "Island relic", "Ridge charm", "Runic guard", "Trial A blade core", "B gate sigil", "Recall beacon" };
        static readonly string[] Art = { "38_sword", "08_gem_green", "13_neck", "42_shield", "39_sword", "10_gem_purple", "03_Torch" };
        static readonly string[] Details = {
            "A dependable blade.", "Sword damage +2",
            "Sprint stamina -30%", "Sword damage +4",
            "Sword damage +6", "Sword damage +6",
            "Return to camp."
        };
        static readonly Color Surface = new Color32(247,242,225,255), Gold = new Color32(180,151,85,255),
            Ink = new Color32(24,62,53,255), Muted = new Color32(91,114,98,255);
        string category = "All", selected = "sword", notice = "";
        bool open;
        bool characterOpen;
        CharacterPreview characterPreview;
        bool confirmingRecall;
        GUIStyle title, body, small, button, subtitle, itemName, tag, description, rightText;
        Texture2D frame, bag, characterIcon;
        Vector3 start;
        CursorLockMode oldLock;
        bool oldVisible;

        void Start()
        {
            characterPreview=GetComponent<CharacterPreview>();
            oldLock = Cursor.lockState; oldVisible = Cursor.visible;
            if(!worldFlow){Cursor.lockState = CursorLockMode.None; Cursor.visible = true;}
            if(worldFlow && player)loadout=player.GetComponent<BaselineLoadout>();
            start = player ? player.position : Vector3.zero;
            for (int i=0;i<Ids.Length;i++)
            {
                if(!worldFlow)model.Grant(Ids[i], Names[i], i==6 ? "Supplies" : "Equipment", i==6 ? 3 : 1);
                icons[Ids[i]] = Resources.Load<Texture2D>("InventoryKit/"+Art[i]);
            }
            frame = Resources.Load<Texture2D>("InventoryKit/Icon_Frame");
            bag = Resources.Load<Texture2D>("InventoryKit/BagParcel");
            characterIcon = Resources.Load<Texture2D>("InventoryKit/CharacterEquipment");
        }
        void OnDisable() { if(settingsGear){Destroy(settingsGear);settingsGear=null;} if(worldFlow){Close();worldFlow.SetInventoryOpen(false);}else{Cursor.lockState=oldLock; Cursor.visible=oldVisible;} }
        public void Close(){if(controlsOpen)WorldControlsPanel.ShowOverview();controlsOpen=false;companionsOpen=false;open=false;characterOpen=false;confirmingRecall=false;if(characterPreview)characterPreview.Show(false,false);}
        public void ShowBag(){if(controlsOpen)WorldInputBindings.CancelCapture();controlsOpen=false;companionsOpen=false;open=true;characterOpen=false;confirmingRecall=false;}
        public void ShowCharacter(){OpenCharacter(selected);}
        public bool NavigateBack(){if(!IsOpen)return false;if(confirmingRecall)confirmingRecall=false;else if(controlsOpen&&WorldControlsPanel.TryBack()){}else Close();SyncWorldLock();return true;}
        void SyncWorld()
        {
            if(!worldFlow)return;
            var session=GameSession.Instance;
            model.SetOwned("sword",Names[0],"Equipment",session && session.HasSword?1:0,session && session.SwordEquipped);
            for(int i=0;i<5;i++)model.SetOwned(Ids[i+1],loadout?loadout.EquipmentName(i):Names[i+1],"Equipment",loadout && loadout.OwnsEquipment(i)?1:0,loadout && loadout.IsEquipped(i));
            model.SetOwned("beacon",Names[6],"Supplies",worldFlow.Progress.RecallBeaconsRemaining,false);
        }
        void SyncWorldLock(){if(worldFlow)worldFlow.SetInventoryOpen(IsOpen);}
        void LateUpdate(){SyncWorldLock();CompanionWardrobe.IsBagOpen=IsOpen;}
        void ToggleSelected()
        {
            if(!worldFlow){model.ToggleEquipment(selected);return;}
            int index=System.Array.IndexOf(Ids,selected);
            bool success=index==0?worldFlow.TogglePracticeSword():index>0 && index<6 && worldFlow.ToggleEquipment(index-1);
            notice=success?"":worldFlow.Status;SyncWorld();
        }
        string EquipmentEffect(int index)=>worldFlow && loadout?loadout.EquipmentEffect(index-1):Details[index];
        bool Recall()
        {
            if(!worldFlow)return model.Use("beacon",()=>{if(!player)return false;player.position=start;return true;});
            bool used=worldFlow.UseRecallBeacon();notice=worldFlow.Status;SyncWorld();return used;
        }
        void Update()
        {
            SyncWorld();
            if(worldFlow && !worldFlow.InventoryAvailable){Close();SyncWorldLock();return;}
            var k=Keyboard.current; if(k==null)return;
            if(WorldInputBindings.ConsumedInterfaceInput)return;
            bool bagPressed=WorldInputBindings.Pressed("Bag");
            if(bagPressed&&open){Close();SyncWorldLock();}
            else HandleShortcuts(bagPressed,WorldInputBindings.Pressed("Character"),k.escapeKey.wasPressedThisFrame);
            if(WorldInputBindings.Pressed("Controls"))ShowControls();
            if(characterPreview)characterPreview.Show(characterOpen,model.IsEquipped("sword"));
            SyncWorldLock();
            if(!worldFlow && !IsOpen && player)
            {
                var move=new Vector3((k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),0,(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0));
                player.position+=Vector3.ClampMagnitude(move,1)*Time.deltaTime*3;
                player.position=new Vector3(Mathf.Clamp(player.position.x,-3,3),start.y,Mathf.Clamp(player.position.z,-2,2));
            }
        }
        public void HandleShortcuts(bool bagPressed,bool characterPressed,bool closePressed)
        {
            if(worldFlow && !worldFlow.InventoryAvailable)return;
            if(bagPressed)ShowBag();
            if(characterPressed)ShowCharacter();
            if(closePressed)NavigateBack();
            SyncWorldLock();
        }
        void Styles()
        {
            if(title!=null)return;
            title=new GUIStyle(GUI.skin.label){font=Resources.Load<Font>("InventoryKit/LibreBaskerville"),fontSize=32,fontStyle=FontStyle.Bold};
            body=new GUIStyle(GUI.skin.label){fontSize=21,wordWrap=true};
            small=new GUIStyle(body){fontSize=18};
            button=new GUIStyle(title){fontSize=19,wordWrap=false};
            title.normal.textColor=body.normal.textColor=Ink; small.normal.textColor=Muted;
            button.normal.textColor=button.hover.textColor=button.active.textColor=button.focused.textColor=Surface;
            subtitle=new GUIStyle(small){fontSize=16};
            itemName=new GUIStyle(title){fontSize=28,wordWrap=true};
            tag=new GUIStyle(small){fontSize=14};
            description=new GUIStyle(body){fontSize=16};
            rightText=new GUIStyle(subtitle){alignment=TextAnchor.MiddleRight};
        }
        static void Fill(Rect r,Color c) { var old=GUI.color; GUI.color=c; GUI.DrawTexture(r,Texture2D.whiteTexture); GUI.color=old; }
        static void Border(Rect r,Color c)
        {
            Fill(new Rect(r.x,r.y,r.width,2),c);Fill(new Rect(r.x,r.yMax-2,r.width,2),c);
            Fill(new Rect(r.x,r.y,2,r.height),c);Fill(new Rect(r.xMax-2,r.y,2,r.height),c);
        }
    }
}
