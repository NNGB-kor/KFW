using System;using System.Collections.Generic;using System.Linq;
namespace KlipperWinForms {
 // Accessed only on the UI thread; each lease owns exactly its requested resources.
 sealed class UiWorkState {
  internal sealed class Work {internal string Label;internal HashSet<string> Keys;}
  readonly Dictionary<Guid,Work> Active=new Dictionary<Guid,Work>();
  internal bool Busy=>Active.Count!=0;
  internal bool Locked(params string[] keys)=>Active.Values.Any(work=>work.Keys.Overlaps(keys));
  internal bool Begin(string label,string[] keys,out Guid token){token=Guid.Empty;if(Locked(keys))return false;token=Guid.NewGuid();Active[token]=new Work{Label=label,Keys=new HashSet<string>(keys)};return true;}
  internal void End(Guid token){Active.Remove(token);}
  internal string Summary=>string.Join(" | ",Active.Values.Select(work=>work.Label));
 }
}
