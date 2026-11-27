[CmdletBinding()]
param([string]$Python='python',[string]$MakeNSIS,[string]$DreamValidator,[switch]$SkipRestore)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$out=Join-Path $root 'out'
$stage=Join-Path $out 'app'
$payload=@{}
function Stage-File([string]$Source,[string]$Destination){
    $path=Join-Path $stage $Destination
    Copy-Item -LiteralPath $Source -Destination $path -Force
    $payload[[IO.Path]::GetFullPath($path)]=$true
}
foreach($project in @('app/TwinShift','signal/TwinSignal')){
    $file=Join-Path $root ($project+'.csproj')
    if(!$SkipRestore){& dotnet restore $file --locked-mode;if($LASTEXITCODE){throw 'Restore failed'}}
    & dotnet build $file -c Release --no-restore
    if($LASTEXITCODE){throw 'Build failed'}
}
$sceneArgs=@((Join-Path $root 'tools/build_scenes.py'),'--out',(Join-Path $out 'scenes'))
if($DreamValidator){$sceneArgs+=@('--validator',$DreamValidator)}
& $Python @sceneArgs
if($LASTEXITCODE){throw 'Scene validation failed'}
$directories=@('assets','themes','scenes','marketplace/.agents/plugins','marketplace/plugin/.codex-plugin','marketplace/plugin/hooks')
foreach($dir in $directories){[IO.Directory]::CreateDirectory((Join-Path $stage $dir)) | Out-Null}
$files=@('app/bin/Release/net48/TwinShift.exe','app/bin/Release/net48/TwinShift.exe.config','LICENSE','docs/使用手册.html')
foreach($file in $files){Stage-File (Join-Path $root $file) ([IO.Path]::GetFileName($file))}
foreach($file in @('attention.wav','evo.jpg','neo.jpg','tutel.png','twinshift.ico','voices.json','NOTICE.md','TUTEL-LICENSE.md')){Stage-File (Join-Path $root "assets/$file") "assets/$file"}
foreach($name in @('neo','evo')){Stage-File (Join-Path $out "scenes/TwinShift-$name.zip") "scenes/TwinShift-$name.zip"}
foreach($name in @('neo-light','evo-dark')){Stage-File (Join-Path $root "themes/$name.txt") "themes/$name.txt"}
foreach($file in @('.agents/plugins/marketplace.json','plugin/.codex-plugin/plugin.json','plugin/hooks/hooks.json')){Stage-File (Join-Path $root $file) "marketplace/$file"}
Stage-File (Join-Path $root 'signal/bin/Release/net48/TwinSignal.exe') 'marketplace/plugin/hooks/TwinSignal.exe'
$items=@(Get-ChildItem -LiteralPath $stage -Recurse -Force)
if($items | Where-Object {(!$_.PSIsContainer -and !$payload.ContainsKey($_.FullName)) -or ($_.Attributes -band [IO.FileAttributes]::ReparsePoint)}){throw 'Unexpected file or reparse point in out/app. Inspect the staging directory before packaging.'}
if($MakeNSIS){
    $lines=@(foreach($item in ($items|Where-Object {!$_.PSIsContainer})){ 'Delete "$INSTDIR\'+$item.FullName.Substring($stage.Length+1)+'"' })
    $lines+=@(foreach($item in ($items|Where-Object {$_.PSIsContainer}|Sort-Object {$_.FullName.Length} -Descending)){ 'RMDir "$INSTDIR\'+$item.FullName.Substring($stage.Length+1)+'"' })
    [IO.File]::WriteAllLines((Join-Path $out 'uninstall-files.nsh'),[string[]]$lines,[Text.UTF8Encoding]::new($true))
    & $MakeNSIS /INPUTCHARSET UTF8 "/DSTAGE=$stage" "/DOUTPUT=$out" (Join-Path $root 'packaging/TwinShift.nsi')
    if($LASTEXITCODE){throw 'Installer build failed'}
}
