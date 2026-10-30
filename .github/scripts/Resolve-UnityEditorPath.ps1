# Copyright 2026 Penguin
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#     http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

# Resolves the Editor only after fail-closed runtime prerequisites and optional evidence.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EditorPath,

    [string]$RealEditorPathOutputFile,
    [switch]$RuntimeEvidence,
    [switch]$RequireExistingRuntime,
    [string]$WorkflowRunId,
    [string]$WorkflowRunAttempt,
    [string]$IntendedSha,
    [string]$CheckoutSha
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Invoke-DownloadWithRetry {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][string]$OutFile,
        [ValidateRange(1, 5)][int]$MaximumAttempts = 3
    )

    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        $Detail.downloadAttempt = $attempt
        $failure = $null
        try {
            $response = Invoke-WebRequest -Uri $Uri -OutFile $OutFile -PassThru -ErrorAction Stop
        }
        catch { $failure = $_ }
        if ($null -eq $failure) {
            return $response
        }
        $Detail.downloadErrors += Get-RuntimeFailureData $failure
        $State.payload = if (Test-Path -LiteralPath $OutFile -PathType Leaf) { 'Partial' } else { 'Absent' }
        Save-RuntimeDetail
        if ($attempt -eq $MaximumAttempts) {
            $State.failure = 'DownloadFailed'
            throw 'RuntimeDownloadFailed'
        }
        Write-Warning "RuntimeDownloadRetry attempt=$attempt"
        Start-Sleep -Seconds ([Math]::Pow(2, $attempt))
    }
}

function Assert-PlainPath {
    param([string]$Path, [string]$Root)

    $full = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'PathOutsideRoot' }
    for ($node = $full; $node; $node = [IO.Path]::GetDirectoryName($node)) {
        if (Test-Path -LiteralPath $node) {
            if ((Get-Item -LiteralPath $node -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw 'ReparsePath'
            }
        }
    }
}

function Write-RuntimeJson {
    param([string]$Path, $Value)

    try {
        Assert-PlainPath -Path $Path -Root $env:RUNNER_TEMP
        $json = ConvertTo-Json -InputObject $Value -Depth 30
        Set-Content -LiteralPath $Path -Value $json -Encoding utf8NoBOM -ErrorAction Stop
    }
    catch {
        $_.Exception.Data['RuntimeSaveFailure'] = if ($Path -eq $ReportPath) { 'ReportSaveFailed' } else { 'PrivateSaveFailed' }
        throw
    }
}

function Add-RuntimeEvent {
    param([string]$Stage)

    $State.stage = $Stage
    $Detail.events += [ordered]@{ stage = $Stage; utc = [DateTime]::UtcNow.ToString('o') }
}

function Save-RuntimeDetail {
    if ($RuntimeEvidence) {
        $Detail.state = $State
        Write-RuntimeJson -Path (Join-Path $RuntimeRoot 'metadata.json') -Value $Detail
    }
}

function Save-RuntimeReport {
    if ($RuntimeEvidence) {
        Write-RuntimeJson -Path $ReportPath -Value $Report
    }
}

function Initialize-RuntimeEvidence {
    if (-not $RuntimeEvidence) { return }
    if ($WorkflowRunId -notmatch '^[0-9]+$' -or $WorkflowRunAttempt -notmatch '^[1-9][0-9]*$' -or
        $IntendedSha -notmatch '^[0-9a-f]{40}$' -or $CheckoutSha -notmatch '^[0-9a-f]{40}$') {
        throw 'RuntimeEvidenceIdentityInvalid'
    }
    foreach ($path in @($PublicRoot, $PrivateRoot)) {
        Assert-PlainPath -Path $path -Root $env:RUNNER_TEMP
        if (Test-Path -LiteralPath $path) { throw 'RuntimeEvidenceCollision' }
    }
    foreach ($path in @($PublicRoot, $PrivateRoot)) {
        New-Item -ItemType Directory -Path $path -ErrorAction Stop | Out-Null
        Assert-PlainPath -Path $path -Root $env:RUNNER_TEMP
    }
    $provenance = [ordered]@{
        run = $WorkflowRunId; attempt = $WorkflowRunAttempt; intendedSha = $IntendedSha; checkoutSha = $CheckoutSha
        utc = [DateTime]::UtcNow.ToString('o'); utcFormat = 'ISO8601 round-trip, 100ns representation; clock accuracy unknown'
        pwsh = $PSVersionTable; os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        job = $env:GITHUB_JOB; runnerName = $env:RUNNER_NAME; runnerOS = $env:RUNNER_OS
        runnerArch = $env:RUNNER_ARCH; imageOS = $env:ImageOS; imageVersion = $env:ImageVersion
        missingMetadata = 'Unknown: not supplied by environment'; transfer = 'NotReached: no authorized transfer'
        upload = 'Unknown: external workflow outcome'; runnerLoss = 'Unknown: cannot observe after process loss'
    }
    Write-RuntimeJson -Path (Join-Path $PrivateRoot 'provenance.json') -Value $provenance
    Save-RuntimeReport
}

function Get-ApiField {
    param($Object, [string]$Name)

    if ($null -eq $Object) { return $null }
    if ($Object -is [Collections.IDictionary]) { return $Object[$Name] }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -ne $property) { return $property.Value }
    return $null
}

function Get-RuntimeFailureData {
    param([Management.Automation.ErrorRecord]$Record)

    return [ordered]@{
        exception = $Record.Exception.ToString(); errorRecord = $Record.ToString()
        errorId = $Record.FullyQualifiedErrorId; category = $Record.CategoryInfo.ToString()
        scriptStackTrace = $Record.ScriptStackTrace; errorDetails = [string]$Record.ErrorDetails
        position = $Record.InvocationInfo.PositionMessage; line = $Record.InvocationInfo.Line
        exceptionType = $Record.Exception.GetType().FullName
    }
}

function Get-RuntimeHttpMetadata {
    param($Response, [string]$Uri)

    $baseResponse = Get-ApiField $Response 'BaseResponse'
    $request = Get-ApiField $baseResponse 'RequestMessage'
    $headers = Get-ApiField $Response 'Headers'
    $selected = [ordered]@{}
    foreach ($name in @('Content-Type', 'Content-Length', 'Content-Encoding', 'Date', 'Last-Modified', 'ETag')) {
        $selected[$name] = Get-ApiField $headers $name
    }
    return [ordered]@{
        requestUri = $Uri; finalUri = [string](Get-ApiField $request 'RequestUri')
        statusCode = Get-ApiField $Response 'StatusCode'; headers = $selected
        absentFields = 'NotProvidedByApi'; redirectHistory = 'NotProvidedByApi'
    }
}

function Save-RuntimeCertificate {
    param($Certificate, [string]$Name)

    if ($null -eq $Certificate) { return $null }
    $bytes = [byte[]]$Certificate.RawData
    if ($RuntimeEvidence) {
        $path = Join-Path $RuntimeRoot "$Name.der"
        Assert-PlainPath -Path $path -Root $RuntimeRoot
        [IO.File]::WriteAllBytes($path, $bytes)
    }
    return [ordered]@{
        file = "$Name.der"; length = $bytes.LongLength
        sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
        subject = $Certificate.Subject; issuer = $Certificate.Issuer; serialNumber = $Certificate.SerialNumber
        thumbprint = $Certificate.Thumbprint; notBefore = $Certificate.NotBefore.ToString('o'); notAfter = $Certificate.NotAfter.ToString('o')
    }
}

function Save-RuntimeSignature {
    param($Signature)

    $numericStatus = $null
    try { $numericStatus = [int]$Signature.Status } catch { $numericStatus = $null }
    $record = [ordered]@{
        status = [string]$Signature.Status; statusCode = $numericStatus; statusMessage = $Signature.StatusMessage
        signatureType = $Signature.SignatureType; isOSBinary = $Signature.IsOSBinary; path = $Signature.Path
        signerCertificate = Save-RuntimeCertificate $Signature.SignerCertificate 'signer'
        timeStamperCertificate = Save-RuntimeCertificate $Signature.TimeStamperCertificate 'timestamp'
        chain = 'NotProvidedByApi'; winVerifyTrustCode = 'NotProvidedByApi'; revocationHistory = 'NotProvidedByApi'
    }
    if ($RuntimeEvidence) { Write-RuntimeJson -Path (Join-Path $RuntimeRoot 'signature.json') -Value $record }
}

function Copy-ProtectedRuntime {
    param([IO.FileStream]$Handle, [string]$Path)

    Assert-PlainPath -Path $Path -Root $RuntimeRoot
    $State.length = $Handle.Length
    $State.sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Handle)).ToLowerInvariant()
    $Handle.Position = 0
    if (-not $RuntimeEvidence) { return }
    $copy = Join-Path $RuntimeRoot 'installer.bytes'
    Assert-PlainPath -Path $copy -Root $RuntimeRoot
    if (Test-Path -LiteralPath $copy) { throw 'RuntimeCopyCollision' }
    Copy-Item -LiteralPath $Path -Destination $copy -ErrorAction Stop
    Assert-PlainPath -Path $copy -Root $RuntimeRoot
    $copyHash = (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash.ToLowerInvariant()
    if ((Get-Item -LiteralPath $copy).Length -ne $State.length -or $copyHash -cne $State.sha256) {
        throw 'RuntimeCopyMismatch'
    }
    $Detail.source = [IO.Path]::GetFileName($Path)
    $Detail.copy = 'installer.bytes'
}

function Read-RuntimeSignature {
    param([string]$Path)

    Add-RuntimeEvent 'Signature'
    $signature = $null
    try { $signature = Get-AuthenticodeSignature -LiteralPath $Path -ErrorAction Stop }
    catch {
        $State.signature = 'ResultNotReturned'
        $State.failure = if ($_.Exception -is [Management.Automation.CommandNotFoundException]) { 'SignatureUnavailable' } else { 'SignatureNotReturned' }
        $Detail.signatureError = Get-RuntimeFailureData $_
        return
    }
    $State.signature = 'Unknown'
    $known = @('Valid', 'UnknownError', 'NotSigned', 'HashMismatch', 'NotTrusted', 'NotSupportedFileFormat', 'Incompatible')
    if ([string]$signature.Status -cin $known) { $State.signature = [string]$signature.Status }
    $State.failure = 'SignatureRejected'
    if ([string]$signature.Status -ceq 'Valid' -and $null -ne $signature.SignerCertificate -and
        $signature.SignerCertificate.Subject -like '*Microsoft Corporation*') { $State.failure = 'None' }
    Add-RuntimeEvent 'SignatureReturned'
    Save-RuntimeSignature $signature
}

function Install-ProtectedRuntime {
    param([string]$Path, [string[]]$Arguments, [string[]]$RequiredFiles)

    Add-RuntimeEvent 'Installer'
    Save-RuntimeDetail
    Save-RuntimeReport
    $State.installer = 'StartRequested'
    try {
        $process = Start-Process -FilePath $Path -ArgumentList $Arguments -Wait -PassThru -NoNewWindow -ErrorAction Stop
    }
    catch { $State.failure = 'InstallerStartFailed'; throw }
    $State.installer = 'Returned'
    $State.exitCode = [int]$process.ExitCode
    Add-RuntimeEvent 'InstallerReturned'
    $State.failure = 'InstallerExitRejected'
    if ($State.exitCode -notin @(0, 1638, 3010)) { return }
    $State.files = @($RequiredFiles | ForEach-Object { [bool](Test-Path -LiteralPath $_ -PathType Leaf) })
    $State.failure = if ($State.files -contains $false) { 'RuntimeFilesMissing' } else { 'None' }
    Add-RuntimeEvent 'Postcheck'
}

function Resolve-WindowsAbsolutePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $candidate = $Path.Trim().Trim('"')
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        throw 'Unity Editor path output was empty.'
    }

    # setup-unity-cli may report an MSYS-style /c/... path even though this script
    # runs in native Windows PowerShell. Convert only that well-defined drive form,
    # then let System.IO perform all remaining canonicalization and validation.
    if ($candidate -match '^[/\\]([A-Za-z])(?:[/\\](.*))?$') {
        $drive = $Matches[1].ToUpperInvariant()
        $tail = if ($Matches.Count -gt 2 -and $null -ne $Matches[2]) { $Matches[2] } else { '' }
        $candidate = '{0}:\{1}' -f $drive, $tail.Replace('/', '\')
    }

    return [System.IO.Path]::GetFullPath($candidate)
}

function New-RuntimeInstallerPath {
    param([string]$Root, [string]$FileName)

    New-Item -ItemType Directory -Path $Root -ErrorAction Stop | Out-Null
    $path = Join-Path $Root $FileName
    Assert-PlainPath -Path $path -Root $Root
    return $path
}

function Assert-MicrosoftRuntime {
    param(
        [Parameter(Mandatory = $true)][string]$DisplayName,
        [Parameter(Mandatory = $true)][string[]]$RequiredFiles,
        [Parameter(Mandatory = $true)][string]$InstallerFileName,
        [Parameter(Mandatory = $true)][string]$InstallerUri,
        [Parameter(Mandatory = $true)][string[]]$InstallerArguments
    )

    $State = $Report.runtimes[$InstallerFileName]
    $RuntimeRoot = Join-Path $PrivateRoot $State.runtime
    $Detail = [ordered]@{ events = @(); downloadAttempt = 0; downloadErrors = @(); source = $InstallerFileName; copy = $null }
    $handle = $null
    try {
        Add-RuntimeEvent 'Prerequisite'
        $State.files = @($RequiredFiles | ForEach-Object { [bool](Test-Path -LiteralPath $_ -PathType Leaf) })
        if ($State.files -notcontains $false) { $State.status = 'ExistingRuntime' }
        else {
            if ($RequireExistingRuntime) { $State.failure = 'RuntimeFilesMissing'; throw 'RuntimeFilesMissing' }
            $installerPath = New-RuntimeInstallerPath -Root $RuntimeRoot -FileName $InstallerFileName
            Add-RuntimeEvent 'Download'
            $response = Invoke-DownloadWithRetry -Uri $InstallerUri -OutFile $installerPath
            $State.status = 'Downloaded'
            $State.payload = 'Incomplete'
            Add-RuntimeEvent 'Downloaded'
            $Detail.http = Get-RuntimeHttpMetadata -Response $response -Uri $InstallerUri
            $handle = [IO.File]::Open($installerPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
            Copy-ProtectedRuntime -Handle $handle -Path $installerPath
            Save-RuntimeDetail
            Read-RuntimeSignature -Path $installerPath
            $State.payload = 'Complete'
            Save-RuntimeDetail
            Save-RuntimeReport
            if ($State.failure -eq 'None') {
                Install-ProtectedRuntime -Path $installerPath -Arguments $InstallerArguments -RequiredFiles $RequiredFiles
            }
        }
    }
    catch {
        $Detail.error = Get-RuntimeFailureData $_
        if ($State.payload -ne 'Partial') { $State.payload = 'Incomplete' }
        if ($_.Exception.Data['RuntimeSaveFailure']) { $State.secondary = $_.Exception.Data['RuntimeSaveFailure'] }
        elseif ($State.stage -in @('Signature', 'SignatureReturned')) { $State.secondary = 'PrivateSaveFailed' }
        if ($State.failure -eq 'None') { $State.failure = 'EvidenceIncomplete' }
    }
    finally { if ($null -ne $handle) { $handle.Dispose() } }
    Complete-RuntimeEvidence
}

function Complete-RuntimeEvidence {
    Add-RuntimeEvent 'SaveRequested'
    try {
        if ($State.status -ne 'ExistingRuntime') { Save-RuntimeDetail }
    }
    catch { $State.secondary = 'PrivateSaveFailed'; $State.payload = 'Incomplete' }
    if ($State.failure -eq 'None' -and $State.secondary -ne 'None') { $State.failure = 'EvidenceIncomplete' }
    try { Save-RuntimeReport }
    catch { $State.secondary = 'ReportSaveFailed'; $State.payload = 'Incomplete' }
    if ($State.failure -eq 'None' -and $State.secondary -ne 'None') { $State.failure = 'EvidenceIncomplete' }
    if ($State.failure -ne 'None') {
        throw [InvalidOperationException]::new("RuntimePrerequisiteFailed: $($State.failure); secondary=$($State.secondary)")
    }
}

$suffix = if ($RuntimeEvidence) { "$WorkflowRunId-$WorkflowRunAttempt" } else { [guid]::NewGuid().ToString('N') }
$PrivateRoot = Join-Path $env:RUNNER_TEMP "PureBase-Runtime-Prerequisites-Private-$suffix"
$PublicRoot = Join-Path $env:RUNNER_TEMP "PureBase-Release-Validation-$suffix/runtime-prerequisites"
$ReportPath = Join-Path $PublicRoot 'report.json'
$Report = [ordered]@{
    schemaVersion = 1; run = $WorkflowRunId; attempt = $WorkflowRunAttempt
    intendedSha = $IntendedSha; checkoutSha = $CheckoutSha; runtimes = [ordered]@{}
}
foreach ($id in @('2010-sp1-x64', '2013-x64')) {
    $Report.runtimes["vcredist-$id.exe"] = [ordered]@{
        runtime = $id; stage = 'NotReached'; status = 'NotReached'; signature = 'NotReached'
        installer = 'NotReached'; exitCode = $null; files = @(); length = $null; sha256 = $null
        payload = 'Absent'; failure = 'None'; secondary = 'None'; transfer = 'Withheld'
    }
}
try { Initialize-RuntimeEvidence } catch { throw [InvalidOperationException]::new('RuntimeEvidenceInitializationFailed') }

$runtimeDirectory = Join-Path $env:WINDIR 'System32'
Assert-MicrosoftRuntime `
    -DisplayName 'Microsoft Visual C++ 2010 SP1 x64 runtime' `
    -RequiredFiles @(
    (Join-Path $runtimeDirectory 'msvcr100.dll'),
    (Join-Path $runtimeDirectory 'msvcp100.dll')
) `
    -InstallerFileName 'vcredist-2010-sp1-x64.exe' `
    -InstallerUri 'https://download.microsoft.com/download/1/6/5/165255E7-1014-4D0A-B094-B6A430A6BFFC/vcredist_x64.exe' `
    -InstallerArguments @('/quiet', '/norestart')

Assert-MicrosoftRuntime `
    -DisplayName 'Microsoft Visual C++ 2013 x64 runtime' `
    -RequiredFiles @(
    (Join-Path $runtimeDirectory 'msvcr120.dll'),
    (Join-Path $runtimeDirectory 'msvcp120.dll')
) `
    -InstallerFileName 'vcredist-2013-x64.exe' `
    -InstallerUri 'https://download.microsoft.com/download/0/5/6/056DCDA9-D667-4E27-8001-8A0C6971D6B1/vcredist_x64.exe' `
    -InstallerArguments @('/install', '/quiet', '/norestart')

$normalizedPath = Resolve-WindowsAbsolutePath -Path $EditorPath
if (-not (Test-Path -LiteralPath $normalizedPath -PathType Leaf)) {
    throw "Unity Editor executable was not found at '$normalizedPath' (raw output: '$EditorPath')."
}

if ([System.IO.Path]::GetFileName($normalizedPath) -ine 'Unity.exe') {
    throw "Resolved Unity Editor path does not point to Unity.exe: '$normalizedPath'."
}

# Configure and Daily use the watchdog proxy, while release validation retains the
# real Unity.exe because its audited runner intentionally rejects wrapper executables.
if (-not [string]::IsNullOrWhiteSpace($RealEditorPathOutputFile)) {
    $realEditorOutputPath = [System.IO.Path]::GetFullPath($RealEditorPathOutputFile)
    $realEditorOutputDirectory = Split-Path -Parent $realEditorOutputPath
    if (-not [string]::IsNullOrEmpty($realEditorOutputDirectory)) {
        New-Item -ItemType Directory -Path $realEditorOutputDirectory -Force | Out-Null
    }
    [System.IO.File]::WriteAllText(
        $realEditorOutputPath,
        $normalizedPath + "`n",
        [System.Text.UTF8Encoding]::new($false)
    )
    Write-Host "Real Unity Editor path output: $realEditorOutputPath"
}

$proxyScriptPath = Join-Path $PSScriptRoot 'UnityWatchdogProxy.ps1'
if (-not (Test-Path -LiteralPath $proxyScriptPath -PathType Leaf)) {
    throw "Unity watchdog proxy script was not found at '$proxyScriptPath'."
}

$pwshPath = (Get-Command pwsh -ErrorAction Stop).Source
$proxyRoot = Join-Path $env:RUNNER_TEMP 'PureBaseUnityProxy/2022.3.28f1/Editor'
New-Item -ItemType Directory -Path $proxyRoot -Force | Out-Null
$proxyCommandPath = Join-Path $proxyRoot 'Unity.cmd'
$proxyCommand = @"
@echo off
"$pwshPath" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "$proxyScriptPath" -UnityEditorPath "$normalizedPath" %*
exit /b %ERRORLEVEL%
"@
[System.IO.File]::WriteAllText(
    $proxyCommandPath,
    $proxyCommand.Replace("`n", "`r`n"),
    [System.Text.ASCIIEncoding]::new()
)

Write-Host "Unity watchdog proxy: $proxyCommandPath"
Write-Host "Unity watchdog target: $normalizedPath"
Write-Output $proxyCommandPath

