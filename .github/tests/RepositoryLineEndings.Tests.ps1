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

# This test file verifies the repository line-ending checker against isolated Git fixtures.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

BeforeAll {
    $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $checkerPath = Join-Path $repositoryRoot '.github/scripts/Test-RepositoryLineEndings.ps1'

    function New-LineEndingRepository {
        [CmdletBinding(SupportsShouldProcess)]
        param()

        $root = Join-Path $TestDrive ('line-endings-' + [guid]::NewGuid().ToString('N'))
        if (-not $PSCmdlet.ShouldProcess($root, 'Create line-ending Git fixture repository')) {
            return
        }
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        & git -C $root init --quiet
        if ($LASTEXITCODE -ne 0) { throw 'git init failed for test fixture.' }
        [IO.File]::WriteAllBytes((Join-Path $root '.gitattributes'), [Text.Encoding]::UTF8.GetBytes("* text eol=lf`n"))
        [IO.File]::WriteAllBytes((Join-Path $root 'text.txt'), [Text.Encoding]::UTF8.GetBytes("alpha`nbeta`n"))
        & git -C $root add --all
        if ($LASTEXITCODE -ne 0) { throw 'git add failed for test fixture.' }
        return $root
    }

    function Write-LineEndingFixtureBytes {
        param(
            [Parameter(Mandatory)][string]$Root,
            [Parameter(Mandatory)][string]$RelativePath,
            [Parameter(Mandatory)][byte[]]$Bytes
        )

        $path = Join-Path $Root $RelativePath
        $directory = Split-Path -Parent $path
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }
        [IO.File]::WriteAllBytes($path, $Bytes)
    }

    function Get-LineEndingFixtureBytes {
        param([Parameter(Mandatory)][string]$Kind)

        switch ($Kind) {
            'CRLF' { return [Text.Encoding]::UTF8.GetBytes("alpha`r`nbeta`r`n") }
            'CR' { return [Text.Encoding]::UTF8.GetBytes("alpha`rbeta`r") }
            'mixed' { return [Text.Encoding]::UTF8.GetBytes("alpha`nbeta`r`ngamma`n") }
            default { throw "Unsupported line-ending fixture kind '$Kind'." }
        }
    }

    function Set-LineEndingFixtureIndexBytes {
        [CmdletBinding(SupportsShouldProcess)]
        param(
            [Parameter(Mandatory)][string]$Root,
            [Parameter(Mandatory)][string]$RelativePath,
            [Parameter(Mandatory)][byte[]]$Bytes
        )

        $path = Join-Path $Root $RelativePath
        if (-not $PSCmdlet.ShouldProcess($path, 'Set Git index fixture bytes')) {
            return
        }
        Write-LineEndingFixtureBytes -Root $Root -RelativePath $RelativePath -Bytes $Bytes
        $objectId = (& git -C $Root hash-object -w --no-filters -- (Join-Path $Root $RelativePath)).Trim()
        if ($LASTEXITCODE -ne 0 -or $objectId -notmatch '^[0-9a-f]{40,64}$') {
            throw 'git hash-object failed for index fixture.'
        }
        & git -C $Root update-index --add --cacheinfo "100644,$objectId,$RelativePath"
        if ($LASTEXITCODE -ne 0) { throw 'git update-index failed for index fixture.' }
    }

    function Invoke-LineEndingChecker {
        param([Parameter(Mandatory)][string]$Root)

        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
        $startInfo.WorkingDirectory = $repositoryRoot
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        foreach ($argument in @('-NoProfile', '-NonInteractive', '-File', $checkerPath, '-RepositoryRoot', $Root)) {
            [void]$startInfo.ArgumentList.Add($argument)
        }

        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        try {
            [void]$process.Start()
            $stdoutTask = $process.StandardOutput.ReadToEndAsync()
            $stderrTask = $process.StandardError.ReadToEndAsync()
            $process.WaitForExit()
            $stdout = $stdoutTask.GetAwaiter().GetResult()
            $stderr = $stderrTask.GetAwaiter().GetResult()
            $output = [string[]]@(@($stdout, $stderr) | Where-Object { -not [string]::IsNullOrEmpty($_) })
            return [pscustomobject]@{
                ExitCode = $process.ExitCode
                Output = $output
            }
        }
        finally {
            $process.Dispose()
        }
    }
}

Describe 'Repository line-ending checker' {
    It 'accepts a compliant LF-only repository' {
        $root = New-LineEndingRepository
        $result = Invoke-LineEndingChecker -Root $root

        $result.ExitCode | Should -Be 0
        $result.Output | Should -BeNullOrEmpty
    }

    It 'rejects a Git repository subdirectory while accepting its top-level root' {
        $root = New-LineEndingRepository
        $subdirectory = Join-Path $root 'nested'
        New-Item -ItemType Directory -Path $subdirectory -Force | Out-Null

        $rootResult = Invoke-LineEndingChecker -Root $root
        $subdirectoryResult = Invoke-LineEndingChecker -Root $subdirectory

        $rootResult.ExitCode | Should -Be 0
        $subdirectoryResult.ExitCode | Should -Be 1
        ($subdirectoryResult.Output -join "`n") | Should -Match 'Git top-level directory'
    }

    It 'reports a successful checker process exit code independently of prior native failures' {
        $root = New-LineEndingRepository
        $hostPath = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
        & $hostPath -NoProfile -NonInteractive -Command 'exit 73'
        $LASTEXITCODE | Should -Be 73

        $result = Invoke-LineEndingChecker -Root $root

        $result.ExitCode | Should -Be 0
        $result.Output | Should -BeNullOrEmpty
    }

    It 'rejects <Scope>-only <Kind> endings' -ForEach @(
        @{ Scope = 'index'; Kind = 'CRLF' },
        @{ Scope = 'worktree'; Kind = 'CRLF' },
        @{ Scope = 'index'; Kind = 'CR' },
        @{ Scope = 'worktree'; Kind = 'CR' },
        @{ Scope = 'index'; Kind = 'mixed' },
        @{ Scope = 'worktree'; Kind = 'mixed' }
    ) {
        $root = New-LineEndingRepository
        $relativePath = 'text.txt'
        $invalidBytes = Get-LineEndingFixtureBytes -Kind $Kind
        if ($Scope -eq 'index') {
            Set-LineEndingFixtureIndexBytes -Root $root -RelativePath $relativePath -Bytes $invalidBytes
            Write-LineEndingFixtureBytes -Root $root -RelativePath $relativePath -Bytes ([Text.Encoding]::UTF8.GetBytes("alpha`nbeta`n"))
        }
        else {
            Write-LineEndingFixtureBytes -Root $root -RelativePath $relativePath -Bytes $invalidBytes
        }

        $result = Invoke-LineEndingChecker -Root $root
        $output = $result.Output -join "`n"

        $result.ExitCode | Should -Be 1
        $output | Should -Match ([regex]::Escape($relativePath))
        $diagnosticScope = if ($Scope -eq 'worktree') { 'working-tree' } else { $Scope }
        $output | Should -Match "$diagnosticScope bytes contain CR"
        if ($Kind -ne 'CR') {
            $output | Should -Match "$diagnosticScope EOL status reports CRLF or mixed endings"
        }
    }

    It 'rejects index CR bytes using cached text attributes when live attributes mark all files binary' {
        $root = New-LineEndingRepository
        $relativePath = 'text.txt'
        Set-LineEndingFixtureIndexBytes -Root $root -RelativePath $relativePath -Bytes (Get-LineEndingFixtureBytes -Kind 'CR')
        Write-LineEndingFixtureBytes -Root $root -RelativePath $relativePath -Bytes ([Text.Encoding]::UTF8.GetBytes("alpha`nbeta`n"))
        [IO.File]::WriteAllBytes((Join-Path $root '.gitattributes'), [Text.Encoding]::UTF8.GetBytes("* binary`n"))

        $result = Invoke-LineEndingChecker -Root $root
        $output = $result.Output -join "`n"

        $result.ExitCode | Should -Be 1
        $output | Should -Match ([regex]::Escape($relativePath))
        $output | Should -Match 'index bytes contain CR'
        $output | Should -Not -Match 'working-tree bytes contain CR'
    }

    It 'rejects working-tree CR bytes using live text attributes when cached attributes mark all files binary' {
        $root = New-LineEndingRepository
        $relativePath = 'text.txt'
        $sentinelPath = 'cached-text.txt'
        [IO.File]::WriteAllBytes((Join-Path $root '.gitattributes'), [Text.Encoding]::UTF8.GetBytes("* binary`n$sentinelPath -binary text`n"))
        Write-LineEndingFixtureBytes -Root $root -RelativePath $sentinelPath -Bytes ([Text.Encoding]::UTF8.GetBytes("valid`n"))
        & git -C $root add -- .gitattributes $sentinelPath
        if ($LASTEXITCODE -ne 0) { throw 'git add failed for cached binary attribute fixture.' }
        [IO.File]::WriteAllBytes((Join-Path $root '.gitattributes'), [Text.Encoding]::UTF8.GetBytes("* text eol=lf`n"))
        Write-LineEndingFixtureBytes -Root $root -RelativePath $relativePath -Bytes (Get-LineEndingFixtureBytes -Kind 'CR')

        $result = Invoke-LineEndingChecker -Root $root
        $output = $result.Output -join "`n"

        $result.ExitCode | Should -Be 1
        $output | Should -Match ([regex]::Escape($relativePath))
        $output | Should -Match 'working-tree bytes contain CR'
        $output | Should -Not -Match 'index bytes contain CR'
    }

    It 'skips explicitly binary files while enforcing an adjacent meta file' {
        $root = New-LineEndingRepository
        [IO.File]::AppendAllText((Join-Path $root '.gitattributes'), "*.exr binary`n*.png binary`nTests/Fixtures/Scenes/PureBaseValidation/LightingData.asset binary`n", [Text.Encoding]::UTF8)
        $binaryBytes = [byte[]](1, 13, 10, 2)
        foreach ($path in @(
                'Tests/Fixtures/Scenes/PureBaseValidation/color.exr',
                'Tests/Fixtures/Scenes/PureBaseValidation/color.png',
                'Tests/Fixtures/Scenes/PureBaseValidation/LightingData.asset'
            )) {
            Write-LineEndingFixtureBytes -Root $root -RelativePath $path -Bytes $binaryBytes
        }
        Write-LineEndingFixtureBytes -Root $root -RelativePath 'Tests/Fixtures/Scenes/PureBaseValidation/LightingData.asset.meta' -Bytes ([Text.Encoding]::UTF8.GetBytes("meta`r`n"))
        & git -C $root add --all
        if ($LASTEXITCODE -ne 0) { throw 'git add failed for binary fixture.' }

        $result = Invoke-LineEndingChecker -Root $root
        $output = $result.Output -join "`n"

        $result.ExitCode | Should -Be 1
        $output | Should -Match 'LightingData\.asset\.meta'
        $output | Should -Not -Match 'color\.exr|color\.png|LightingData\.asset:'
    }

    It 'accepts CR-containing explicit binary files when no text file violates the policy' {
        $root = New-LineEndingRepository
        [IO.File]::AppendAllText((Join-Path $root '.gitattributes'), "*.exr binary`n*.png binary`nTests/Fixtures/Scenes/PureBaseValidation/LightingData.asset binary`n", [Text.Encoding]::UTF8)
        foreach ($path in @(
                'Tests/Fixtures/Scenes/PureBaseValidation/color.exr',
                'Tests/Fixtures/Scenes/PureBaseValidation/color.png',
                'Tests/Fixtures/Scenes/PureBaseValidation/LightingData.asset'
            )) {
            Write-LineEndingFixtureBytes -Root $root -RelativePath $path -Bytes ([byte[]](1, 13, 10, 2))
        }
        & git -C $root add --all
        if ($LASTEXITCODE -ne 0) { throw 'git add failed for binary-only fixture.' }

        $result = Invoke-LineEndingChecker -Root $root

        $result.ExitCode | Should -Be 0
        $result.Output | Should -BeNullOrEmpty
    }

    It 'fails closed when the Git repository is invalid' {
        $root = New-LineEndingRepository
        Remove-Item -LiteralPath (Join-Path $root '.git/HEAD') -Force

        $result = Invoke-LineEndingChecker -Root $root
        $output = $result.Output -join "`n"

        $result.ExitCode | Should -Be 1
        $output | Should -Match '^repository:'
        $output | Should -Match 'rev-parse --is-inside-work-tree'
        $output | Should -Match 'exit code: [1-9][0-9]*'
        $output | Should -Match 'stderr:\s*\S'
    }

    It 'fails closed when Git stage output contains a non-regular entry' {
        $root = New-LineEndingRepository
        $stage = (& git -C $root ls-files --stage -- text.txt)
        if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed for parser fixture.' }
        $parts = (($stage -split "`t", 2)[0] -split ' ')
        & git -C $root update-index --cacheinfo "160000,$($parts[1]),text.txt"
        if ($LASTEXITCODE -ne 0) { throw 'git update-index failed for parser fixture.' }

        $result = Invoke-LineEndingChecker -Root $root

        $result.ExitCode | Should -Be 1
        ($result.Output -join "`n") | Should -Match 'unsupported entry'
    }

    It 'preserves status, index bytes, and tracked file bytes' {
        $root = New-LineEndingRepository
        $statusBefore = (& git -C $root status --porcelain=v1) -join "`n"
        $indexBefore = (Get-FileHash -LiteralPath (Join-Path $root '.git/index') -Algorithm SHA256).Hash
        $fileBefore = (Get-FileHash -LiteralPath (Join-Path $root 'text.txt') -Algorithm SHA256).Hash

        $result = Invoke-LineEndingChecker -Root $root

        $statusAfter = (& git -C $root status --porcelain=v1) -join "`n"
        $indexAfter = (Get-FileHash -LiteralPath (Join-Path $root '.git/index') -Algorithm SHA256).Hash
        $fileAfter = (Get-FileHash -LiteralPath (Join-Path $root 'text.txt') -Algorithm SHA256).Hash
        $result.ExitCode | Should -Be 0
        $statusAfter | Should -Be $statusBefore
        $indexAfter | Should -Be $indexBefore
        $fileAfter | Should -Be $fileBefore
    }

    It 'handles non-ASCII tracked paths on every platform' {
        $root = New-LineEndingRepository
        $relativePath = '日本語-é.txt'
        Write-LineEndingFixtureBytes -Root $root -RelativePath $relativePath -Bytes ([Text.Encoding]::UTF8.GetBytes("valid`n"))
        & git -C $root add --all
        if ($LASTEXITCODE -ne 0) { throw 'git add failed for non-ASCII fixture.' }

        $result = Invoke-LineEndingChecker -Root $root

        $result.ExitCode | Should -Be 0
        $result.Output | Should -BeNullOrEmpty
    }

    It 'handles tab and newline path records on Ubuntu' -Skip:(-not $IsLinux) {
        $root = New-LineEndingRepository
        $relativePath = "directory/tab`tname`nfile.txt"
        Write-LineEndingFixtureBytes -Root $root -RelativePath $relativePath -Bytes ([Text.Encoding]::UTF8.GetBytes("valid`n"))
        & git -C $root add --all
        if ($LASTEXITCODE -ne 0) { throw 'git add failed for tab and newline path fixture.' }
        Write-LineEndingFixtureBytes -Root $root -RelativePath $relativePath -Bytes ([Text.Encoding]::UTF8.GetBytes("invalid`r`n"))

        $result = Invoke-LineEndingChecker -Root $root

        $result.ExitCode | Should -Be 1
        ($result.Output -join "`n") | Should -Match 'directory/tab\\tname\\nfile\.txt'
    }

    It 'uses native stdout byte capture to preserve NUL-delimited Git records' {
        $root = New-LineEndingRepository
        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = 'git'
        $startInfo.WorkingDirectory = $root
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        foreach ($argument in @('ls-files', '--stage', '-z')) {
            [void]$startInfo.ArgumentList.Add($argument)
        }
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        try {
            [void]$process.Start()
            $stdout = [IO.MemoryStream]::new()
            $process.StandardOutput.BaseStream.CopyTo($stdout)
            $stderr = $process.StandardError.ReadToEnd()
            $process.WaitForExit()
            $process.ExitCode | Should -Be 0
            $stderr | Should -BeNullOrEmpty
            [Array]::IndexOf($stdout.ToArray(), [byte]0) | Should -BeGreaterThan -1
        }
        finally {
            $process.Dispose()
        }
    }

    It 'invokes the actual package root successfully' -Tag 'actual-package' {
        $result = Invoke-LineEndingChecker -Root $repositoryRoot

        $result.ExitCode | Should -Be 0
        $result.Output | Should -BeNullOrEmpty
    }
}