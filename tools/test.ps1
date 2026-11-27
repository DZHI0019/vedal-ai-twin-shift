[CmdletBinding()]
param([string]$ScratchRoot=[IO.Path]::GetTempPath(),[string]$InstalledRoot,[switch]$SkipRestore,[switch]$CheckAudioOutput,[switch]$CheckCodex,[switch]$CheckWindowScene,[string]$WallpaperEngine)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if(!$InstalledRoot){$InstalledRoot=Join-Path $root 'out/app'}
if(!(Test-Path (Join-Path $InstalledRoot 'TwinShift.exe'))){throw 'Build or install the package first.'}
$run=[IO.Directory]::CreateDirectory((Join-Path $ScratchRoot ('twinshift-check-'+[Guid]::NewGuid().ToString('N')))).FullName
$stage=[IO.Directory]::CreateDirectory((Join-Path $run '应用 # %25')).FullName
Get-ChildItem -LiteralPath $InstalledRoot -Force | Copy-Item -Destination $stage -Recurse
$shots=[IO.Directory]::CreateDirectory((Join-Path $run 'shots')).FullName
$project=Join-Path $root 'tests/TwinShift.Checks.csproj'
if(!$SkipRestore){& dotnet restore $project --locked-mode;if($LASTEXITCODE){throw 'Test restore failed'}}
& dotnet build $project -c Release --no-restore
if($LASTEXITCODE){throw 'Test build failed'}
Copy-Item (Join-Path $root 'tests/bin/Release/net48/TwinShift.Checks.exe*') $stage
$savedData=$env:TWINSHIFT_DATA_DIR
$savedProfile=$env:CODEX_HOME
try {
    $env:TWINSHIFT_DATA_DIR=Join-Path $run '本地录音 # %25'
    $env:CODEX_HOME=[IO.Directory]::CreateDirectory((Join-Path $run 'codex')).FullName
    $options=@($shots,'--tray')
    if($CheckAudioOutput){$options+='--audio-output'}
    if($CheckCodex){$options+='--codex'}
    if($WallpaperEngine){$options+=@('--wallpaper-engine',(Resolve-Path -LiteralPath $WallpaperEngine).Path)}
    & (Join-Path $stage 'TwinShift.Checks.exe') @options
    if($LASTEXITCODE){throw "Native tests failed. Test root: $run"}
    if($CheckWindowScene){
        & (Join-Path $stage 'TwinShift.Checks.exe') --window-scene
        if($LASTEXITCODE){throw "Visible wallpaper test failed. Test root: $run"}
    }
    & node --test (Join-Path $root 'themes/theme.test.mjs')
    if($LASTEXITCODE){throw 'Palette tests failed'}
    Write-Output "Run and native window screenshots: $run"
} finally {
    $env:TWINSHIFT_DATA_DIR=$savedData
    $env:CODEX_HOME=$savedProfile
}
