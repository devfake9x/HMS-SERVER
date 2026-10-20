[CmdletBinding()]
param(
    [string]$FirmwareProto = 'D:\WORKSPACE\PROJECT\HMS-IDF\protobuf\proto\license.proto',
    [string]$PinnedProto = 'D:\WORKSPACE\PROJECT\HMS-SERVER\plans\260924-2044-hms-server-management\contracts\license\license.proto'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $FirmwareProto)) {
    throw "Firmware license schema not found: $FirmwareProto"
}
if (-not (Test-Path -LiteralPath $PinnedProto)) {
    throw "Pinned license schema not found: $PinnedProto"
}

$sourceHash = (Get-FileHash -LiteralPath $FirmwareProto -Algorithm SHA256).Hash.ToLowerInvariant()
$pinnedHash = (Get-FileHash -LiteralPath $PinnedProto -Algorithm SHA256).Hash.ToLowerInvariant()
$sameBytes = [System.Linq.Enumerable]::SequenceEqual(
    [System.IO.File]::ReadAllBytes($FirmwareProto),
    [System.IO.File]::ReadAllBytes($PinnedProto)
)

$result = [ordered]@{
    evidence = if ($sameBytes) { 'CONTRACT_TESTED' } else { 'FAIL' }
    source = $FirmwareProto
    pinned = $PinnedProto
    sourceSha256 = $sourceHash
    pinnedSha256 = $pinnedHash
    byteExact = $sameBytes
    limitation = 'This proves the pinned copy matches the selected workspace source; it does not prove a firmware build used this schema.'
}
$result | ConvertTo-Json -Depth 5

if (-not $sameBytes) {
    exit 1
}
