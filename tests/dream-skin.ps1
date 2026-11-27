[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$UpstreamRoot,
    [string]$ArchiveDirectory = (Join-Path $PSScriptRoot '..\out\scenes'),
    [string]$PreviousArchiveDirectory
)

$ErrorActionPreference = 'Stop'
$engine = (Resolve-Path -LiteralPath (Join-Path $UpstreamRoot 'windows')).Path
if ((Get-Content -LiteralPath (Join-Path $engine 'VERSION') -Raw).Trim() -ne '1.5.21') {
    throw 'Use the Codex Dream Skin v1.5.21 source tree.'
}
. (Join-Path $engine 'scripts\common-windows.ps1')
. (Join-Path $engine 'scripts\theme-windows.ps1')

# Exercise the stock Windows importer and its duplicate handling in a disposable store.
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testName = 'twin-shift-dream-skin-' + [Guid]::NewGuid().ToString('N')
$testRoot = [IO.Directory]::CreateDirectory((Join-Path $tempBase $testName)).FullName
try {
    foreach ($slot in @('neo', 'evo')) {
        if ($PreviousArchiveDirectory) {
            $oldArchive = (Resolve-Path -LiteralPath (Join-Path $PreviousArchiveDirectory "TwinShift-$slot.zip")).Path
            $old = Import-DreamSkinThemeZip -ArchivePath $oldArchive -StateRoot $testRoot
            if ($old.Status -ne 'Imported' -or $old.Id -ne "twin-shift.$slot") { throw "Previous package setup failed for $slot." }
        }
        $archive = (Resolve-Path -LiteralPath (Join-Path $ArchiveDirectory "TwinShift-$slot.zip")).Path
        $first = Import-DreamSkinThemeZip -ArchivePath $archive -StateRoot $testRoot
        if ($first.Status -ne 'Imported' -or $first.Id -ne "twin-shift.$slot" -or
            $first.PackageFormat -ne 'official' -or $first.SafeCssStatus -ne 'validated') {
            throw "Unexpected importer result for $slot."
        }
        $second = Import-DreamSkinThemeZip -ArchivePath $archive -StateRoot $testRoot
        if ($second.Status -ne 'Duplicate' -or $second.Id -ne $first.Id) {
            throw "Importing $slot twice must preserve the saved theme."
        }
        Write-Output "$($first.Id) imported and deduplicated"
    }
} finally {
    $resolved = (Resolve-Path -LiteralPath $testRoot).Path
    $expected = [IO.Path]::GetFullPath((Join-Path $tempBase $testName))
    if ($resolved -ceq $expected) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
