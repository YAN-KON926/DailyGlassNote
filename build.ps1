$ErrorActionPreference = 'Stop'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$gac = 'C:\Windows\Microsoft.NET\assembly'

& $compiler /nologo /target:winexe /optimize+ /platform:anycpu /win32icon:'assets\daily-note-badge-04.ico' /out:'每日便签.exe' `
  /reference:"$gac\GAC_64\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll" `
  /reference:"$gac\GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll" `
  /reference:"$gac\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll" `
  /reference:"$gac\GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll" `
  /reference:"$framework\System.Runtime.Serialization.dll" `
  /reference:"$framework\System.dll" `
  /reference:"$framework\System.Core.dll" `
  /reference:"$framework\System.Drawing.dll" `
  /reference:"$framework\System.Windows.Forms.dll" `
  src\NativeSticky.cs

if ($LASTEXITCODE -ne 0) { throw "C# 编译失败，退出码 $LASTEXITCODE" }
Get-Item -LiteralPath '.\每日便签.exe' | Select-Object FullName,Length,LastWriteTime
