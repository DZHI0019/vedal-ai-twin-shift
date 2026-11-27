[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Preflight','Sign','Verify')][string]$Mode,
    [string]$Path,
    [string]$SignTool = $env:TWINSHIFT_SIGNTOOL,
    [ValidatePattern('^[a-fA-F0-9]{40}$')][string]$SignerThumbprint = $env:TWINSHIFT_SIGNER
)
$ErrorActionPreference = 'Stop'
if (!$SignerThumbprint -or !$SignTool) { throw 'A project-authorized publisher thumbprint and Windows SDK SignTool are required.' }
$sdk = (Get-Item -LiteralPath $SignTool).FullName
if ([IO.Path]::GetFileName($sdk) -ine 'signtool.exe') { throw 'Select signtool.exe from the Microsoft Windows SDK.' }

if ($Mode -ne 'Verify') {
    $cert = Get-Item -LiteralPath "Cert:\CurrentUser\My\$SignerThumbprint"
    if (!$cert.HasPrivateKey -or $cert.Subject -eq $cert.Issuer -or
        $cert.NotBefore -gt (Get-Date) -or $cert.NotAfter -le (Get-Date)) {
        throw 'The selected publisher needs a current CA-issued code signing certificate with access to its private key.'
    }
    $chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
    try {
        $chain.ChainPolicy.ApplicationPolicy.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3')) | Out-Null
        $chain.ChainPolicy.UrlRetrievalTimeout = [TimeSpan]::FromSeconds(20)
        if (!$chain.Build($cert)) { throw 'Windows could not verify the publisher chain and revocation status.' }
        $rootCert = $chain.ChainElements[$chain.ChainElements.Count - 1].Certificate
        if (!(Test-Path -LiteralPath "Cert:\LocalMachine\AuthRoot\$($rootCert.Thumbprint)")) {
            throw 'The publisher chain must end in a Windows Third-Party Root Certification Authorities root.'
        }
    } finally {
        $chain.Dispose()
    }
}
if ($Mode -eq 'Preflight') { return }
$file = (Get-Item -LiteralPath $Path).FullName
if ($Mode -eq 'Sign') {
    & $sdk sign /q /s My /sha1 $SignerThumbprint /fd SHA256 /tr 'http://timestamp.digicert.com' /td SHA256 $file
    if ($LASTEXITCODE -ne 0) { throw 'Authenticode signing or RFC 3161 timestamping failed.' }
}
$signature = Get-AuthenticodeSignature -LiteralPath $file
if ($signature.Status -ne 'Valid' -or $signature.SignatureType -ne 'Authenticode' -or
    $signature.SignerCertificate.Thumbprint -ne $SignerThumbprint -or
    $signature.SignerCertificate.Subject -eq $signature.SignerCertificate.Issuer -or !$signature.TimeStamperCertificate) {
    throw 'Expected an embedded, trusted, timestamped signature from the selected publisher.'
}
& $sdk verify /q /pa /all /tw $file
if ($LASTEXITCODE -ne 0) { throw 'Windows Authenticode validation failed or returned a timestamp warning.' }
