[CmdletBinding()]
param([string]$Python='python',[string]$MakeNSIS,[string]$DreamValidator,[switch]$SkipRestore,
    [switch]$Signed,[string]$SignTool,[string]$SignerThumbprint)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$out=Join-Path $root 'out'
$packageDir=$out
$signatureScript=Join-Path $PSScriptRoot 'windows-signature.ps1'
if($Signed){
    if(!$MakeNSIS){throw 'Signed builds require NSIS and produce a complete installer.'}
    & $signatureScript -Mode Preflight -SignTool $SignTool -SignerThumbprint $SignerThumbprint
    $packageDir=[IO.Directory]::CreateDirectory((Join-Path $out ('signing-'+[Guid]::NewGuid().ToString('N')))).FullName
}elseif($SignTool -or $SignerThumbprint){throw 'Use -Signed to enable release signing.'}
$stage=Join-Path $packageDir 'app'
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
foreach($file in @('attention.wav','evo.jpg','neo.jpg','tutel.png','twinshift.ico','voices.json','NOTICE.md','TUTEL-LICENSE.md','DREAM-SKIN-LICENSE.txt')){Stage-File (Join-Path $root "assets/$file") "assets/$file"}
foreach($name in @('neo','evo')){Stage-File (Join-Path $out "scenes/TwinShift-$name.zip") "scenes/TwinShift-$name.zip"}
foreach($name in @('neo-light','evo-dark')){Stage-File (Join-Path $root "themes/$name.txt") "themes/$name.txt"}
foreach($file in @('.agents/plugins/marketplace.json','plugin/.codex-plugin/plugin.json','plugin/hooks/hooks.json')){Stage-File (Join-Path $root $file) "marketplace/$file"}
Stage-File (Join-Path $root 'signal/bin/Release/net48/TwinSignal.exe') 'marketplace/plugin/hooks/TwinSignal.exe'
$items=@(Get-ChildItem -LiteralPath $stage -Recurse -Force)
if($items | Where-Object {(!$_.PSIsContainer -and !$payload.ContainsKey($_.FullName)) -or ($_.Attributes -band [IO.FileAttributes]::ReparsePoint)}){throw 'Unexpected file or reparse point in out/app. Inspect the staging directory before packaging.'}
if($MakeNSIS){
    $version=([xml](Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
    if($version -notmatch '^\d+\.\d+\.\d+(?:-[a-z]+\.\d+)?$'){throw 'Unexpected installer version.'}
    if($Signed){
        foreach($binary in @('TwinShift.exe','marketplace/plugin/hooks/TwinSignal.exe')){
            & $signatureScript -Mode Sign -Path (Join-Path $stage $binary) -SignTool $SignTool -SignerThumbprint $SignerThumbprint
        }
    }
    $lines=@(foreach($item in ($items|Where-Object {!$_.PSIsContainer})){ 'Delete "$INSTDIR\'+$item.FullName.Substring($stage.Length+1)+'"' })
    $lines+=@(foreach($item in ($items|Where-Object {$_.PSIsContainer}|Sort-Object {$_.FullName.Length} -Descending)){ 'RMDir "$INSTDIR\'+$item.FullName.Substring($stage.Length+1)+'"' })
    [IO.File]::WriteAllLines((Join-Path $packageDir 'uninstall-files.nsh'),[string[]]$lines,[Text.UTF8Encoding]::new($true))
    $compileArgs=@('/INPUTCHARSET','UTF8',"/DRELEASE_VERSION=$version","/DSTAGE=$stage","/DOUTPUT=$packageDir")
    $savedSdk=$env:TWINSHIFT_SIGNTOOL
    $savedSigner=$env:TWINSHIFT_SIGNER
    try{
        if($Signed){
            $env:TWINSHIFT_SIGNTOOL=(Resolve-Path -LiteralPath $SignTool).Path
            $env:TWINSHIFT_SIGNER=$SignerThumbprint
            $compileArgs+=@("/DSIGN_PWSH=$((Get-Command pwsh -CommandType Application).Source)","/DSIGN_SCRIPT=$signatureScript")
        }
        & $MakeNSIS @compileArgs (Join-Path $root 'packaging/TwinShift.nsi')
        if($LASTEXITCODE){throw 'Installer build failed'}
        if($Signed){
            $setup=Join-Path $packageDir "TwinShift-$version-Setup.exe"
            & $signatureScript -Mode Sign -Path $setup
            & (Join-Path $root 'tests/installer.ps1') -Installer $setup -SignTool $SignTool -SignerThumbprint $SignerThumbprint
            $signedDir=[IO.Directory]::CreateDirectory((Join-Path $out 'signed')).FullName
            $destination=Join-Path $signedDir ([IO.Path]::GetFileName($setup))
            [IO.File]::Move($setup,$destination,$true)
            Write-Output "Verified signed installer: $destination"
        }
    }finally{
        $env:TWINSHIFT_SIGNTOOL=$savedSdk
        $env:TWINSHIFT_SIGNER=$savedSigner
    }
}
