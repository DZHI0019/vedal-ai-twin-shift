[CmdletBinding()]
param([Parameter(Mandatory)][string]$Installer,[string]$ScratchRoot=[IO.Path]::GetTempPath(),[string]$PreviousInstaller)
$ErrorActionPreference='Stop'
$setup=(Resolve-Path -LiteralPath $Installer).Path
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
    if($PreviousInstaller -and [IO.File]::ReadAllText((Join-Path $env:TWINSHIFT_DATA_DIR 'backup-light.txt')) -ne 'backup'){
        throw 'Upgrade did not preserve the previous theme backup'
    }
    foreach($file in @('TwinShift.exe','portable.install','使用手册.html','marketplace/.agents/plugins/marketplace.json','marketplace/plugin/hooks/TwinSignal.exe','scenes/TwinShift-neo.zip','scenes/TwinShift-evo.zip')){
        if(!(Test-Path (Join-Path $target $file))){throw "Missing payload $file"}
    }
    [IO.File]::WriteAllText((Join-Path $target 'personal.txt'),'keep')
    [IO.File]::WriteAllText((Join-Path $env:TWINSHIFT_DATA_DIR 'backup-light.txt'),'backup')
    Execute $setup "/S /PORTABLE /D=$target" 0
    Execute (Join-Path $target 'Uninstall.exe') "/S _?=$target" 0
    if((Test-Path (Join-Path $target 'TwinShift.exe')) -or (Test-Path (Join-Path $target 'twinshift.install'))){throw 'Uninstall left installed payload'}
    if([IO.File]::ReadAllText((Join-Path $target 'personal.txt')) -ne 'keep' -or [IO.File]::ReadAllText((Join-Path $env:TWINSHIFT_DATA_DIR 'backup-light.txt')) -ne 'backup'){throw 'User file lost'}
    Write-Output "PASS foreign directory refusal, portable installation, reinstall and uninstall with user files preserved."
} finally {
    $env:TWINSHIFT_DATA_DIR=$savedData
    $env:CODEX_HOME=$savedProfile
}
