using UnityEngine;
public sealed class CompanionWardrobe:MonoBehaviour
{
 public enum Friend { None,Paimon,Amberwing }
 public PaimonCompanion paimon;public AmberwingCompanion amber;public NiaolongCompanion niaolong;
 public Friend Selected {get;private set;}
 public static bool IsBagOpen;
 const string Key="Companions.SelectedFriend";
 public static CompanionWardrobe For(Transform target){var p=PaimonCompanion.FindFor(target);return p?p.GetComponent<CompanionWardrobe>():null;}
 void Start(){paimon=GetComponent<PaimonCompanion>();var prefab=Resources.Load<GameObject>("Companions/NiaolongCompanion");if(!prefab)return;var go=Instantiate(prefab);niaolong=go.GetComponent<NiaolongCompanion>();niaolong.Bind(paimon.target);Select((Friend)Mathf.Clamp(PlayerPrefs.GetInt(Key,paimon.Equipped?1:0),0,2));}
 public void Select(Friend value){Selected=value;if(paimon)paimon.SetEquipped(value==Friend.Paimon);if(niaolong)niaolong.SetEquipped(value==Friend.Amberwing);PlayerPrefs.SetInt(Key,(int)value);PlayerPrefs.Save();}
 public void Toggle(Friend value){Select(Selected==value?Friend.None:value);}
 public void Draw(Rect r){GUI.Box(r,"BACKPACK / COMPANIONS");var title=new GUIStyle(GUI.skin.label){fontSize=21,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter};var text=new GUIStyle(GUI.skin.label){wordWrap=true,alignment=TextAnchor.MiddleCenter,fontSize=13};
 GUI.Label(new Rect(r.x+20,r.y+36,r.width-40,30),"Choose your travelling friend",title);
 Card(new Rect(r.x+20,r.y+88,230,195),Friend.Paimon,"PAIMON","A lively floating friend\nGestures / expressions / star magic");Card(new Rect(r.x+270,r.y+88,230,195),Friend.Amberwing,"N-I-A-O-L-O-N-G","A sleepy little pillow spirit\nPillow hugs / wishes / happy hops");
 if(GUI.Button(new Rect(r.x+130,r.y+301,260,32),"Recall companion"))Select(Friend.None);GUI.Label(new Rect(r.x+20,r.y+342,r.width-40,25),"B to close  /  Click your friend to circle",text);
 void Card(Rect c,Friend f,string name,string description){GUI.Box(c,"");GUI.Label(new Rect(c.x+8,c.y+14,c.width-16,28),name,title);GUI.Label(new Rect(c.x+13,c.y+55,c.width-26,65),description,text);if(GUI.Button(new Rect(c.x+15,c.y+135,c.width-30,42),Selected==f?"Equipped - recall":"Equip / switch"))Toggle(f);}}
 void OnDestroy(){if(amber)Destroy(amber.gameObject);if(niaolong)Destroy(niaolong.gameObject);IsBagOpen=false;}
}
