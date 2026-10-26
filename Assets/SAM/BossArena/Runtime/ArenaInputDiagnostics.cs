using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SamProduction.BossArena
{
    // Opt-in observation of this game's input system. Never injects a command or changes gameplay.
    public sealed class ArenaInputDiagnostics : MonoBehaviour
    {
        private StreamWriter writer;
        private StreamWriter eventWriter;
        private Keyboard observedKeyboard;
        private int keyboardEvents,mouseEvents;
        private double next;
        private string last="none";
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--sam-input-diagnostics")>=0)
                new GameObject("Input diagnostics").AddComponent<ArenaInputDiagnostics>();
        }
        private void Start()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--sam-output");
            if(i<0||i+1>=args.Length)throw new ArgumentException("Input diagnostics needs --sam-output");
            Directory.CreateDirectory(args[i+1]);
            writer=new StreamWriter(new FileStream(Path.Combine(args[i+1],"input_observation.csv"),FileMode.CreateNew));
            writer.AutoFlush=true;
            eventWriter=new StreamWriter(new FileStream(Path.Combine(args[i+1],"input_events.csv"),FileMode.CreateNew));
            eventWriter.AutoFlush=true;
            eventWriter.WriteLine("wall_s,type,device_enabled,enter,numpad_enter,r,space,tab,w,a,s,d,detail");
            observedKeyboard=Keyboard.current;
            if(observedKeyboard!=null)observedKeyboard.onTextInput+=TextObserved;
            writer.WriteLine("wall_s,focused,keyboard_present,mouse_present,keyboard_events,mouse_events,enter_pressed,r_pressed,space_pressed,tab_pressed,move_x,move_y,round,automatic_tick");
            InputSystem.onEvent+=Observe;
        }
        private void Observe(InputEventPtr e,InputDevice device)
        {
            if(!Application.isFocused)return;
            if(device is Keyboard keyboard)
            {
                keyboardEvents++;
                if(e.type==StateEvent.Type||e.type==DeltaStateEvent.Type)
                    eventWriter?.WriteLine(FormattableString.Invariant($"{Time.realtimeSinceStartupAsDouble:F6},{e.type},{device.enabled},{keyboard.enterKey.ReadValueFromEvent(e)},{keyboard.numpadEnterKey.ReadValueFromEvent(e)},{keyboard.rKey.ReadValueFromEvent(e)},{keyboard.spaceKey.ReadValueFromEvent(e)},{keyboard.tabKey.ReadValueFromEvent(e)},{keyboard.wKey.ReadValueFromEvent(e)},{keyboard.aKey.ReadValueFromEvent(e)},{keyboard.sKey.ReadValueFromEvent(e)},{keyboard.dKey.ReadValueFromEvent(e)},state"));
                else eventWriter?.WriteLine(FormattableString.Invariant($"{Time.realtimeSinceStartupAsDouble:F6},{e.type},{device.enabled},,,,,,,,,,non_state"));
            }
            if(device is Mouse)mouseEvents++;
        }
        private void TextObserved(char value)
        {
            if(!Application.isFocused)return;
            string label=value=='\r'||value=='\n'?"Enter":value=='r'?"Restart":value==' '?"Space":value=='\t'?"Tab":value=='w'||value=='a'||value=='s'||value=='d'?"MovementLetter":"OtherText";
            eventWriter?.WriteLine(FormattableString.Invariant($"{Time.realtimeSinceStartupAsDouble:F6},TEXT_CALLBACK,{observedKeyboard.enabled},,,,,,,,,,{label}"));
        }
        private void Update()
        {
            var k=Keyboard.current;var m=Mouse.current;
            bool enter=k!=null&&k.enterKey.wasPressedThisFrame;
            bool restart=k!=null&&k.rKey.wasPressedThisFrame;
            bool dodge=k!=null&&k.spaceKey.wasPressedThisFrame;
            bool tab=k!=null&&k.tabKey.wasPressedThisFrame;
            int x=k==null?0:(k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0);
            int y=k==null?0:(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0);
            if(enter||restart||dodge||tab)last=enter?"Enter":restart?"Restart":dodge?"Dodge":"Lock";
            if(Time.realtimeSinceStartupAsDouble<next&&!enter&&!restart&&!dodge&&!tab)return;
            next=Time.realtimeSinceStartupAsDouble+.1;
            var session=FindFirstObjectByType<ArenaSession>();
            writer?.WriteLine(FormattableString.Invariant($"{Time.realtimeSinceStartupAsDouble:F6},{Application.isFocused},{k!=null},{m!=null},{keyboardEvents},{mouseEvents},{enter},{restart},{dodge},{tab},{x},{y},{session?.State},{session?.automaticTick}"));
        }
        private void OnGUI()
        {
            GUI.color=Color.white;
            GUI.Label(new Rect(20,60,950,30),$"INPUT OBSERVER / focus={Application.isFocused} / keyboard={Keyboard.current!=null} / events={keyboardEvents}/{mouseEvents} / last={last}");
        }
        private void OnDestroy(){InputSystem.onEvent-=Observe;if(observedKeyboard!=null)observedKeyboard.onTextInput-=TextObserved;writer?.Dispose();eventWriter?.Dispose();}
    }
}
