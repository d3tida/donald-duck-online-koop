using System;using System.Collections.Generic;
namespace DonaldOnline{
public sealed class InputExpressions{
 readonly uint[][] words;readonly uint entryBase;bool[] previous;HashSet<int> keys;bool latch;bool[] values;byte[] visit;
 public InputExpressions(uint[][] expressions,uint baseAddress){words=expressions;entryBase=baseAddress;previous=new bool[words.Length];}
 public bool[] Evaluate(int scan,bool edgeLatch){keys=new HashSet<int>();if(scan>=0)keys.Add(scan);latch=edgeLatch;values=new bool[words.Length];visit=new byte[words.Length];for(int i=0;i<words.Length;i++)Action(i,0);previous=(bool[])values.Clone();return values;}
 bool Action(int i,int depth){if(i<0||i>=words.Length||depth>32)return false;if(visit[i]==2)return values[i];if(visit[i]==1)return false;visit[i]=1;int pos=0;values[i]=Node(i,ref pos,depth+1);visit[i]=2;return values[i];}
 uint Arg(uint[] data,ref int pos){return pos<data.Length?data[pos++]:0;}
 bool Node(int i,ref int pos,int depth){uint[] data=words[i];if(pos>=data.Length||depth>64)return false;int op=(int)(Arg(data,ref pos)&255);if(op==1||op==2){bool a=Node(i,ref pos,depth+1),b=Node(i,ref pos,depth+1);return op==1?a&&b:a||b;}if(op==3)return !Node(i,ref pos,depth+1);if(op>=4&&op<=7){bool down=keys.Contains((int)(Arg(data,ref pos)&65535));return op==5?false:op==7?!down:down;}if(op>=8&&op<=11){uint reference=Arg(data,ref pos);if(reference<entryBase||(reference-entryBase)%24!=0)return false;int index=(int)((reference-entryBase)/24);if(index<0||index>=words.Length)return false;bool down=Action(index,depth+1);if(op==8)return down&&(latch||!previous[index]);if(op==9)return !down&&previous[index];return op==10?down:!down;}if((op>=12&&op<=15)||(op>=20&&op<=27)){Arg(data,ref pos);Arg(data,ref pos);return op==15||op==23||op==27;}if(op==16){for(int a=0;a<4;a++)Arg(data,ref pos);return false;}if(op>=17&&op<=19){for(int a=0;a<5;a++)Arg(data,ref pos);return false;}if(op==28||op==29){for(int a=0;a<3;a++)Arg(data,ref pos);return false;}if(op>=30&&op<=32){Arg(data,ref pos);return false;}pos=data.Length;return false;}
 public int KeyFor(int action){if(action<0||action>=words.Length)return -1;uint[] data=words[action];for(int i=0;i+1<data.Length;i++)if((data[i]&65535)==0xF006)return (int)(data[i+1]&65535);return -1;}
}
}
