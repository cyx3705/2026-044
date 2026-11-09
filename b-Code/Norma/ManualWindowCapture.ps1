param([long]$WindowHandle,[int]$ExpectedProcessId,[int]$X=0,[int]$Y=0,[int]$Width=0,[int]$Height=0,[string]$OutputPath,[string]$Action='capture',[string]$PageName,[string]$PagePath,[string]$Query,[int]$Offset=0,[int]$Limit=30)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NormaWindowCapture {
 public delegate bool EnumProc(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumProc callback, IntPtr l);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h,System.Text.StringBuilder text,int count);
 public static IntPtr Child(IntPtr parent,string className) { IntPtr result=IntPtr.Zero; EnumChildWindows(parent,delegate(IntPtr h,IntPtr l){var text=new System.Text.StringBuilder(256);GetClassName(h,text,256);if(text.ToString()==className){result=h;return false;}return true;},IntPtr.Zero);return result; }
 public sealed class Entry { public long Handle; public string Name,Path,Expansion; public bool Selected; }
 [DllImport("user32.dll",SetLastError=true)] public static extern IntPtr SendMessageTimeout(IntPtr h,uint message,IntPtr w,IntPtr l,uint flags,uint timeout,out UIntPtr result);
 [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access,bool inherit,int id);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
 [DllImport("kernel32.dll")] static extern IntPtr VirtualAllocEx(IntPtr p,IntPtr address,UIntPtr size,uint allocation,uint protect);
 [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(IntPtr p,IntPtr address,UIntPtr size,uint release);
 [DllImport("kernel32.dll")] static extern bool WriteProcessMemory(IntPtr p,IntPtr address,byte[] data,UIntPtr length,out UIntPtr written);
 [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr p,IntPtr address,byte[] data,UIntPtr length,out UIntPtr read);
 [DllImport("kernel32.dll")] static extern bool IsWow64Process(IntPtr p,out bool wow);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
 [DllImport("oleacc.dll")] static extern int ObjectFromLresult(IntPtr result,ref Guid iid,IntPtr w,[MarshalAs(UnmanagedType.Interface)] out object obj);
 static long Message(IntPtr h,uint message,long w,long l) { UIntPtr result;if(SendMessageTimeout(h,message,new IntPtr(w),new IntPtr(l),2,1000,out result)==IntPtr.Zero)throw new Exception("手册控件消息超时或失败");return unchecked((long)result.ToUInt64()); }
 public static string DocumentTitle(IntPtr content) {
  var result=Message(content,RegisterWindowMessage("WM_HTML_GETOBJECT"),0,0); if(result==0)return "";
  var iid=new Guid("332C4425-26CB-11D0-B483-00C04FD90119"); object doc;
  if(ObjectFromLresult(new IntPtr(result),ref iid,IntPtr.Zero,out doc)!=0)return "";
  try {return Convert.ToString(doc.GetType().InvokeMember("title",System.Reflection.BindingFlags.GetProperty,null,doc,null));}finally{if(Marshal.IsComObject(doc))Marshal.ReleaseComObject(doc);}
 }
 public static void Select(IntPtr tree,long item) { Message(tree,0x1114,0,item);Message(tree,0x110B,9,item);if(Message(tree,0x110A,9,0)!=item)throw new Exception("原目录未确认选择成功"); }
 public static void Expand(IntPtr tree,long item){Message(tree,0x1102,2,item);}
 public static Entry[] Rows(IntPtr tree,int pid) {
  var process=OpenProcess(0x438,false,pid);if(process==IntPtr.Zero)throw new Exception("无法读取原目录进程");
  IntPtr remote=IntPtr.Zero;
  try {
   bool wow;IsWow64Process(process,out wow);int ptr=(Environment.Is64BitProcess&&!wow)?8:4;int size=ptr==8?56:40;
   remote=VirtualAllocEx(process,IntPtr.Zero,new UIntPtr((uint)(size+4096)),0x3000,4);if(remote==IntPtr.Zero)throw new Exception("无法分配目录读取缓冲区");
   var rows=new System.Collections.Generic.List<Entry>();var stack=new System.Collections.Generic.Stack<System.Tuple<long,string>>();long first=Message(tree,0x110A,0,0);if(first!=0)stack.Push(System.Tuple.Create(first,""));
   long selected=Message(tree,0x110A,9,0);
   while(stack.Count>0){
    if(rows.Count>=20000)throw new Exception("目录超过20000项，停止遍历");var node=stack.Pop();long item=node.Item1;var buffer=new byte[size+4096];
    Action<int,long,int> put=delegate(int offset,long value,int bytes){Array.Copy(BitConverter.GetBytes(value),0,buffer,offset,bytes);};
    put(0,0x59,4);put(ptr==8?8:4,item,ptr);put(ptr==8?20:12,0x20,4);put(ptr==8?24:16,remote.ToInt64()+size,ptr);put(ptr==8?32:20,2048,4);
    UIntPtr count;if(!WriteProcessMemory(process,remote,buffer,new UIntPtr((uint)buffer.Length),out count)||Message(tree,0x113E,0,remote.ToInt64())==0||!ReadProcessMemory(process,remote,buffer,new UIntPtr((uint)buffer.Length),out count))throw new Exception("读取原目录项失败");
    string name=System.Text.Encoding.Unicode.GetString(buffer,size,4096).Split('\0')[0];string path=node.Item2.Length==0?name:node.Item2+"/"+name;int state=BitConverter.ToInt32(buffer,ptr==8?16:8);int children=BitConverter.ToInt32(buffer,ptr==8?44:32);
    rows.Add(new Entry{Handle=item,Name=name,Path=path,Selected=item==selected,Expansion=children==0?"LeafNode":(state&0x20)!=0?"Expanded":"Collapsed"});
    long next=Message(tree,0x110A,1,item);if(next!=0)stack.Push(System.Tuple.Create(next,node.Item2));long child=Message(tree,0x110A,4,item);if(child!=0)stack.Push(System.Tuple.Create(child,path));
   }
   return rows.ToArray();
  } finally {if(remote!=IntPtr.Zero)VirtualFreeEx(process,remote,UIntPtr.Zero,0x8000);CloseHandle(process);}
 }
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[void][NormaWindowCapture]::SetProcessDPIAware()
$handle=[IntPtr]$WindowHandle
[uint32]$actualProcessId=0
[void][NormaWindowCapture]::GetWindowThreadProcessId($handle,[ref]$actualProcessId)
if($actualProcessId -ne $ExpectedProcessId){throw '手册窗口句柄已失效或所属进程改变'}
if([NormaWindowCapture]::IsIconic($handle)){throw '手册窗口已最小化，请还原后重试'}
$treeHandle=[NormaWindowCapture]::Child($handle,'SysTreeView32')
$contentHandle=[NormaWindowCapture]::Child($handle,'Internet Explorer_Server')
if($treeHandle -eq [IntPtr]::Zero -or $contentHandle -eq [IntPtr]::Zero){throw '手册目录或右侧正文控件不可识别，拒绝猜测裁切区域'}
$rows=@()
if($Action -eq 'open' -or $Action -eq 'directory'){$rows=[NormaWindowCapture]::Rows($treeHandle,$ExpectedProcessId)}
if($Action -eq 'open') {
 if([string]::IsNullOrWhiteSpace($PageName)){throw 'name必填，应为目录中的完整页名称'}
 if(-not [string]::IsNullOrWhiteSpace($PagePath)){
  $parts=$PagePath.Split('/');$prefix=''
  foreach($segment in $parts){
   if($prefix.Length -gt 0){$prefix+='/'};$prefix+=$segment
   $found=@($rows | Where-Object {$_.Path -ceq $prefix})
   if($found.Count -ne 1){throw ('path分段无法唯一定位：'+$prefix)}
   if($found[0].Expansion -eq 'Collapsed'){[NormaWindowCapture]::Expand($treeHandle,$found[0].Handle);$rows=[NormaWindowCapture]::Rows($treeHandle,$ExpectedProcessId)}
  }
 }
 $matches=@($rows | Where-Object {$_.Name -ceq $PageName})
 if(-not [string]::IsNullOrWhiteSpace($PagePath)){$matches=@($matches | Where-Object {$_.Path -ceq $PagePath})}
 if($matches.Count -eq 0){throw '未找到完整页名；请用directory确认名称并传入完整path展开祖先'}
 if($matches.Count -gt 1){throw ('同名页面有'+$matches.Count+'个，请传完整path：'+[string]::Join(' | ',@($matches | ForEach-Object {$_.Path})))}
 $selected=$matches[0]
 if($selected.Expansion -ne 'LeafNode'){throw '名称对应目录分组，请选择具体页面'}
 [NormaWindowCapture]::Select($treeHandle,$selected.Handle)
 $verified=$false;$title=''
 for($attempt=0;$attempt -lt 40;$attempt++){
  $title=[NormaWindowCapture]::DocumentTitle($contentHandle)
  if($title -ceq $PageName){$verified=$true;break}
  Start-Sleep -Milliseconds 100
 }
 if(-not $verified){throw ('已选择目录项，但右侧正文标题未匹配：'+$title+'；不报告打开成功')}
 @{name=$PageName;path=$selected.Path;contentTitle=$title;verified=$true;processId=$ExpectedProcessId;windowHandle=$WindowHandle;note='已在原手册打开并核实右侧正文标题；可继续manual.screenshot截正文'} | ConvertTo-Json -Compress
 exit 0
}
$directoryItems=@()
if($Action -eq 'directory'){
 foreach($item in $rows){
  if(-not [string]::IsNullOrWhiteSpace($Query) -and $item.Name.IndexOf($Query,[StringComparison]::OrdinalIgnoreCase) -lt 0){continue}
  $directoryItems+=@{name=$item.Name;path=$item.Path;selected=$item.Selected;expansion=$item.Expansion}
 }
}
$rect=New-Object NormaWindowCapture+Rect
if(-not [NormaWindowCapture]::GetWindowRect($handle,[ref]$rect)){throw '无法读取手册窗口；请在交互桌面会话运行'}
$fullWidth=$rect.Right-$rect.Left
$fullHeight=$rect.Bottom-$rect.Top
if($fullWidth -lt 1 -or $fullHeight -lt 1 -or $fullWidth -gt 12000 -or $fullHeight -gt 12000){throw '窗口尺寸不可截图'}
$regionHandle=$contentHandle
if($Action -eq 'directory'){$regionHandle=$treeHandle}
$region=New-Object NormaWindowCapture+Rect
if(-not [NormaWindowCapture]::GetWindowRect($regionHandle,[ref]$region)){throw '无法读取目标区域尺寸'}
$regionWidth=$region.Right-$region.Left;$regionHeight=$region.Bottom-$region.Top
if($Width -eq 0){$Width=$regionWidth-$X}
if($Height -eq 0){$Height=$regionHeight-$Y}
if($X -lt 0 -or $Y -lt 0 -or $Width -lt 1 -or $Height -lt 1 -or $X+$Width -gt $regionWidth -or $Y+$Height -gt $regionHeight){throw '裁切区域超出所选正文/目录控件范围'}
$bitmap=New-Object System.Drawing.Bitmap($fullWidth,$fullHeight)
$graphics=[System.Drawing.Graphics]::FromImage($bitmap)
try {
 $graphics.Clear([System.Drawing.Color]::Magenta)
 $dc=$graphics.GetHdc()
 try { $ok=[NormaWindowCapture]::PrintWindow($handle,$dc,2) } finally { $graphics.ReleaseHdc($dc) }
 if(-not $ok){throw 'Windows PrintWindow 返回失败'}
 $colors=New-Object 'System.Collections.Generic.HashSet[int]'
 for($cy=0;$cy -lt $fullHeight;$cy += [Math]::Max(1,[int]($fullHeight/30))){for($cx=0;$cx -lt $fullWidth;$cx += [Math]::Max(1,[int]($fullWidth/30))){[void]$colors.Add($bitmap.GetPixel($cx,$cy).ToArgb())}}
 if($colors.Count -lt 3){throw '系统截图为空白或纯色，未保存为有效证据'}
 $cropX=$region.Left-$rect.Left+$X;$cropY=$region.Top-$rect.Top+$Y
 $crop=$bitmap.Clone((New-Object System.Drawing.Rectangle($cropX,$cropY,$Width,$Height)),[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
 try {$crop.Save($OutputPath,[System.Drawing.Imaging.ImageFormat]::Png)} finally {$crop.Dispose()}
 $result=@{contentTitle=[NormaWindowCapture]::DocumentTitle($contentHandle);imagePath=$OutputPath;width=$Width;height=$Height;x=$X;y=$Y;regionWidth=$regionWidth;regionHeight=$regionHeight;windowWidth=$fullWidth;windowHeight=$fullHeight;processId=$ExpectedProcessId;windowHandle=$WindowHandle;evidence='native-window';method='Windows PrintWindow';region='content';note='原手册右侧正文像素截图；裁切坐标相对正文控件左上角，不含左侧目录和工具栏'}
 if($Action -eq 'directory'){
  $result.region='directory';$result.note='左侧原目录控件截图与当前可访问目录项；折叠分组可能尚未暴露子页，path用于逐级展开定位'
  $result.total=$directoryItems.Count;$result.offset=$Offset;$result.items=@($directoryItems | Select-Object -Skip $Offset -First $Limit);$result.returned=$result.items.Count
  $result.nextOffset=$null
  if($Offset+$result.returned -lt $result.total){$result.nextOffset=$Offset+$result.returned}
 }
 $result | ConvertTo-Json -Depth 8 -Compress
} finally {$graphics.Dispose();$bitmap.Dispose()}
