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

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-PureBaseSemVer {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Value)

    if ($Value -notmatch '^(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-(?<prerelease>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$') {
        throw "Value is not a strict unprefixed SemVer 2 version: '$Value'."
    }

    $major = $Matches['major']
    $minor = $Matches['minor']
    $patch = $Matches['patch']
    $prereleaseText = $Matches['prerelease']
    $prerelease = @()
    if (-not [string]::IsNullOrEmpty($prereleaseText)) {
        $prerelease = @($prereleaseText.Split('.'))
        foreach ($identifier in $prerelease) {
            if ($identifier -match '^[0-9]+$' -and $identifier.Length -gt 1 -and $identifier.StartsWith('0', [StringComparison]::Ordinal)) {
                throw "Numeric prerelease identifiers must not contain leading zeroes: '$Value'."
            }
        }
    }

    return [pscustomobject][ordered]@{
        original    = $Value
        major       = $major
        minor       = $minor
        patch       = $patch
        prerelease  = $prerelease
        isPrerelease = $prerelease.Count -ne 0
        prereleaseKind = if ($prerelease.Count -eq 0) { '' } else { $prerelease[0] }
    }
}

function Compare-PureBaseSemVerNumericIdentifier {
    param(
        [Parameter(Mandatory)][string]$Left,
        [Parameter(Mandatory)][string]$Right
    )

    if ($Left.Length -ne $Right.Length) {
        return [Math]::Sign($Left.Length - $Right.Length)
    }
    return [Math]::Sign([string]::Compare($Left, $Right, [StringComparison]::Ordinal))
}

function Compare-PureBaseSemVer {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Left,
        [Parameter(Mandatory)][string]$Right
    )

    $leftVersion = ConvertTo-PureBaseSemVer -Value $Left
    $rightVersion = ConvertTo-PureBaseSemVer -Value $Right
    foreach ($part in @('major', 'minor', 'patch')) {
        $comparison = Compare-PureBaseSemVerNumericIdentifier -Left ([string]$leftVersion.$part) -Right ([string]$rightVersion.$part)
        if ($comparison -ne 0) { return $comparison }
    }

    if (-not $leftVersion.isPrerelease -and -not $rightVersion.isPrerelease) { return 0 }
    if (-not $leftVersion.isPrerelease) { return 1 }
    if (-not $rightVersion.isPrerelease) { return -1 }
    $commonLength = [Math]::Min($leftVersion.prerelease.Count, $rightVersion.prerelease.Count)
    for ($index = 0; $index -lt $commonLength; $index++) {
        $leftIdentifier = [string]$leftVersion.prerelease[$index]
        $rightIdentifier = [string]$rightVersion.prerelease[$index]
        $leftNumeric = $leftIdentifier -match '^[0-9]+$'
        $rightNumeric = $rightIdentifier -match '^[0-9]+$'
        if ($leftNumeric -and $rightNumeric) {
            $comparison = Compare-PureBaseSemVerNumericIdentifier -Left $leftIdentifier -Right $rightIdentifier
        }
        elseif ($leftNumeric) { $comparison = -1 }
        elseif ($rightNumeric) { $comparison = 1 }
        else { $comparison = [Math]::Sign([string]::Compare($leftIdentifier, $rightIdentifier, [StringComparison]::Ordinal)) }
        if ($comparison -ne 0) { return $comparison }
    }
    return [Math]::Sign($leftVersion.prerelease.Count - $rightVersion.prerelease.Count)
}

function Resolve-PureBaseReleaseMode {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$CurrentVersion,
        [Parameter(Mandatory)][string]$TargetVersion,
        [Parameter()][switch]$Resume,
        [Parameter()][AllowEmptyString()][string]$ExistingTagSha = '',
        [Parameter()][AllowNull()]$ExistingRelease = $null
    )

    $current = ConvertTo-PureBaseSemVer -Value $CurrentVersion
    $target = ConvertTo-PureBaseSemVer -Value $TargetVersion
    $releaseState = if ($null -eq $ExistingRelease) {
        'none'
    }
    elseif ([bool]$ExistingRelease.draft) {
        'draft'
    }
    else {
        'published'
    }
    if ($null -ne $ExistingRelease) {
        $prereleaseProperty = $ExistingRelease.PSObject.Properties['prerelease']
        if ($null -eq $prereleaseProperty -or $prereleaseProperty.Value -isnot [bool] -or [bool]$prereleaseProperty.Value -ne [bool]$target.isPrerelease) {
            throw "Existing release prerelease state does not match target '$TargetVersion'."
        }
    }

    if ($Resume) {
        if (-not [string]::Equals($target.original, $current.original, [StringComparison]::Ordinal)) {
            throw 'Resume is valid only when update_trigger.json and package.json versions are equal.'
        }
        return [pscustomobject][ordered]@{
            Mode           = 'resume'
            CurrentVersion = $CurrentVersion
            TargetVersion  = $TargetVersion
            PrereleaseKind = $target.prereleaseKind
            TagState       = if ([string]::IsNullOrEmpty($ExistingTagSha)) { 'missing' } else { 'present' }
            ReleaseState   = $releaseState
        }
    }

    if ((Compare-PureBaseSemVer -Left $target.original -Right $current.original) -le 0) {
        throw "update_trigger.json '$TargetVersion' must be newer than package.json '$CurrentVersion'."
    }
    if (-not [string]::IsNullOrEmpty($ExistingTagSha) -or $null -ne $ExistingRelease) {
        throw "Tag or release '$TargetVersion' already exists."
    }

    return [pscustomobject][ordered]@{
        Mode           = 'fresh'
        CurrentVersion = $CurrentVersion
        TargetVersion  = $TargetVersion
        PrereleaseKind = $target.prereleaseKind
        TagState       = 'missing'
        ReleaseState   = 'none'
    }
}

function Resolve-PureBaseResumeTagAction {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$HeadSha,
        [Parameter()][AllowEmptyString()][string]$ExistingTagSha = ''
    )

    if ([string]::IsNullOrWhiteSpace($HeadSha)) {
        throw 'Resume requires a non-empty HEAD commit SHA.'
    }
    if ([string]::IsNullOrEmpty($ExistingTagSha)) {
        throw 'Resume requires the annotated release tag; it must exist.'
    }
    if (-not [string]::Equals($ExistingTagSha, $HeadSha, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The existing release tag points to a different commit.'
    }
    return 'reuse'
}

function Invoke-PureBaseGit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PackageRoot,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter()][switch]$AllowFailure
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.WorkingDirectory = [IO.Path]::GetFullPath($PackageRoot)
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw 'Failed to start Git.'
        }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout = $stdoutTask.GetAwaiter().GetResult().Trim()
        $stderr = $stderrTask.GetAwaiter().GetResult().Trim()
        $exitCode = $process.ExitCode
    }
    finally {
        $process.Dispose()
    }

    if ($exitCode -ne 0 -and -not $AllowFailure) {
        $details = @($stderr, $stdout) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        throw "git $($Arguments -join ' ') failed with exit code ${exitCode}:`n$($details -join "`n")"
    }

    return [pscustomobject][ordered]@{
        ExitCode = $exitCode
        Output   = $stdout
        Error    = $stderr
    }
}

function New-PureBasePackageUrl {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$AssetName
    )

    if ($Repository -notmatch '^[^/]+/[^/]+$') {
        throw 'Repository must use owner/name form.'
    }
    [void](ConvertTo-PureBaseSemVer -Value $Version)
    if ([string]::IsNullOrWhiteSpace($AssetName) -or $AssetName -match '[/\\]') {
        throw 'AssetName must be one file name without path separators.'
    }

    $encodedAssetName = [Uri]::EscapeDataString($AssetName)
    return "https://github.com/$Repository/releases/download/$Version/$encodedAssetName"
}

function New-PureBaseDispatchPayload {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PackageName,
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$CommitSha,
        [Parameter(Mandatory)][string]$PolicyCommitSha,
        [Parameter(Mandatory)][string]$AssetName,
        [Parameter(Mandatory)][string]$Sha256,
        [Parameter(Mandatory)][string]$ReleaseUrl
    )

    return [ordered]@{
        event_type     = 'update-vpm'
        client_payload = [ordered]@{
            packageName      = $PackageName
            version          = $Version
            tag              = $Version
            commitSha        = $CommitSha
            policyCommitSha  = $PolicyCommitSha
            assetName        = $AssetName
            packageurl       = New-PureBasePackageUrl -Repository $Repository -Version $Version -AssetName $AssetName
            sha256           = $Sha256
            releaseUrl       = $ReleaseUrl
            sourceRepository = $Repository
        }
    }
}

function Move-PureBaseJsonWhitespace {
    param([Parameter(Mandatory)][string]$Text, [Parameter(Mandatory)][ref]$Position)
    while ($Position.Value -lt $Text.Length -and $Text[$Position.Value] -match '[ \t\r\n]') { $Position.Value++ }
}

function Read-PureBaseJsonString {
    param([Parameter(Mandatory)][string]$Text, [Parameter(Mandatory)][ref]$Position)
    if ($Position.Value -ge $Text.Length -or $Text[$Position.Value] -ne '"') { throw 'Expected a JSON string.' }
    $Position.Value++
    $builder = [Text.StringBuilder]::new()
    while ($Position.Value -lt $Text.Length) {
        $character = $Text[$Position.Value]
        $Position.Value++
        if ($character -eq '"') { return $builder.ToString() }
        if ([int][char]$character -lt 0x20) { throw 'JSON strings cannot contain control characters.' }
        if ($character -ne '\') { [void]$builder.Append($character); continue }
        if ($Position.Value -ge $Text.Length) { throw 'JSON string ends after an escape character.' }
        $escape = $Text[$Position.Value]
        $Position.Value++
        switch ($escape) {
            '"' { [void]$builder.Append('"') }
            '\' { [void]$builder.Append('\') }
            '/' { [void]$builder.Append('/') }
            'b' { [void]$builder.Append([char]8) }
            'f' { [void]$builder.Append([char]12) }
            'n' { [void]$builder.Append("`n") }
            'r' { [void]$builder.Append("`r") }
            't' { [void]$builder.Append("`t") }
            'u' {
                if ($Position.Value + 4 -gt $Text.Length) { throw 'JSON Unicode escape is incomplete.' }
                $hex = $Text.Substring($Position.Value, 4)
                if ($hex -notmatch '^[0-9A-Fa-f]{4}$') { throw 'JSON Unicode escape is invalid.' }
                [void]$builder.Append([char][Convert]::ToInt32($hex, 16))
                $Position.Value += 4
            }
            default { throw 'JSON string contains an invalid escape sequence.' }
        }
    }
    throw 'JSON string is not terminated.'
}

function Read-PureBaseJsonPrimitive {
    param([Parameter(Mandatory)][string]$Text, [Parameter(Mandatory)][ref]$Position)
    $start = $Position.Value
    while ($Position.Value -lt $Text.Length -and $Text[$Position.Value] -notmatch '[,}\]\s]') { $Position.Value++ }
    if ($start -eq $Position.Value) { throw 'Expected a JSON primitive.' }
    return $Text.Substring($start, $Position.Value - $start)
}

function Read-PureBaseVpmYankPolicy {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -gt 65536) { throw 'VPM yank policy must not exceed 64 KiB.' }
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { throw 'VPM yank policy must not include a UTF-8 BOM.' }
    try { $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes) }
    catch { throw 'VPM yank policy must be valid UTF-8.' }

    $position = 0
    Move-PureBaseJsonWhitespace -Text $text -Position ([ref]$position)
    if ($position -ge $text.Length -or $text[$position] -ne '{') { throw 'VPM yank policy must be a JSON object.' }
    $position++
    $keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $versions = [ordered]@{}
    $schemaVersion = $null
    $package = $null
    while ($true) {
        Move-PureBaseJsonWhitespace -Text $text -Position ([ref]$position)
        if ($position -lt $text.Length -and $text[$position] -eq '}') { $position++; break }
        $key = Read-PureBaseJsonString -Text $text -Position ([ref]$position)
        if (-not $keys.Add($key)) { throw "VPM yank policy has duplicate key '$key'." }
        Move-PureBaseJsonWhitespace -Text $text -Position ([ref]$position)
        if ($position -ge $text.Length -or $text[$position] -ne ':') { throw 'VPM yank policy JSON object is missing a colon.' }
        $position++
        Move-PureBaseJsonWhitespace -Text $text -Position ([ref]$position)
        switch ($key) {
            'schemaVersion' {
                $schemaVersion = Read-PureBaseJsonPrimitive -Text $text -Position ([ref]$position)
                if ($schemaVersion -ne '1') { throw "Unsupported VPM yank policy schemaVersion '$schemaVersion'." }
            }
            'package' { $package = Read-PureBaseJsonString -Text $text -Position ([ref]$position) }
            'versions' {
                if ($position -ge $text.Length -or $text[$position] -ne '{') { throw 'VPM yank policy versions must be an object.' }
                $position++
                $versionKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                while ($true) {
                    Move-PureBaseJsonWhitespace -Text $text -Position ([ref]$position)
                    if ($position -lt $text.Length -and $text[$position] -eq '}') { $position++; break }
                    $version = Read-PureBaseJsonString -Text $text -Position ([ref]$position)
                    if (-not $versionKeys.Add($version)) { throw "VPM yank policy has duplicate version '$version'." }
                    [void](ConvertTo-PureBaseSemVer -Value $version)
                    Move-PureBaseJsonWhitespace -Text $text -Position ([ref]$position)
                    if ($position -ge $text.Length -or $text[$position] -ne ':') { throw 'VPM yank policy version entry is missing a colon.' }
                    $position++
                    Move-PureBaseJsonWhitespace -Text $text -Position ([ref]$position)
                    $reason = Read-PureBaseJsonString -Text $text -Position ([ref]$position)
                    if ([string]::IsNullOrWhiteSpace($reason)) { throw "VPM yank policy reason for '$version' must not be blank." }
                    $versions[$version] = $reason
                    Move-PureBaseJsonWhitespace -Text $text -Position ([ref]$position)
                    if ($position -lt $text.Length -and $text[$position] -eq ',') {
                        $position++
                        Move-PureBaseJsonWhitespace -Text $text -Position ([ref]$position)
                        if ($position -lt $text.Length -and $text[$position] -eq '}') { throw 'VPM yank policy versions object must not contain a trailing comma.' }
                        continue
                    }
                    if ($position -lt $text.Length -and $text[$position] -eq '}') { $position++; break }
                    throw 'VPM yank policy versions object is not properly terminated.'
                }
            }
            default { throw "VPM yank policy contains unknown key '$key'." }
        }
        Move-PureBaseJsonWhitespace -Text $text -Position ([ref]$position)
        if ($position -lt $text.Length -and $text[$position] -eq ',') {
            $position++
            Move-PureBaseJsonWhitespace -Text $text -Position ([ref]$position)
            if ($position -lt $text.Length -and $text[$position] -eq '}') { throw 'VPM yank policy root object must not contain a trailing comma.' }
            continue
        }
        if ($position -lt $text.Length -and $text[$position] -eq '}') { $position++; break }
        throw 'VPM yank policy root object is not properly terminated.'
    }
    Move-PureBaseJsonWhitespace -Text $text -Position ([ref]$position)
    if ($position -ne $text.Length) { throw 'VPM yank policy contains trailing data.' }
    if ($schemaVersion -ne '1' -or $package -ne 'jp.penguin.purebase' -or -not $keys.Contains('versions')) { throw 'VPM yank policy must define schemaVersion 1, package jp.penguin.purebase, and versions.' }
    return [pscustomobject][ordered]@{ schemaVersion = 1; package = $package; versions = $versions }
}

function Invoke-PureBaseYankDispatch {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PolicyPath,
        [Parameter(Mandatory)][string]$PolicyCommitSha,
        [Parameter(Mandatory)][scriptblock]$ApiInvoker
    )

    if ($PolicyCommitSha -notmatch '^[0-9a-fA-F]{40}$') { throw 'VPM yank policy commit SHA must be a full Git SHA.' }
    return Read-PureBaseVpmYankPolicy -Path $PolicyPath
}

function Resolve-PureBaseDailySource {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$EventName,
        [Parameter(Mandatory)][string]$Repository,
        [Parameter()][AllowEmptyString()][string]$PushSha = '',
        [Parameter()][AllowEmptyString()][string]$PullRequestHeadRepository = '',
        [Parameter()][AllowEmptyString()][string]$PullRequestHeadSha = '',
        [Parameter()][AllowEmptyString()][string]$PullRequestAuthor = '',
        [Parameter()][bool]$PullRequestDraft = $false
    )

    if ($EventName -eq 'push') {
        if ([string]::IsNullOrWhiteSpace($PushSha)) {
            throw 'Push events require a commit SHA.'
        }
        return [pscustomobject][ordered]@{ Allowed = $true; CheckoutRef = $PushSha; Reason = 'push' }
    }
    if ($EventName -ne 'pull_request') {
        throw "Unsupported Daily event '$EventName'."
    }
    if (-not [string]::Equals($PullRequestHeadRepository, $Repository, [StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject][ordered]@{ Allowed = $false; CheckoutRef = ''; Reason = 'external pull request' }
    }
    if ($PullRequestDraft) {
        return [pscustomobject][ordered]@{ Allowed = $false; CheckoutRef = ''; Reason = 'draft pull request' }
    }
    if ([string]::Equals($PullRequestAuthor, 'dependabot[bot]', [StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject][ordered]@{ Allowed = $false; CheckoutRef = ''; Reason = 'dependabot pull request' }
    }
    if ([string]::IsNullOrWhiteSpace($PullRequestHeadSha)) {
        throw 'Trusted pull requests require a head commit SHA.'
    }
    return [pscustomobject][ordered]@{ Allowed = $true; CheckoutRef = $PullRequestHeadSha; Reason = 'same-repository pull request' }
}

function Assert-PureBaseImmutableReleasesEnabled {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ApiRoot,
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$Token,
        [Parameter(Mandatory)][scriptblock]$ApiInvoker
    )

    $uri = "$($ApiRoot.TrimEnd('/'))/repos/$Repository/immutable-releases"
    try {
        $response = & $ApiInvoker 'GET' $uri $Token
    }
    catch {
        if ($_.Exception.Data['StatusCode'] -eq 404) {
            throw "Immutable Releases must be enabled for '$Repository' before release validation can begin."
        }
        throw
    }

    if ($null -eq $response -or $null -eq $response.PSObject.Properties['enabled'] -or -not [bool]$response.enabled) {
        throw "GitHub did not confirm that Immutable Releases are enabled for '$Repository'."
    }
    return $response
}

function Resolve-PureBasePublishedArtifact {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Release,
        [Parameter(Mandatory)][string]$AssetName,
        [Parameter()][AllowEmptyString()][string]$ExpectedSha256 = ''
    )

    if ([bool]$Release.draft) {
        throw 'A draft release cannot be reused as a published immutable release.'
    }
    if ($null -eq $Release.PSObject.Properties['immutable'] -or -not [bool]$Release.immutable) {
        throw 'GitHub did not report the published release as immutable.'
    }

    $assets = @($Release.assets | Where-Object name -eq $AssetName)
    if ($assets.Count -ne 1) {
        throw "Published release must contain exactly one asset named '$AssetName'."
    }
    $asset = $assets[0]
    if ([string]$asset.state -ne 'uploaded') {
        throw "Published release asset '$AssetName' is not in the uploaded state."
    }

    $digest = [string]$asset.digest
    if ($digest -notmatch '^sha256:([0-9a-fA-F]{64})$') {
        throw "Published release asset '$AssetName' has no valid SHA-256 digest."
    }
    $sha256 = $Matches[1].ToLowerInvariant()
    if (-not [string]::IsNullOrEmpty($ExpectedSha256) -and
        -not [string]::Equals($sha256, $ExpectedSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Published release asset '$AssetName' does not match the expected SHA-256."
    }

    $downloadUrl = [string]$asset.browser_download_url
    if ([string]::IsNullOrWhiteSpace($downloadUrl)) {
        throw "Published release asset '$AssetName' has no browser download URL."
    }

    return [pscustomobject][ordered]@{
        Name        = $AssetName
        Path        = ''
        Sha256      = $sha256
        DownloadUrl = $downloadUrl
        Source      = 'published-release'
    }
}

Export-ModuleMember -Function @(
    'ConvertTo-PureBaseSemVer',
    'Compare-PureBaseSemVer',
    'Resolve-PureBaseReleaseMode',
    'Resolve-PureBaseResumeTagAction',
    'Invoke-PureBaseGit',
    'New-PureBasePackageUrl',
    'New-PureBaseDispatchPayload',
    'Resolve-PureBaseDailySource',
    'Assert-PureBaseImmutableReleasesEnabled',
    'Resolve-PureBasePublishedArtifact',
    'Read-PureBaseVpmYankPolicy',
    'Invoke-PureBaseYankDispatch'
)
