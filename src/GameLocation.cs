using System;
using System.IO;

namespace DonaldOnline {
public static class GameLocation {
 public static string Find(string root, string[] args, Func<string> savedFolder) {
  for(int i=0;i+1<args.Length;i++) if(args[i]=="--game") return Path.GetFullPath(args[i+1]);
  string bundled=Path.Combine(root,"Donald Duck");
  if(AppPaths.IsGameFolder(bundled)) return bundled;
  string saved=savedFolder();
  return AppPaths.IsGameFolder(saved)?saved:bundled;
 }
 public static bool PackageComplete(string root,string game) {
  return AppPaths.IsGameFolder(game) && File.Exists(Path.Combine(game,"CD","Donald.iso"))
   && File.Exists(Path.Combine(game,"DDraw.dll")) && File.Exists(Path.Combine(game,"D3DImm.dll"))
   && File.Exists(Path.Combine(game,"dgVoodoo.conf"));
 }
}
}
