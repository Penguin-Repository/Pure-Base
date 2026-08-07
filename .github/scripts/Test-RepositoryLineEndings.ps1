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

# This script validates tracked text files for LF-only line endings in both the Git index and working tree.

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RepositoryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$utf8 = [Text.UTF8Encoding]::new($false, $true)

function ConvertFrom-RepositoryUtf8 {
    param([Parameter(Mandatory)][byte[]]$Bytes)

    try {
        return $utf8.GetString($Bytes)
    }
    catch {
        throw [IO.InvalidDataException]::new('Git output contains invalid UTF-8.', $_.Exception)
    }
}

function Invoke-RepositoryGitBytes {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter()][byte[]]$StandardInputBytes
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.WorkingDirectory = $Root
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $null -ne $StandardInputBytes
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw 'Git could not be started.'
        }
        $stdout = [IO.MemoryStream]::new()
        $stdoutTask = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if ($null -ne $StandardInputBytes) {
            $process.StandardInput.BaseStream.Write($StandardInputBytes, 0, $StandardInputBytes.Length)
            $process.StandardInput.Close()
        }
        $process.WaitForExit()
        [void]($stdoutTask.GetAwaiter().GetResult())
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            $stderrSummary = $stderr.Trim()
            if ($stderrSummary.Length -gt 4096) {
                $stderrSummary = $stderrSummary.Substring(0, 4096) + '...'
            }
            $diagnostic = "Git command failed (args: $($Arguments -join ' '); exit code: $($process.ExitCode)"
            if (-not [string]::IsNullOrEmpty($stderrSummary)) {
                $diagnostic += "; stderr: $stderrSummary"
            }
            throw "$diagnostic)."
        }
        return ,$stdout.ToArray()
    }
    finally {
        $process.Dispose()
    }
}

function Split-RepositoryNulRecords {
    param([Parameter(Mandatory)][byte[]]$Bytes)

    $records = [Collections.Generic.List[byte[]]]::new()
    $recordStart = 0
    for ($index = 0; $index -lt $Bytes.Length; $index++) {
        if ($Bytes[$index] -eq 0) {
            $length = $index - $recordStart
            if ($length -eq 0) {
                throw 'Git output contains an empty NUL record.'
            }
            $record = [byte[]]::new($length)
            [Array]::Copy($Bytes, $recordStart, $record, 0, $length)
            $records.Add($record)
            $recordStart = $index + 1
        }
    }
    if ($recordStart -ne $Bytes.Length) {
        throw 'Git output is not NUL terminated.'
    }
    return $records
}

function ConvertTo-RepositoryDisplayPath {
    param([Parameter(Mandatory)][string]$Path)

    return $Path.Replace("`r", '\r').Replace("`n", '\n').Replace("`t", '\t')
}

function Add-RepositoryViolation {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][Collections.Generic.List[object]]$Violations,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Reason
    )

    $Violations.Add([pscustomobject]@{
            Path = $Path
            Reason = $Reason
        })
}

function Test-RepositoryCrByte {
    param([Parameter(Mandatory)][AllowEmptyCollection()][byte[]]$Bytes)

    return [Array]::IndexOf($Bytes, [byte]13) -ge 0
}

function Read-RepositoryIndexBlobs {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string[]]$ObjectIds
    )

    $blobs = [Collections.Generic.Dictionary[string, byte[]]]::new([StringComparer]::OrdinalIgnoreCase)
    if ($ObjectIds.Count -eq 0) {
        return ,$blobs
    }
    $batchInput = [Text.Encoding]::ASCII.GetBytes((($ObjectIds -join "`n") + "`n"))
    $output = Invoke-RepositoryGitBytes -Root $Root -Arguments @('cat-file', '--batch') -StandardInputBytes $batchInput
    $offset = 0
    foreach ($expectedObjectId in $ObjectIds) {
        $headerEnd = -1
        for ($index = $offset; $index -lt $output.Length; $index++) {
            if ($output[$index] -eq 10) {
                $headerEnd = $index
                break
            }
        }
        if ($headerEnd -lt 0 -or $headerEnd - $offset -gt 200) {
            throw 'Git blob batch output has an invalid header.'
        }
        $headerBytes = [byte[]]::new($headerEnd - $offset)
        [Array]::Copy($output, $offset, $headerBytes, 0, $headerBytes.Length)
        $header = [Text.Encoding]::ASCII.GetString($headerBytes)
        $match = [regex]::Match($header, '^(?<object>[0-9a-fA-F]{40,64}) blob (?<length>[0-9]+)$')
        if (-not $match.Success -or -not [string]::Equals($match.Groups['object'].Value, $expectedObjectId, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Git blob batch output does not match the index object.'
        }
        $length = 0L
        if (-not [Int64]::TryParse($match.Groups['length'].Value, [ref]$length) -or $length -lt 0 -or $length -gt [Int32]::MaxValue) {
            throw 'Git blob batch output has an invalid length.'
        }
        $bodyStart = $headerEnd + 1
        if ($output.Length - $bodyStart -lt $length + 1 -or $output[$bodyStart + $length] -ne 10) {
            throw 'Git blob batch output has a truncated body.'
        }
        $body = [byte[]]::new([int]$length)
        [Array]::Copy($output, $bodyStart, $body, 0, [int]$length)
        $blobs.Add($expectedObjectId, $body)
        $offset = $bodyStart + $length + 1
    }
    if ($offset -ne $output.Length) {
        throw 'Git blob batch output has trailing data.'
    }
    return $blobs
}

function Get-RepositoryAttributes {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][Collections.Generic.Dictionary[string, object]]$StageByPath,
        [Parameter(Mandatory)][byte[]]$PathInput,
        [Parameter(Mandatory)][bool]$UseCachedAttributes
    )

    $arguments = @('check-attr')
    if ($UseCachedAttributes) {
        $arguments += '--cached'
    }
    $arguments += @('-z', 'binary', 'text', '--stdin')
    $attributeRecords = Split-RepositoryNulRecords (Invoke-RepositoryGitBytes -Root $Root -Arguments $arguments -StandardInputBytes $PathInput)
    if (($attributeRecords.Count % 3) -ne 0) {
        throw 'Git attribute output is malformed.'
    }

    $attributesByPath = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    for ($index = 0; $index -lt $attributeRecords.Count; $index += 3) {
        $path = ConvertFrom-RepositoryUtf8 $attributeRecords[$index]
        $attribute = ConvertFrom-RepositoryUtf8 $attributeRecords[$index + 1]
        $value = ConvertFrom-RepositoryUtf8 $attributeRecords[$index + 2]
        if (-not $StageByPath.ContainsKey($path) -or ($attribute -ne 'binary' -and $attribute -ne 'text')) {
            throw 'Git attribute output does not match tracked paths.'
        }
        if (-not $attributesByPath.ContainsKey($path)) {
            $attributesByPath.Add($path, [ordered]@{})
        }
        if ($attributesByPath[$path].Contains($attribute)) {
            throw 'Git attribute output contains duplicate attributes.'
        }
        $attributesByPath[$path][$attribute] = $value
    }
    if ($attributesByPath.Count -ne $StageByPath.Count -or @($attributesByPath.Values | Where-Object { $_.Count -ne 2 }).Count -ne 0) {
        throw 'Git attribute output is incomplete.'
    }
    return $attributesByPath
}

try {
    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw 'Repository root is not a directory.'
    }
    [void](Invoke-RepositoryGitBytes -Root $root -Arguments @('rev-parse', '--is-inside-work-tree'))
    $gitRoot = [IO.Path]::GetFullPath((ConvertFrom-RepositoryUtf8 (Invoke-RepositoryGitBytes -Root $root -Arguments @('rev-parse', '--show-toplevel'))).TrimEnd([char[]]@("`r", "`n")))
    $pathComparison = if ([IO.Path]::DirectorySeparatorChar -eq [char]'\') { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not [string]::Equals($root.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar), $gitRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar), $pathComparison)) {
        throw 'Repository root is not the Git top-level directory.'
    }

    $stageByPath = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($recordBytes in Split-RepositoryNulRecords (Invoke-RepositoryGitBytes -Root $root -Arguments @('ls-files', '--stage', '-z'))) {
        $record = ConvertFrom-RepositoryUtf8 $recordBytes
        $tab = $record.IndexOf("`t")
        if ($tab -lt 0) {
            throw 'Git stage output is malformed.'
        }
        $header = $record.Substring(0, $tab).Split(' ')
        if ($header.Count -ne 3 -or $header[0] -notmatch '^100[0-7]{3}$' -or $header[2] -ne '0' -or $header[1] -notmatch '^[0-9a-fA-F]{40,64}$') {
            throw 'Git stage output contains an unsupported entry.'
        }
        $path = $record.Substring($tab + 1)
        if ([string]::IsNullOrEmpty($path) -or -not $stageByPath.TryAdd($path, [pscustomobject]@{ ObjectId = $header[1] })) {
            throw 'Git stage output contains a duplicate or empty path.'
        }
    }

    $eolByPath = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($recordBytes in Split-RepositoryNulRecords (Invoke-RepositoryGitBytes -Root $root -Arguments @('ls-files', '--eol', '-z'))) {
        $record = ConvertFrom-RepositoryUtf8 $recordBytes
        $tab = $record.IndexOf("`t")
        if ($tab -lt 0) {
            throw 'Git EOL output is malformed.'
        }
        $path = $record.Substring($tab + 1)
        if (-not $stageByPath.ContainsKey($path) -or -not $eolByPath.TryAdd($path, $record.Substring(0, $tab))) {
            throw 'Git EOL output does not match tracked paths.'
        }
    }
    if ($eolByPath.Count -ne $stageByPath.Count) {
        throw 'Git EOL output is incomplete.'
    }

    $pathInput = [IO.MemoryStream]::new()
    foreach ($path in $stageByPath.Keys) {
        $pathBytes = $utf8.GetBytes($path)
        $pathInput.Write($pathBytes, 0, $pathBytes.Length)
        $pathInput.WriteByte(0)
    }
    $pathInputBytes = $pathInput.ToArray()
    $indexAttributesByPath = Get-RepositoryAttributes -Root $root -StageByPath $stageByPath -PathInput $pathInputBytes -UseCachedAttributes $true
    $workingTreeAttributesByPath = Get-RepositoryAttributes -Root $root -StageByPath $stageByPath -PathInput $pathInputBytes -UseCachedAttributes $false

    $indexEligiblePaths = @($stageByPath.Keys | Where-Object { $indexAttributesByPath[$_]['binary'] -ne 'set' -and $indexAttributesByPath[$_]['text'] -ne 'unset' })
    $workingTreeEligiblePaths = @($stageByPath.Keys | Where-Object { $workingTreeAttributesByPath[$_]['binary'] -ne 'set' -and $workingTreeAttributesByPath[$_]['text'] -ne 'unset' })
    $objectIds = @($indexEligiblePaths | ForEach-Object { $stageByPath[$_].ObjectId } | Sort-Object -Unique)
    $indexBlobs = Read-RepositoryIndexBlobs -Root $root -ObjectIds $objectIds
    $violations = [Collections.Generic.List[object]]::new()
    foreach ($path in $indexEligiblePaths | Sort-Object) {
        $eol = $eolByPath[$path]
        if ($eol -match '(^| )i/(crlf|mixed)( |$)') {
            Add-RepositoryViolation -Violations $violations -Path $path -Reason 'index EOL status reports CRLF or mixed endings'
        }
        if (Test-RepositoryCrByte $indexBlobs[$stageByPath[$path].ObjectId]) {
            Add-RepositoryViolation -Violations $violations -Path $path -Reason 'index bytes contain CR (0x0D)'
        }
    }
    foreach ($path in $workingTreeEligiblePaths | Sort-Object) {
        $eol = $eolByPath[$path]
        if ($eol -match '(^| )w/(crlf|mixed)( |$)') {
            Add-RepositoryViolation -Violations $violations -Path $path -Reason 'working-tree EOL status reports CRLF or mixed endings'
        }
        $workingPath = [IO.Path]::GetFullPath((Join-Path $root $path))
        if (-not $workingPath.StartsWith($root.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, $pathComparison) -or -not (Test-Path -LiteralPath $workingPath -PathType Leaf)) {
            Add-RepositoryViolation -Violations $violations -Path $path -Reason 'working-tree file is missing or outside the repository root'
            continue
        }
        if (Test-RepositoryCrByte ([IO.File]::ReadAllBytes($workingPath))) {
            Add-RepositoryViolation -Violations $violations -Path $path -Reason 'working-tree bytes contain CR (0x0D)'
        }
    }
    foreach ($violation in $violations) {
        Write-Output "$(ConvertTo-RepositoryDisplayPath $violation.Path): $($violation.Reason)"
    }
    if ($violations.Count -gt 0) {
        exit 1
    }
}
catch {
    Write-Output "repository: $($_.Exception.Message)"
    exit 1
}