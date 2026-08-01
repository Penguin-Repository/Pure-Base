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
        [Parameter(Mandatory)][string]$PackageVersion,
        [Parameter(Mandatory)][string]$ConfirmedVersion,
        [Parameter(Mandatory)][string]$HeadSha,
        [Parameter()][switch]$Resume,
        [Parameter()][AllowNull()]$ExistingTag = $null,
        [Parameter()][AllowNull()]$ExistingRelease = $null
    )

    $package = ConvertTo-PureBaseSemVer -Value $PackageVersion
    [void](ConvertTo-PureBaseSemVer -Value $ConfirmedVersion)
    if ([string]::IsNullOrWhiteSpace($HeadSha) -or $HeadSha -notmatch '^[0-9a-fA-F]{40}$' -or
        -not [string]::Equals($package.original, $ConfirmedVersion, [StringComparison]::Ordinal)) {
        throw 'The strict release state requires package.json version, confirmation, and an exact commit SHA to agree.'
    }

    $releaseState = if ($null -eq $ExistingRelease) {
        'none'
    }
    elseif ([bool]$ExistingRelease.draft) {
        'draft'
    }
    else {
        'published'
    }
    if (-not $Resume) {
        if ($null -ne $ExistingTag -or $null -ne $ExistingRelease) {
            throw 'The strict release state for a fresh release requires no existing tag or release.'
        }
        return [pscustomobject][ordered]@{ Mode = 'fresh'; Version = $ConfirmedVersion; PrereleaseKind = $package.prereleaseKind; TagState = 'missing'; ReleaseState = 'none' }
    }

    if ($null -eq $ExistingTag -or $null -eq $ExistingRelease) {
        throw 'The strict release state for resume requires both an annotated tag and a release.'
    }
    $tagName = [string]$ExistingTag.Name
    $tagCommit = [string]$ExistingTag.PeeledCommitSha
    if (-not [bool]$ExistingTag.Annotated -or
        -not [string]::Equals($tagName, $ConfirmedVersion, [StringComparison]::Ordinal) -or
        -not [string]::Equals($tagCommit, $HeadSha, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The strict release state requires an annotated tag for the confirmed version at the exact commit.'
    }
    $releaseTag = [string]$ExistingRelease.tag_name
    $releaseCommit = [string]$ExistingRelease.target_commitish
    $prereleaseProperty = $ExistingRelease.PSObject.Properties['prerelease']
    if (-not [string]::Equals($releaseTag, $ConfirmedVersion, [StringComparison]::Ordinal) -or
        -not [string]::Equals($releaseCommit, $HeadSha, [StringComparison]::OrdinalIgnoreCase) -or
        $null -eq $prereleaseProperty -or $prereleaseProperty.Value -isnot [bool] -or
        [bool]$prereleaseProperty.Value -ne [bool]$package.isPrerelease) {
        throw 'The strict release state release identity does not match the confirmed version and exact commit.'
    }
    if ($releaseState -eq 'published' -and ($null -eq $ExistingRelease.PSObject.Properties['immutable'] -or -not [bool]$ExistingRelease.immutable)) {
        throw 'The strict release state requires a published resume release to be immutable.'
    }
    return [pscustomobject][ordered]@{ Mode = 'resume'; Version = $ConfirmedVersion; PrereleaseKind = $package.prereleaseKind; TagState = 'annotated'; ReleaseState = $releaseState }
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
        [Parameter()][AllowEmptyString()][string]$AuthenticationToken = '',
        [Parameter()][AllowEmptyString()][string]$GitServerUrl = '',
        [Parameter()][switch]$AllowFailure
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.WorkingDirectory = [IO.Path]::GetFullPath($PackageRoot)
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    if (-not [string]::IsNullOrEmpty($AuthenticationToken)) {
        $serverUrl = if ($GitServerUrl) { $GitServerUrl } elseif ($env:GITHUB_SERVER_URL) { [string]$env:GITHUB_SERVER_URL } else { 'https://github.com' }
        $serverUri = $null
        if (-not [Uri]::TryCreate($serverUrl, [UriKind]::Absolute, [ref]$serverUri) -or $serverUri.Scheme -cne 'https') {
            throw 'Git authentication requires an absolute HTTPS GitHub server URL.'
        }
        $existingConfigCount = [string]$startInfo.Environment['GIT_CONFIG_COUNT']
        if ($existingConfigCount -and $existingConfigCount -notmatch '^(?:0|[1-9][0-9]*)$') {
            throw 'GIT_CONFIG_COUNT must be a non-negative integer.'
        }
        $configIndex = if ($existingConfigCount) { [int]$existingConfigCount } else { 0 }
        $serverScope = $serverUri.GetLeftPart([UriPartial]::Authority).TrimEnd('/') + '/'
        $credential = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("x-access-token:$AuthenticationToken"))
        $startInfo.Environment['GIT_CONFIG_COUNT'] = [string]($configIndex + 1)
        $startInfo.Environment["GIT_CONFIG_KEY_$configIndex"] = "http.$serverScope.extraheader"
        $startInfo.Environment["GIT_CONFIG_VALUE_$configIndex"] = "AUTHORIZATION: basic $credential"
    }
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

function Select-PureBaseReleaseValidationRun {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object[]]$Runs, [Parameter(Mandatory)][string]$HeadSha, [Parameter(Mandatory)][string]$Branch, [Parameter(Mandatory)][string]$WorkflowPath)

    $expectedWorkflowPath = ConvertTo-PureBaseReleaseValidationWorkflowPath -Value $WorkflowPath
    $candidates = @($Runs | Where-Object {
            if ($null -eq $_) { return $false }
            try { $runWorkflowPath = ConvertTo-PureBaseReleaseValidationWorkflowPath -Value ([string]$_.path) }
            catch { return $false }
            return $runWorkflowPath -ceq $expectedWorkflowPath -and
                [string]::Equals([string]$_.head_sha, $HeadSha, [StringComparison]::OrdinalIgnoreCase) -and
                [string]$_.head_branch -ceq $Branch -and [string]$_.event -ceq 'workflow_dispatch' -and
                [int]$_.run_number -gt 0 -and [int]$_.run_attempt -gt 0
        })
    if ($candidates.Count -eq 0) { throw 'No matching validation run was found for the exact workflow, branch, and commit.' }
    $latest = @($candidates | Sort-Object @{ Expression = { [int]$_.run_number }; Descending = $true }, @{ Expression = { [int]$_.run_attempt }; Descending = $true })[0]
    if ([string]$latest.status -cne 'completed' -or [string]$latest.conclusion -cne 'success') { throw 'The latest matching validation run is not completed successfully.' }
    return $latest
}

function Resolve-PureBaseValidationArtifact {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowNull()][AllowEmptyCollection()][object[]]$Artifacts, [Parameter(Mandatory)][string]$ExpectedName, [Parameter(Mandatory)][long]$WorkflowRunId, [Parameter(Mandatory)][int]$WorkflowRunAttempt)

    $expectedSuffix = "-$WorkflowRunId-$WorkflowRunAttempt"
    if (-not $ExpectedName.EndsWith($expectedSuffix, [StringComparison]::Ordinal)) {
        throw 'The expected validation artifact name does not bind the selected workflow run and attempt.'
    }
    $matches = @($Artifacts | Where-Object {
            $null -ne $_ -and [string]$_.name -ceq $ExpectedName -and -not [bool]$_.expired -and $null -ne $_.workflow_run -and
            [long]$_.id -gt 0 -and [long]$_.workflow_run.id -eq $WorkflowRunId
        })
    if ($matches.Count -ne 1) { throw 'Expected exactly one unexpired validation artifact for the selected workflow run.' }
    return $matches[0]
}

function Assert-PureBaseValidationManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Manifest, [Parameter(Mandatory)][string]$Repository, [Parameter(Mandatory)][string]$HeadSha,
        [Parameter(Mandatory)][string]$HeadBranch, [Parameter(Mandatory)][long]$WorkflowRunId, [Parameter(Mandatory)][int]$WorkflowRunAttempt,
        [Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][string]$AssetName, [Parameter(Mandatory)][string]$Sha256
    )

    if ($null -eq $Manifest) { throw 'Validation manifest is missing.' }
    $expected = [ordered]@{ schemaVersion = 1; repository = $Repository; headSha = $HeadSha; headBranch = $HeadBranch; workflowRunId = $WorkflowRunId; workflowRunAttempt = $WorkflowRunAttempt; version = $Version; assetName = $AssetName; sha256 = $Sha256 }
    $fieldLabels = @{ headSha = 'head SHA'; headBranch = 'head branch'; workflowRunId = 'workflow run ID'; workflowRunAttempt = 'workflow run attempt'; sha256 = 'SHA-256' }
    foreach ($entry in $expected.GetEnumerator()) {
        if ($Manifest -is [Collections.IDictionary]) {
            if (-not $Manifest.Contains($entry.Key)) { throw "Validation manifest $($entry.Key) is missing." }
            $actual = $Manifest[$entry.Key]
        }
        else {
            $property = $Manifest.PSObject.Properties[$entry.Key]
            if ($null -eq $property) { throw "Validation manifest $($entry.Key) is missing." }
            $actual = $property.Value
        }
        if ($null -eq $actual) { throw "Validation manifest $($entry.Key) is missing." }
        $equal = if ($entry.Key -in @('headSha', 'sha256')) { [string]::Equals([string]$actual, [string]$entry.Value, [StringComparison]::OrdinalIgnoreCase) } else { [string]$actual -ceq [string]$entry.Value }
        $label = if ($fieldLabels.ContainsKey($entry.Key)) { $fieldLabels[$entry.Key] } else { $entry.Key }
        if (-not $equal) { throw "Validation manifest $label does not match the expected value." }
    }
    $manifestSha256 = if ($Manifest -is [Collections.IDictionary]) { [string]$Manifest['sha256'] } else { [string]$Manifest.sha256 }
    if ($manifestSha256 -notmatch '^[0-9a-f]{64}$') { throw 'Validation manifest SHA-256 must be lowercase hexadecimal.' }
    return $Manifest
}

function Resolve-PureBaseValidationPayloadFiles {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string[]]$Files, [Parameter(Mandatory)][string]$AssetName)

    $expected = @($AssetName, "$AssetName.sha256", 'release-validation.json')
    foreach ($name in $expected) { if (@($Files | Where-Object { $_ -ceq $name }).Count -ne 1) { throw "The validation payload must contain exactly one '$name'." } }
    if ($Files.Count -ne $expected.Count) { throw 'The validation payload contains unexpected files.' }
    return [pscustomobject][ordered]@{ Zip = $AssetName; Sidecar = "$AssetName.sha256"; Manifest = 'release-validation.json' }
}

function Assert-PureBaseValidationPayloadLayout {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ValidatedPackageDirectory, [Parameter(Mandatory)][string]$AssetName)

    $rootPath = [IO.Path]::GetFullPath($ValidatedPackageDirectory)
    $entries = @(Get-ChildItem -LiteralPath $rootPath -Force -Recurse)
    $rootFiles = @($entries | Where-Object {
            -not $_.PSIsContainer -and [string]::Equals($_.DirectoryName, $rootPath, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -ExpandProperty Name)
    [void](Resolve-PureBaseValidationPayloadFiles -Files $rootFiles -AssetName $AssetName)

    $directories = @($entries | Where-Object PSIsContainer | ForEach-Object { $_.FullName.Substring($rootPath.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) })
    $nestedFiles = @($entries | Where-Object {
            -not $_.PSIsContainer -and -not [string]::Equals($_.DirectoryName, $rootPath, [StringComparison]::OrdinalIgnoreCase)
        } | ForEach-Object { $_.FullName.Substring($rootPath.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) })
    if ($directories.Count -gt 0 -or $nestedFiles.Count -gt 0) {
        $details = @()
        if ($directories.Count -gt 0) { $details += "directories: $($directories -join ', ')" }
        if ($nestedFiles.Count -gt 0) { $details += "nested files: $($nestedFiles -join ', ')" }
        throw "The validation payload layout must not contain $($details -join '; ')."
    }
}

function New-PureBaseReleaseBody {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Repository, [Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][string]$AssetName, [Parameter()][AllowEmptyString()][string]$GeneratedNotesBody = '')

    if ($Repository -notmatch '^(?<owner>[^/]+)/(?<name>[^/]+)$') { throw 'Repository must use owner/name form.' }
    $owner = $Matches['owner']
    $repositoryName = $Matches['name']
    [void](ConvertTo-PureBaseSemVer -Value $Version)
    if ([string]::IsNullOrWhiteSpace($AssetName) -or $AssetName -match '[/\\]') { throw 'AssetName must be one file name without path separators.' }
    $badge = '[![Downloads](https://img.shields.io/github/downloads/{0}/{1}/{2}/{3}?label=downloads)]' -f [Uri]::EscapeDataString($owner), [Uri]::EscapeDataString($repositoryName), [Uri]::EscapeDataString($Version), [Uri]::EscapeDataString($AssetName)
    if ([string]::IsNullOrEmpty($GeneratedNotesBody)) { return $badge }
    return "$badge`n$GeneratedNotesBody"
}

function Resolve-PureBaseDraftAssetAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowNull()][AllowEmptyCollection()][object[]]$Assets, [Parameter(Mandatory)][string]$AssetName, [Parameter(Mandatory)][string]$Sha256)

    if ($Sha256 -notmatch '^[0-9a-f]{64}$') { throw 'Expected release asset SHA-256 must be lowercase hexadecimal.' }
    $matches = [Collections.Generic.List[object]]::new()
    foreach ($asset in $Assets) {
        if ($null -ne $asset -and [string]::Equals([string]$asset.name, $AssetName, [StringComparison]::Ordinal)) {
            $matches.Add($asset)
        }
    }
    if ($matches.Count -eq 0) { return 'upload' }
    if ($matches.Count -ne 1) { throw "The release asset '$AssetName' is duplicated." }
    $assetState = if ($null -eq $matches[0].PSObject.Properties['state']) { '' } else { [string]$matches[0].state }
    if ($assetState -cne 'uploaded') { throw "The release asset '$AssetName' is not in the uploaded state." }
    if ([string]$matches[0].digest -cne "sha256:$Sha256") { throw "The release asset '$AssetName' does not match the validated SHA-256." }
    return 'reuse'
}

function Assert-PureBasePublishedResumeArtifact {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Release, [Parameter(Mandatory)][string]$AssetName, [Parameter(Mandatory)][string]$ValidationArtifactSha256)

    $matches = @($Release.assets | Where-Object { $null -ne $_ -and [string]$_.name -ceq $AssetName })
    if ($matches.Count -ne 1 -or [string]$matches[0].digest -cne "sha256:$ValidationArtifactSha256") { throw 'The published release asset does not match the validation artifact digest.' }
    $assetState = if ($null -eq $matches[0].PSObject.Properties['state']) { '' } else { [string]$matches[0].state }
    if ($assetState -cne 'uploaded') { throw "The published release asset '$AssetName' is not in the uploaded state." }
    return $matches[0]
}

function Assert-PureBaseArtifactRedirectLocation {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Location)

    $uri = $null
    if (-not [Uri]::TryCreate($Location, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -cne 'https' -or -not [string]::IsNullOrEmpty($uri.UserInfo)) { throw 'Artifact archive redirects must use an absolute HTTPS redirect without userinfo.' }
    return $uri.AbsoluteUri
}

function ConvertTo-PureBaseReleaseValidationWorkflowPath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Value)

    $normalized = $Value.Trim().Replace('\', '/')
    while ($normalized.StartsWith('./', [StringComparison]::Ordinal)) { $normalized = $normalized.Substring(2) }
    if ($normalized -notmatch '^\.github/workflows/') { $normalized = ".github/workflows/$normalized" }
    if ($normalized -cne '.github/workflows/release-validation.yml') {
        throw "Validation workflow path must be '.github/workflows/release-validation.yml', not '$Value'."
    }
    return $normalized
}

function Get-PureBaseArtifactHttpResponse {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Response)

    $statusCodeProperty = $Response.PSObject.Properties['StatusCode']
    if ($null -eq $statusCodeProperty) { throw 'Validation artifact archive response has no HTTP status code.' }
    $statusCode = [int]$statusCodeProperty.Value
    $location = ''
    $headersProperty = $Response.PSObject.Properties['Headers']
    if ($null -ne $headersProperty -and $null -ne $headersProperty.Value) {
        $headers = $headersProperty.Value
        if ($headers -is [Collections.IDictionary]) {
            $location = [string]$headers['Location']
        }
        else {
            $locationProperty = $headers.PSObject.Properties['Location']
            if ($null -ne $locationProperty -and $null -ne $locationProperty.Value) {
                $location = [string]$locationProperty.Value
            }
            elseif ($null -ne $headers.PSObject.Methods['TryGetValues']) {
                $values = $null
                if ($headers.TryGetValues('Location', [ref]$values)) { $location = [string](@($values) | Select-Object -First 1) }
            }
        }
    }
    return [pscustomobject][ordered]@{ StatusCode = $statusCode; Location = $location }
}

function Get-PureBaseArtifactHttpResponseFromException {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Exception]$Exception)

    $current = $Exception
    while ($null -ne $current) {
        $responseProperty = $current.PSObject.Properties['Response']
        if ($null -ne $responseProperty -and $null -ne $responseProperty.Value) { return $responseProperty.Value }
        $current = $current.InnerException
    }
    return $null
}

function Invoke-PureBaseArtifactRequestWithoutRedirect {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][scriptblock]$RequestInvoker, [Parameter(Mandatory)][string]$Uri,
        [Parameter(Mandatory)][hashtable]$Headers, [Parameter()][AllowEmptyString()][string]$OutFile = ''
    )

    try {
        $response = & $RequestInvoker 'GET' $Uri $Headers $OutFile 0
        return Get-PureBaseArtifactHttpResponse -Response $response
    }
    catch {
        $response = Get-PureBaseArtifactHttpResponseFromException -Exception $_.Exception
        if ($null -eq $response) { throw }
        return Get-PureBaseArtifactHttpResponse -Response $response
    }
}

function Invoke-PureBaseArtifactArchiveDownload {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ArchiveUri, [Parameter(Mandatory)][string]$Token, [Parameter(Mandatory)][string]$DestinationPath,
        [Parameter()][scriptblock]$RequestInvoker = {
            param($Method, $Uri, $Headers, $OutFile, $MaximumRedirection)
            if ($OutFile) { Invoke-WebRequest -Method $Method -Uri $Uri -Headers $Headers -OutFile $OutFile -MaximumRedirection $MaximumRedirection -ErrorAction Stop }
            else { Invoke-WebRequest -Method $Method -Uri $Uri -Headers $Headers -MaximumRedirection $MaximumRedirection -ErrorAction Stop }
        },
        [ValidateRange(0, 3)][int]$MaximumRedirects = 3
    )

    $currentUri = $ArchiveUri
    for ($requestIndex = 0; $requestIndex -le $MaximumRedirects; $requestIndex++) {
        $headers = if ($requestIndex -eq 0) { @{ Authorization = "Bearer $Token"; Accept = 'application/vnd.github+json'; 'User-Agent' = 'Pure-Base-Actions' } } else { @{ 'User-Agent' = 'Pure-Base-Actions' } }
        $response = Invoke-PureBaseArtifactRequestWithoutRedirect -RequestInvoker $RequestInvoker -Uri $currentUri -Headers $headers -OutFile $(if ($requestIndex -eq 0) { '' } else { $DestinationPath })
        $statusCode = $response.StatusCode
        if ($statusCode -eq 410) { throw 'Validation artifact archive download returned HTTP 410 (expired).' }
        if ($requestIndex -eq 0 -and $statusCode -ne 302) { throw "Validation artifact archive API endpoint must return HTTP 302, not HTTP $statusCode." }
        if ($statusCode -eq 200) { if ($requestIndex -eq 0) { throw 'Validation artifact archive API endpoint returned success instead of a redirect.' }; return $DestinationPath }
        if ($statusCode -notin @(301, 302, 303, 307, 308)) { throw "Validation artifact archive download failed with HTTP $statusCode." }
        if ($requestIndex -eq $MaximumRedirects) { throw 'Validation artifact archive redirect limit was exceeded.' }
        $currentUri = Assert-PureBaseArtifactRedirectLocation -Location $response.Location
    }
    throw 'Validation artifact archive redirect limit was exceeded.'
}

function Assert-PureBaseValidatedArchive {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ValidatedPackageDirectory, [Parameter(Mandatory)][string]$AssetName, [Parameter(Mandatory)][string]$ExpectedSha256, [Parameter(Mandatory)][string]$Version, [Parameter()][string]$PackageName = 'jp.penguin.purebase')

    try {
        Assert-PureBaseValidationPayloadLayout -ValidatedPackageDirectory $ValidatedPackageDirectory -AssetName $AssetName
        $zipPath = Join-Path $ValidatedPackageDirectory $AssetName
        $sidecar = [IO.File]::ReadAllText($zipPath + '.sha256', [Text.UTF8Encoding]::new($false, $true)).TrimEnd("`r", "`n")
        if ($sidecar -notmatch '^[0-9a-f]{64}$' -or $sidecar -cne $ExpectedSha256) { throw 'sidecar does not match expected SHA-256' }
        $actualHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -cne $ExpectedSha256) { throw 'ZIP does not match expected SHA-256' }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
        try {
            $entries = @($archive.Entries | Where-Object { $_.FullName -ceq 'package.json' })
            if ($entries.Count -ne 1) { throw 'ZIP must contain exactly one root package.json' }
            $reader = [IO.StreamReader]::new($entries[0].Open(), [Text.UTF8Encoding]::new($false, $true))
            try { $package = $reader.ReadToEnd() | ConvertFrom-Json }
            finally { $reader.Dispose() }
        }
        finally { $archive.Dispose() }
        if ([string]$package.name -cne $PackageName -or [string]$package.version -cne $Version) { throw 'ZIP package identity does not match the validated package.' }
        return [pscustomobject][ordered]@{ Name = $AssetName; Path = $zipPath; Sha256 = $actualHash; Source = 'validated-artifact' }
    }
    catch { throw "Validated archive verification failed: $($_.Exception.Message)" }
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
    'Select-PureBaseReleaseValidationRun',
    'Resolve-PureBaseValidationArtifact',
    'Assert-PureBaseValidationManifest',
    'Resolve-PureBaseValidationPayloadFiles',
    'Assert-PureBaseValidationPayloadLayout',
    'New-PureBaseReleaseBody',
    'Resolve-PureBaseDraftAssetAction',
    'Assert-PureBasePublishedResumeArtifact',
    'Assert-PureBaseArtifactRedirectLocation',
    'Invoke-PureBaseArtifactArchiveDownload',
    'Assert-PureBaseValidatedArchive',
    'Read-PureBaseVpmYankPolicy',
    'Invoke-PureBaseYankDispatch'
)
