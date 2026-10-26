using UnityEngine;

// Offline frame capture must advance unscaled combat timers by the captured frame duration.
// Ordinary gameplay always uses Unity's real unscaled clock.
public static class CombatClock {
#if UNITY_EDITOR
 static int rate,startFrame,lastFrame=-1;static float start;
 static void Sync(){int next=Time.captureFramerate;if(next!=rate||Time.frameCount<lastFrame){rate=next;startFrame=Time.frameCount;start=Time.unscaledTime;}lastFrame=Time.frameCount;}
#endif
 public static float UnscaledTime {get {
#if UNITY_EDITOR
  Sync();if(rate>0)return start+(Time.frameCount-startFrame)/(float)rate;
#endif
  return Time.unscaledTime;
 }}
 public static float Realtime {get {
#if UNITY_EDITOR
  if(Time.captureFramerate>0)return UnscaledTime;
#endif
  return Time.realtimeSinceStartup;
 }}
 public static float UnscaledDeltaTime {get {
#if UNITY_EDITOR
  if(Time.captureFramerate>0)return 1f/Time.captureFramerate;
#endif
  return Time.unscaledDeltaTime;
 }}
}
