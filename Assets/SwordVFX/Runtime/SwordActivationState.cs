using UnityEngine;
namespace KingOfGraphic.SwordVFX {
 public sealed class SwordActivationState {
  public float Reveal {get;private set;}
  public float HoldRemaining {get;private set;}
  bool attacking;
  public void BeginAttack(){attacking=true;HoldRemaining=5;}
  public void EndAttack(){if(!attacking)return;attacking=false;HoldRemaining=5;}
  public void Reset(){attacking=false;HoldRemaining=0;Reveal=0;}
  public void Tick(float dt){
   if(dt<=0)return;
   if(attacking){Reveal=Mathf.MoveTowards(Reveal,1,dt/.2f);return;}
   float held=Mathf.Min(dt,HoldRemaining);
   Reveal=Mathf.MoveTowards(Reveal,1,held/.2f);HoldRemaining-=held;
   Reveal=Mathf.MoveTowards(Reveal,0,(dt-held)/.4f);
  }
 }
}
