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

# Exercises the whole resolver with synthetic files and no native installer or network.
Describe 'Unity runtime prerequisites' {
    BeforeAll {
        . (Join-Path $PSScriptRoot 'HostedUnityReviewContracts.Helpers.ps1')
        $resolverPath = Join-Path $repositoryRoot '.github/scripts/Resolve-UnityEditorPath.ps1'
        $testPathCommand = Get-Command Test-Path -CommandType Cmdlet
        if (-not (Get-Command Get-AuthenticodeSignature -ErrorAction SilentlyContinue)) {
            function Get-AuthenticodeSignature {
                [CmdletBinding()]
                param([string]$LiteralPath)
                throw 'Unmocked signature command is forbidden.'
            }
        }

        function Test-InstallerReadProtection {
            param([string]$Path)

            $writer = $null
            try {
                $writer = [IO.File]::Open($Path, 'Open', 'Write', 'ReadWrite')
                return $false
            }
            catch [IO.IOException] { return $true }
            finally { if ($null -ne $writer) { $writer.Dispose() } }
        }

        function Invoke-ResolverFixture {
            param([switch]$WithoutEvidence)

            $arguments = @{
                EditorPath = $rt.editor; RealEditorPathOutputFile = $rt.real
            }
            if (-not $WithoutEvidence) {
                $arguments.RuntimeEvidence = $true
                $arguments.WorkflowRunId = '1234'
                $arguments.WorkflowRunAttempt = '1'
                $arguments.IntendedSha = 'a' * 40
                $arguments.CheckoutSha = 'a' * 40
            }
            $rt.output = @()
            $rt.error = $null
            try {
                $rt.output = @(& $resolverPath @arguments 6> $rt.hostLog 3> $rt.warningLog 2> $rt.errorLog)
            }
            catch { $rt.error = $_ }
            $rt.console = (@($rt.hostLog, $rt.warningLog, $rt.errorLog) | ForEach-Object {
                if ([IO.File]::Exists($_)) { [IO.File]::ReadAllText($_) }
            }) -join "`n"
            if ($rt.error) { $rt.console += $rt.error.ToString() }
            if ([IO.File]::Exists($rt.report)) {
                $rt.public = [IO.File]::ReadAllText($rt.report)
                if ($rt.public.TrimStart().StartsWith('{')) { $rt.state = $rt.public | ConvertFrom-Json }
            }
        }

        function Assert-FirstRuntimeStopped {
            $rt.error | Should -Not -BeNullOrEmpty
            $rt.output.Count | Should -Be 0
            @($rt.probes).Count | Should -Be 0
            [IO.File]::Exists($rt.real) | Should -BeFalse
            [IO.Directory]::Exists((Join-Path $env:RUNNER_TEMP 'PureBaseUnityProxy')) | Should -BeFalse
            $rt.state.runtimes.'vcredist-2013-x64.exe'.stage | Should -Be 'NotReached'
            $rt.state.runtimes.'vcredist-2010-sp1-x64.exe'.exitCode | Should -BeNullOrEmpty
            Should -Invoke Start-Process -Times 0 -Exactly
        }

        function Get-FirstRuntimeMetadata {
            Get-Content -LiteralPath (Join-Path $rt.private '2010-sp1-x64/metadata.json') -Raw
        }
    }

    BeforeEach {
        $savedEnvironment = @{}
        foreach ($name in @('WINDIR', 'RUNNER_TEMP')) {
            $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        }
        $root = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $env:WINDIR = Join-Path $root 'windows'
        $env:RUNNER_TEMP = Join-Path $root 'temp'
        [IO.Directory]::CreateDirectory((Join-Path $env:WINDIR 'System32')) | Out-Null
        [IO.Directory]::CreateDirectory($env:RUNNER_TEMP) | Out-Null
        $script:rt = @{
            root = $root; editor = (Join-Path $root 'Unity.exe'); real = (Join-Path $root 'real.txt')
            private = (Join-Path $env:RUNNER_TEMP 'PureBase-Runtime-Prerequisites-Private-1234-1')
            report = (Join-Path $env:RUNNER_TEMP 'PureBase-Release-Validation-1234-1/runtime-prerequisites/report.json')
            hostLog = (Join-Path $root 'host.txt'); warningLog = (Join-Path $root 'warning.txt'); errorLog = (Join-Path $root 'error.txt')
            downloads = [Collections.Generic.List[object]]::new(); signatures = [Collections.Generic.List[string]]::new()
            installs = [Collections.Generic.List[object]]::new(); probes = [Collections.Generic.List[string]]::new()
            locks = [Collections.Generic.List[bool]]::new(); sleeps = [Collections.Generic.List[int]]::new()
            status = 'Valid'; signatureThrow = $false; lockDownload = $false; held = $null
            failDownloads = 0; exits = @(0, 1638); missingDll = $false; signer = 'Microsoft Corporation PRIVATE_SENTINEL'
            bytes = [byte[]]@(0, 1, 255, 10, 13, 128, 42); sentinel = 'PRIVATE_SENTINEL'
        }
        [IO.File]::WriteAllText($rt.editor, 'Synthetic non-executable fixture')
        Mock Invoke-WebRequest {
            $rt.downloads.Add([pscustomobject]@{ uri = $Uri; path = $OutFile; passThru = $PassThru })
            [IO.File]::WriteAllBytes($OutFile, $rt.bytes)
            if ($rt.downloads.Count -le $rt.failDownloads) { throw "Download PRIVATE_SENTINEL" }
            if ($rt.lockDownload) { $rt.held = [IO.File]::Open($OutFile, 'Open', 'Read', 'None') }
            [pscustomobject]@{
                StatusCode = 200
                BaseResponse = [pscustomobject]@{ RequestMessage = [pscustomobject]@{ RequestUri = [uri]'https://example.invalid/?PRIVATE_SENTINEL' } }
                Headers = @{ 'Content-Type' = @('PRIVATE_SENTINEL'); 'Content-Length' = @('7'); 'Content-Encoding' = @('PRIVATE_SENTINEL')
                    Date = @('Fri, 30 Oct 2026 17:00:00 GMT'); 'Last-Modified' = @('Fri, 30 Oct 2026 16:00:00 GMT'); ETag = @('PRIVATE_SENTINEL') }
            }
        }
        Mock Get-AuthenticodeSignature {
            $rt.signatures.Add($LiteralPath)
            $rt.locks.Add((Test-InstallerReadProtection $LiteralPath))
            if ($rt.signatureThrow) { throw 'Signature PRIVATE_SENTINEL' }
            $certificate = if ($null -eq $rt.signer) { $null } else {
                [pscustomobject]@{
                    RawData = [byte[]]@(9, 8, 0, 255); Subject = $rt.signer; Issuer = 'PRIVATE_SENTINEL'
                    SerialNumber = 'PRIVATE_SENTINEL'; Thumbprint = 'PRIVATE_SENTINEL'
                    NotBefore = [datetime]'2026-01-01Z'; NotAfter = [datetime]'2027-01-01Z'
                }
            }
            [pscustomobject]@{
                Status = [Enum]::Parse([Management.Automation.SignatureStatus], $rt.status)
                StatusMessage = 'Status PRIVATE_SENTINEL'; SignatureType = 'Authenticode'; IsOSBinary = $false
                Path = $LiteralPath; SignerCertificate = $certificate; TimeStamperCertificate = $certificate
            }
        }
        Mock Start-Process {
            $rt.locks.Add((Test-InstallerReadProtection $FilePath))
            $rt.installs.Add([pscustomobject]@{ path = $FilePath; arguments = $ArgumentList; wait = $Wait; passThru = $PassThru; noWindow = $NoNewWindow })
            if (-not $rt.missingDll) {
                $suffix = if ($FilePath -match '2010') { '100' } else { '120' }
                foreach ($prefix in @('msvcr', 'msvcp')) {
                    [IO.File]::WriteAllText((Join-Path $env:WINDIR "System32/$prefix$suffix.dll"), 'synthetic DLL')
                }
            }
            [pscustomobject]@{ ExitCode = $rt.exits[$rt.installs.Count - 1] }
        }
        Mock Start-Sleep { $rt.sleeps.Add([int]$Seconds) }
        Mock Test-Path -ParameterFilter { $LiteralPath -match '(msvc[pr]120\.dll|Unity\.exe)$' } {
            $rt.probes.Add([string]$LiteralPath)
            # Invoke the captured cmdlet rather than re-entering Pester's named mock.
            & $testPathCommand -LiteralPath $LiteralPath -PathType $PathType
        }
    }

    AfterEach {
        if ($null -ne $rt.held) { $rt.held.Dispose() }
        foreach ($name in @('WINDIR', 'RUNNER_TEMP')) {
            if ($null -eq $savedEnvironment[$name]) { Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue }
            else { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
        }
    }

    It 'uses pwsh-compatible retried runtime downloads' {
        $resolverScript.Contains("`$ProgressPreference = 'SilentlyContinue'") | Should -BeTrue
        $resolverScript.Contains('function Invoke-DownloadWithRetry') | Should -BeTrue
        $resolverScript.Contains('MaximumAttempts = 3') | Should -BeTrue
        $resolverScript.Contains('UseBasicParsing') | Should -BeFalse
    }

    It 'retains a rejected primary and complete private observations without public disclosure' {
        $rt.status = 'UnknownError'
        Invoke-ResolverFixture
        Assert-FirstRuntimeStopped
        $state = $rt.state.runtimes.'vcredist-2010-sp1-x64.exe'
        $state.signature | Should -Be 'UnknownError'
        $state.failure | Should -Be 'SignatureRejected'
        $rt.signatures.Count | Should -Be 1
        $rt.locks | Should -Be @($true)
        $metadata = Get-FirstRuntimeMetadata | ConvertFrom-Json
        $metadata.http.finalUri | Should -Be 'https://example.invalid/?PRIVATE_SENTINEL'
        $metadata.http.headers.'Content-Type' | Should -Be @('PRIVATE_SENTINEL')
        $metadata.http.headers.'Content-Length' | Should -Be @('7')
        $metadata.http.headers.'Content-Encoding' | Should -Be @('PRIVATE_SENTINEL')
        $metadata.http.headers.Date | Should -Be @('Fri, 30 Oct 2026 17:00:00 GMT')
        $metadata.http.headers.'Last-Modified' | Should -Be @('Fri, 30 Oct 2026 16:00:00 GMT')
        $metadata.http.headers.ETag | Should -Be @('PRIVATE_SENTINEL')
        $signature = Get-Content (Join-Path $rt.private '2010-sp1-x64/signature.json') -Raw | ConvertFrom-Json
        $signature.statusCode | Should -Be 1
        $signature.statusMessage | Should -Be 'Status PRIVATE_SENTINEL'
        $signature.signerCertificate.subject | Should -Be 'Microsoft Corporation PRIVATE_SENTINEL'
        foreach ($name in @('signer', 'timestamp')) {
            [IO.File]::ReadAllBytes((Join-Path $rt.private "2010-sp1-x64/$name.der")) | Should -Be ([byte[]]@(9, 8, 0, 255))
        }
        ($rt.public + $rt.console) | Should -Not -Match 'PRIVATE_SENTINEL|example\.invalid|StatusMessage|Microsoft Corporation'
    }

    It 'distinguishes an unreturned signature result and retains the real private exception' {
        $rt.signatureThrow = $true
        Invoke-ResolverFixture
        Assert-FirstRuntimeStopped
        $rt.state.runtimes.'vcredist-2010-sp1-x64.exe'.signature | Should -Be 'ResultNotReturned'
        $rt.state.runtimes.'vcredist-2010-sp1-x64.exe'.failure | Should -Be 'SignatureNotReturned'
        Get-FirstRuntimeMetadata | Should -Match 'Signature PRIVATE_SENTINEL'
        (Get-FirstRuntimeMetadata) | Should -Not -Match 'UnknownError'
        $rt.signatures.Count | Should -Be 1
        ($rt.public + $rt.console) | Should -Not -Match 'PRIVATE_SENTINEL'
    }

    It 'keeps the primary signature failure when secondary persistence fails' -ForEach @(
        @{ ThrowSignature = $false; Expected = 'SignatureRejected' },
        @{ ThrowSignature = $true; Expected = 'SignatureNotReturned' }
    ) {
        $rt.status = 'UnknownError'
        $rt.signatureThrow = $ThrowSignature
        Mock Set-Content -ParameterFilter { $LiteralPath -like '*metadata.json' -and $Value -match 'SignatureReturned|signatureError' } {
            throw 'Persistence PRIVATE_SENTINEL'
        }
        Invoke-ResolverFixture
        Assert-FirstRuntimeStopped
        $rt.state.runtimes.'vcredist-2010-sp1-x64.exe'.failure | Should -Be $Expected
        $rt.state.runtimes.'vcredist-2010-sp1-x64.exe'.secondary | Should -Be 'PrivateSaveFailed'
        $rt.state.runtimes.'vcredist-2010-sp1-x64.exe'.payload | Should -Be 'Incomplete'
        $rt.signatures.Count | Should -Be 1
        ($rt.public + $rt.console) | Should -Not -Match 'PRIVATE_SENTINEL'
    }

    It 'requires a non-null Microsoft signer even for Valid status' -ForEach @(
        @{ Signer = $null }, @{ Signer = 'Other publisher' }
    ) {
        $rt.signer = $Signer
        Invoke-ResolverFixture
        Assert-FirstRuntimeStopped
        $rt.state.runtimes.'vcredist-2010-sp1-x64.exe'.failure | Should -Be 'SignatureRejected'
        $rt.signatures.Count | Should -Be 1
    }

    It 'stops before downloading on a colliding evidence destination without overwriting it' {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($rt.report)) | Out-Null
        [IO.File]::WriteAllText($rt.report, 'preserve me')
        Invoke-ResolverFixture
        $rt.error.ToString() | Should -Match 'RuntimeEvidenceInitializationFailed'
        [IO.File]::ReadAllText($rt.report) | Should -Be 'preserve me'
        $rt.downloads.Count | Should -Be 0
        $rt.probes.Count | Should -Be 0
        Should -Invoke Start-Process -Times 0 -Exactly
    }

    It 'fails closed on shared-read refusal, copy mismatch, or mandatory save failure' -ForEach @(
        @{ Fault = 'Sharing' }, @{ Fault = 'Copy' }, @{ Fault = 'Save' }
    ) {
        if ($Fault -eq 'Sharing') { $rt.lockDownload = $true }
        if ($Fault -eq 'Copy') {
            Mock Copy-Item { [IO.File]::WriteAllText($Destination, 'mismatch') }
        }
        if ($Fault -eq 'Save') {
            Mock Set-Content -ParameterFilter { $LiteralPath -like '*signature.json' } { throw 'Save PRIVATE_SENTINEL' }
        }
        Invoke-ResolverFixture
        Assert-FirstRuntimeStopped
        $rt.state.runtimes.'vcredist-2010-sp1-x64.exe'.failure | Should -Be 'EvidenceIncomplete'
        $rt.state.runtimes.'vcredist-2010-sp1-x64.exe'.payload | Should -Be 'Incomplete'
        $rt.downloads.Count | Should -Be 1
        $rt.sleeps.Count | Should -Be 0
        ($rt.public + $rt.console) | Should -Not -Match 'PRIVATE_SENTINEL'
    }

    It 'classifies a single Valid-path save failure at <Stage>/<File> without requesting install' -ForEach @(
        @{ Stage = 'Installer'; File = 'metadata.json'; Secondary = 'PrivateSaveFailed' },
        @{ Stage = 'Installer'; File = 'report.json'; Secondary = 'ReportSaveFailed' },
        @{ Stage = 'SignatureReturned'; File = 'report.json'; Secondary = 'ReportSaveFailed' }
    ) {
        $rt.saveStage = $Stage
        $rt.saveFile = $File
        $rt.saveFaults = 0
        Mock Set-Content -ParameterFilter {
            $LiteralPath -like "*$($rt.saveFile)" -and $rt.saveFaults -eq 0 -and
            $Value -match ('"stage": "' + $rt.saveStage + '"')
        } {
            $rt.saveFaults++
            throw 'Persistence PRIVATE_SENTINEL'
        }
        Invoke-ResolverFixture
        Assert-FirstRuntimeStopped
        $state = $rt.state.runtimes.'vcredist-2010-sp1-x64.exe'
        $state.failure | Should -Be 'EvidenceIncomplete'
        $state.secondary | Should -Be $Secondary
        $state.payload | Should -Be 'Incomplete'
        $state.signature | Should -Be 'Valid'
        $state.status | Should -Be 'Downloaded'
        $state.installer | Should -Be 'NotReached'
        $metadata = Get-FirstRuntimeMetadata | ConvertFrom-Json
        $metadata.state.failure | Should -Be $state.failure
        $metadata.state.secondary | Should -Be $Secondary
        $metadata.state.payload | Should -Be 'Incomplete'
        $metadata.state.installer | Should -Be 'NotReached'
        $metadata.state.exitCode | Should -BeNullOrEmpty
        $rt.saveFaults | Should -Be 1
        $rt.downloads.Count | Should -Be 1
        $rt.signatures.Count | Should -Be 1
        $rt.locks | Should -Be @($true)
        $rt.sleeps.Count | Should -Be 0
        $rt.error.ToString() | Should -Be "RuntimePrerequisiteFailed: EvidenceIncomplete; secondary=$Secondary"
        ($rt.public + $rt.console) | Should -Not -Match 'PRIVATE_SENTINEL'
    }

    It 'retains a real installer start failure when final private persistence fails' {
        Mock Start-Process { throw 'Installer PRIVATE_SENTINEL' }
        Mock Set-Content -ParameterFilter { $LiteralPath -like '*metadata.json' -and $Value -match 'SaveRequested' } {
            throw 'Persistence PRIVATE_SENTINEL'
        }
        Invoke-ResolverFixture
        $state = $rt.state.runtimes.'vcredist-2010-sp1-x64.exe'
        $state.failure | Should -Be 'InstallerStartFailed'
        $state.secondary | Should -Be 'PrivateSaveFailed'
        $state.payload | Should -Be 'Incomplete'
        $state.installer | Should -Be 'StartRequested'
        $state.exitCode | Should -BeNullOrEmpty
        $rt.error.ToString() | Should -Be 'RuntimePrerequisiteFailed: InstallerStartFailed; secondary=PrivateSaveFailed'
        $rt.output.Count | Should -Be 0
        $rt.probes.Count | Should -Be 0
        $rt.state.runtimes.'vcredist-2013-x64.exe'.stage | Should -Be 'NotReached'
        [IO.File]::Exists($rt.real) | Should -BeFalse
        $rt.downloads.Count | Should -Be 1
        $rt.signatures.Count | Should -Be 1
        $rt.sleeps.Count | Should -Be 0
        Should -Invoke Start-Process -Times 1 -Exactly
        ($rt.public + $rt.console) | Should -Not -Match 'PRIVATE_SENTINEL'
    }

    It 'preserves exact bytes, protected install order, arguments and a single success path' {
        Invoke-ResolverFixture
        $rt.error | Should -BeNullOrEmpty -Because (Get-FirstRuntimeMetadata)
        $rt.output | Should -Be @((Join-Path $env:RUNNER_TEMP 'PureBaseUnityProxy/2022.3.28f1/Editor/Unity.cmd'))
        [IO.File]::Exists($rt.output[0]) | Should -BeTrue
        [IO.File]::ReadAllText($rt.real).Trim() | Should -Be $rt.editor
        $rt.installs.Count | Should -Be 2
        $rt.installs[0].arguments | Should -Be @('/quiet', '/norestart')
        $rt.installs[1].arguments | Should -Be @('/install', '/quiet', '/norestart')
        $rt.locks | Should -Be @($true, $true, $true, $true)
        $rt.downloads[0].uri | Should -Be 'https://download.microsoft.com/download/1/6/5/165255E7-1014-4D0A-B094-B6A430A6BFFC/vcredist_x64.exe'
        $rt.downloads[1].uri | Should -Be 'https://download.microsoft.com/download/0/5/6/056DCDA9-D667-4E27-8001-8A0C6971D6B1/vcredist_x64.exe'
        foreach ($index in @(0, 1)) {
            $rt.installs[$index].path | Should -Be $rt.signatures[$index]
            $rt.installs[$index].path | Should -Be $rt.downloads[$index].path
            $rt.installs[$index].wait | Should -BeTrue
            $rt.installs[$index].passThru | Should -BeTrue
            $rt.installs[$index].noWindow | Should -BeTrue
            $rt.downloads[$index].passThru | Should -BeTrue
            $id = @('2010-sp1-x64', '2013-x64')[$index]
            $copy = Join-Path $rt.private "$id/installer.bytes"
            [IO.File]::ReadAllBytes($copy) | Should -Be $rt.bytes
            $state = $rt.state.runtimes."vcredist-$id.exe"
            $state.length | Should -Be $rt.bytes.Length
            $state.sha256 | Should -Be (Get-FileHash $copy -Algorithm SHA256).Hash.ToLowerInvariant()
            $state.files | Should -Be @($true, $true)
        }
        ($rt.public + $rt.console + ($rt.output -join '')) | Should -Not -Match 'PRIVATE_SENTINEL'
    }

    It 'keeps the no-evidence caller single-valued and accepts reboot-required exits' {
        $rt.exits = @(3010, 0)
        Invoke-ResolverFixture -WithoutEvidence
        $rt.error | Should -BeNullOrEmpty -Because $rt.console
        $rt.output | Should -Be @((Join-Path $env:RUNNER_TEMP 'PureBaseUnityProxy/2022.3.28f1/Editor/Unity.cmd'))
        [IO.File]::Exists($rt.report) | Should -BeFalse
        $rt.installs.Count | Should -Be 2
    }

    It 'records ExistingRuntime without download, signature or installer' {
        foreach ($name in @('msvcr100', 'msvcp100', 'msvcr120', 'msvcp120')) {
            [IO.File]::WriteAllText((Join-Path $env:WINDIR "System32/$name.dll"), 'synthetic DLL')
        }
        Invoke-ResolverFixture
        $rt.error | Should -BeNullOrEmpty
        $rt.output.Count | Should -Be 1
        foreach ($id in @('2010-sp1-x64', '2013-x64')) {
            $rt.state.runtimes."vcredist-$id.exe".status | Should -Be 'ExistingRuntime'
        }
        $rt.downloads.Count | Should -Be 0
        $rt.signatures.Count | Should -Be 0
        Should -Invoke Start-Process -Times 0 -Exactly
    }

    It 'does not advance after rejected installer exit or missing DLL postcheck' -ForEach @(
        @{ Exit = 42; Missing = $false; Failure = 'InstallerExitRejected' },
        @{ Exit = 0; Missing = $true; Failure = 'RuntimeFilesMissing' }
    ) {
        $rt.exits = @($Exit)
        $rt.missingDll = $Missing
        Invoke-ResolverFixture
        $rt.error | Should -Not -BeNullOrEmpty
        $rt.probes.Count | Should -Be 0
        $rt.state.runtimes.'vcredist-2010-sp1-x64.exe'.failure | Should -Be $Failure
        $rt.state.runtimes.'vcredist-2013-x64.exe'.stage | Should -Be 'NotReached'
        $rt.installs.Count | Should -Be 1
    }

    It 'retries only download exceptions with two and four second delays' {
        $rt.failDownloads = 2
        Invoke-ResolverFixture
        $rt.error | Should -BeNullOrEmpty -Because (Get-FirstRuntimeMetadata)
        $rt.downloads.Count | Should -Be 4
        $rt.signatures.Count | Should -Be 2
        $rt.sleeps | Should -Be @(2, 4)
        Get-FirstRuntimeMetadata | Should -Match 'Download PRIVATE_SENTINEL'
        ($rt.public + $rt.console) | Should -Not -Match 'PRIVATE_SENTINEL'
    }

    It 'retains partial download failure privately after exactly three attempts' {
        $rt.failDownloads = 3
        Invoke-ResolverFixture
        Assert-FirstRuntimeStopped
        $rt.downloads.Count | Should -Be 3
        $rt.signatures.Count | Should -Be 0
        $rt.sleeps | Should -Be @(2, 4)
        $rt.state.runtimes.'vcredist-2010-sp1-x64.exe'.failure | Should -Be 'DownloadFailed'
        $rt.state.runtimes.'vcredist-2010-sp1-x64.exe'.payload | Should -Be 'Partial'
        Get-FirstRuntimeMetadata | Should -Match 'Download PRIVATE_SENTINEL'
        ($rt.public + $rt.console) | Should -Not -Match 'PRIVATE_SENTINEL'
    }
}