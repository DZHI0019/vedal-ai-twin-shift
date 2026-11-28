[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SignTool,
    [Parameter(Mandatory)][string]$UnsignedBinary
)
$ErrorActionPreference = 'Stop'
$verify = Join-Path $PSScriptRoot '../tools/windows-signature.ps1'
$sdk = (Resolve-Path -LiteralPath $SignTool).Path
$publisher = (Get-AuthenticodeSignature -LiteralPath $sdk).SignerCertificate.Thumbprint
& $verify -Mode Verify -Path $sdk -SignTool $sdk -SignerThumbprint $publisher
foreach ($case in @(
    @{ Path = $UnsignedBinary; Publisher = $publisher },
    @{ Path = $sdk; Publisher = ('0' * 40) }
)) {
    $rejected = $false
    try {
        & $verify -Mode Verify -Path $case.Path -SignTool $sdk -SignerThumbprint $case.Publisher
    } catch {
        $rejected = $true
    }
    if (!$rejected) { throw 'A signature verification negative control was accepted.' }
}
Write-Output 'Verified SDK signature and rejected both negative controls.'
