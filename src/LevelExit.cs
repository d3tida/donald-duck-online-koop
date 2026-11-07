using System;using System.Collections.Generic;using System.Text;
namespace DonaldOnline {
public sealed partial class GameBridge {
 // Level exit sync. Every map change of the scripts goes through ChangeLevel 0x405800 (callers 0x46B04B/0x46B074 =
 // Proc_ChangeMap). Inside a level the hook can hold such a request back ("veto"): the wanted map is recorded and sent
 // to the partner (Pose.LeaveMap). Only when both games asked for the same map both are sent there together, so
 // nobody is left behind when leaving a level via the menu.
 uint levelHookMem;byte[] levelHookOriginal;uint lastLeaveCount;string leaveRequest;
 public string LeaveRequest{get{return leaveRequest;}}
 public string LeaveDebug{get{return levelHookMem==0?"kein Levelhook":"veto="+U(levelHookMem+0x200)+" anfragen="+U(levelHookMem+0x204)+" wunsch="+(leaveRequest??"-");}}
 void InstallLevelHook(){levelHookOriginal=R(0x405800,5);if(BitConverter.ToString(levelHookOriginal)!="A1-E8-72-56-00")throw new Exception("ChangeLevel bereits modifiziert");
  levelHookMem=(uint)VirtualAllocEx(handle,IntPtr.Zero,(UIntPtr)4096,0x3000,0x40).ToInt64();if(levelHookMem<65536)throw new Exception("Levelhook Speicher");
  uint veto=levelHookMem+0x200,count=levelHookMem+0x204,name=levelHookMem+0x210;
  // Trampoline at +0x100: the displaced instruction, then back into ChangeLevel. Our own transfers call this directly.
  // It first counts the map change (+0x208), so a reload without such a call is known to be a respawn.
  var t=new List<byte>{0xFF,0x05};t.AddRange(BitConverter.GetBytes(levelHookMem+0x208));t.AddRange(levelHookOriginal);t.Add(0xE9);t.AddRange(BitConverter.GetBytes(unchecked(0x405805u-(levelHookMem+0x100+6+5+5))));W(levelHookMem+0x100,t.ToArray());
  var b=new List<byte>();Action<byte[]> B=x=>b.AddRange(x);Action<uint> I=x=>b.AddRange(BitConverter.GetBytes(x));
  B(new byte[]{0x83,0x3D});I(veto);B(new byte[]{0,0x74});int pass=b.Count;B(new byte[]{0});
  // pushad; mov esi,[esp+0x24] (map argument); mov edi,name; mov ecx,31; copy until zero; mov byte [edi],0; inc [count]; popad; xor eax,eax; ret
  B(new byte[]{0x60,0x8B,0x74,0x24,0x24,0xBF});I(name);B(new byte[]{0xB9,31,0,0,0});int loop=b.Count;B(new byte[]{0xAC,0xAA,0x84,0xC0,0x74,0x02,0xE2});b.Add((byte)(loop-(b.Count+1)));B(new byte[]{0xC6,0x07,0,0xFF,0x05});I(count);B(new byte[]{0x61,0x31,0xC0,0xC3});
  b[pass]=(byte)(b.Count-pass-1);B(new byte[]{0xE9});I(unchecked(levelHookMem+0x100-(levelHookMem+(uint)b.Count+4)));
  W(levelHookMem,b.ToArray());lastLeaveCount=0;Jump(0x405800,levelHookMem,5);}
 public uint MapChangeCount{get{try{return levelHookMem==0?0:U(levelHookMem+0x208);}catch{return 0;}}}
 uint ChangeLevelTarget{get{return levelHookMem!=0?levelHookMem+0x100:0x405800u;}}
 public void SetLeaveVeto(bool on){if(on&&NativeExitActive)on=false;if(levelHookMem!=0)try{WU(levelHookMem+0x200,on?1u:0u);}catch{}}
 // Reads requests the veto held back; called every Capture.
 void UpdateLeave(){if(levelHookMem==0)return;uint c=U(levelHookMem+0x204);if(c!=lastLeaveCount){lastLeaveCount=c;var bytes=new List<byte>();for(uint i=0;i<31;i++){byte x=R(levelHookMem+0x210+i,1)[0];if(x==0)break;bytes.Add(x);}string map=Encoding.ASCII.GetString(bytes.ToArray());if(AllowedMapName(map)){if(leaveRequest!=map)AppPaths.Log("Spiel möchte nach "+map+" (angehalten, bis der Partner mitkommt)");leaveRequest=map;}}}
 static bool AllowedMapName(string map){return map.Length>0&&map.Length<=28&&System.Text.RegularExpressions.Regex.IsMatch(map,"^[A-Za-z0-9_]+$");}
 public void ClearLeave(){leaveRequest=null;}
 // Test aid: as if the game had asked for this map (held back by the veto).
 public void TestLeave(string map){if(AllowedMapName(map)){AppPaths.Log("Spiel möchte nach "+map+" (Test)");leaveRequest=map;}}
 void ClearLevelHook(){if(levelHookMem==0)return;try{WU(levelHookMem+0x200,0);CodeWrite(0x405800,levelHookOriginal);}catch{}levelHookMem=0;}
}
}
