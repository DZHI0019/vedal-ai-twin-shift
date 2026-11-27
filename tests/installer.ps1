[CmdletBinding()]
param([Parameter(Mandatory)][string]$Installer,[string]$ScratchRoot=[IO.Path]::GetTempPath(),[string]$PreviousInstaller,
    [string]$SignTool,[string]$SignerThumbprint)
$ErrorActionPreference='Stop'
$setup=(Resolve-Path -LiteralPath $Installer).Path
$verify=Join-Path $PSScriptRoot '../tools/windows-signature.ps1'
if($SignTool -or $SignerThumbprint){& $verify -Mode Verify -Path $setup -SignTool $SignTool -SignerThumbprint $SignerThumbprint}
$run=[IO.Directory]::CreateDirectory((Join-Path $ScratchRoot ('twinshift-setup-'+[Guid]::NewGuid().ToString('N')))).FullName
$target=Join-Path $run '安装测试 with spaces'
$savedData=$env:TWINSHIFT_DATA_DIR
$savedProfile=$env:CODEX_HOME
function Execute([string]$Exe,[string]$Arguments,[int]$Expected){
    $process=Start-Process -FilePath $Exe -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    if($process.ExitCode -ne $Expected){throw "Unexpected exit $($process.ExitCode), wanted $Expected"}
}
try {
    $env:TWINSHIFT_DATA_DIR=[IO.Directory]::CreateDirectory((Join-Path $run 'data')).FullName
    $env:CODEX_HOME=[IO.Directory]::CreateDirectory((Join-Path $run 'codex')).FullName
    $foreign=[IO.Directory]::CreateDirectory((Join-Path $run 'unrelated folder')).FullName
    [IO.File]::WriteAllText((Join-Path $foreign 'mine.txt'),'keep')
    Execute $setup "/S /PORTABLE /D=$foreign" 4
    if($PreviousInstaller){
        $previous=(Resolve-Path -LiteralPath $PreviousInstaller).Path
        Execute $previous "/S /PORTABLE /D=$target" 0
        [IO.File]::WriteAllText((Join-Path $env:TWINSHIFT_DATA_DIR 'backup-light.txt'),'backup')
    }
    Execute $setup "/S /PORTABLE /D=$target" 0
    if($SignTool -or $SignerThumbprint){
        foreach($binary in @('TwinShift.exe','marketplace/plugin/hooks/TwinSignal.exe','Uninstall.exe')){
            & $verify -Mode Verify -Path (Join-Path $target $binary) -SignTool $SignTool -SignerThumbprint $SignerThumbprint
        }
    }
    if($PreviousInstaller -and [IO.File]::ReadAllText((Join-Path $env:TWINSHIFT_DATA_DIR 'backup-light.txt')) -ne 'backup'){
        throw 'Upgrade did not preserve the previous theme backup'
    }
    foreach($file in @('TwinShift.exe','portable.install','使用手册.html','marketplace/.agents/plugins/marketplace.json','marketplace/plugin/hooks/TwinSignal.exe','scenes/TwinShift-neuro.zip','scenes/TwinShift-Evil.zip')){
        if(!(Test-Path (Join-Path $target $file))){throw "Missing payload $file"}
    }
    $voices = Join-Path $target 'assets/voices'
    $sourceCatalog = Join-Path $PSScriptRoot '../assets/voices.json'
    $installedCatalog = Join-Path $target 'assets/voices.json'
    if ((Get-FileHash -LiteralPath $installedCatalog).Hash -ne (Get-FileHash -LiteralPath $sourceCatalog).Hash) {
        throw 'Installed voice catalogue differs from the source release'
    }
    $cues = @(Get-Content -Raw -Encoding utf8 -LiteralPath $sourceCatalog | ConvertFrom-Json)
    if ($cues.Count -ne 8 -or @(Get-ChildItem -File -LiteralPath $voices).Count -ne 8) { throw 'Incomplete offline voice pack' }
    foreach ($cue in $cues) {
        if ((Get-FileHash -LiteralPath (Join-Path $voices ($cue.Id + '.mp3'))).Hash -ne $cue.Sha256) { throw 'Installed voice checksum mismatch' }
    }
    [IO.File]::WriteAllText((Join-Path $target 'personal.txt'),'keep')
    [IO.File]::WriteAllText((Join-Path $env:TWINSHIFT_DATA_DIR 'backup-light.txt'),'backup')
    Execute $setup "/S /PORTABLE /D=$target" 0
    Execute (Join-Path $target 'Uninstall.exe') "/S _?=$target" 0
    if (Test-Path -LiteralPath $voices) { throw 'Uninstall left bundled recordings behind' }
    if((Test-Path (Join-Path $target 'TwinShift.exe')) -or (Test-Path (Join-Path $target 'twinshift.install'))){throw 'Uninstall left installed payload'}
    if([IO.File]::ReadAllText((Join-Path $target 'personal.txt')) -ne 'keep' -or [IO.File]::ReadAllText((Join-Path $env:TWINSHIFT_DATA_DIR 'backup-light.txt')) -ne 'backup'){throw 'User file lost'}
    Write-Output "PASS foreign directory refusal, portable installation, reinstall and uninstall with user files preserved."
} finally {
    $env:TWINSHIFT_DATA_DIR=$savedData
    $env:CODEX_HOME=$savedProfile
}
