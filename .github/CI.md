<!--
Copyright 2026 Penguin

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
-->

# GitHub Actions configuration

## Unity runner

Daily and release validation run on GitHub-hosted `windows-2022` runners with Unity
2022.3.28f1 (revision `6bae5ce6b222`). After checkout, each execution job invokes
`.github/actions/setup-unity-editor` once, activates Unity Personal through
`buildalon/activate-unity-license`, and passes the discovered Editor path to its validation scripts.
Daily checks out its authorized ref; Release validation checks out the dispatched commit SHA. Both
retain read-only contents permission and `persist-credentials: false`.

The composite reads fixed inputs with `Tests/Tooling/PureBaseValidationInputs.ps1`: the Unity
version and revision come from the consumer project's `ProjectVersion.txt`, and the CLI version
and SHA-256 come from `Tests/Release/ci-toolchain.json`. For Windows X64 it downloads the versioned
`unity-windows-x64.exe` from Unity's CLI CDN, verifies its SHA-256, and only then places it as
`unity.exe`. The expected checksum must come from Unity's official version-specific manifest.
Review and update the CLI version and checksum together; a change requires hosted verification.
Accepted CLI versions match `^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:\.[0-9A-Za-z]+)*)?$`.
Unsupported platforms, malformed inputs, download errors and checksum mismatches fail closed;
an existing CLI remains untouched until verification succeeds.

The same verified executable supplies `unity install-path` and, on a cache miss,
`unity install <version> -y --accept-eula --non-interactive`. The pinned cache restore and save
actions use the exact key
`pure-base-unity-editor-v1-${{ runner.os }}-${{ runner.arch }}-${{ steps.inputs.outputs.version }}-${{ steps.inputs.outputs.revision }}`.
There are no restore keys or cross-OS archives. GitHub cache scope can permit reuse of a matching
default-branch entry. The cache contains only the version-specific paths under the discovered
install roots, excludes the CLI, and does not contain Unity activation secrets.

On an exact hit, setup skips installation and resolves the Editor from those roots. On a miss it
installs the fixed version, resolves the Editor, and saves the limited paths only after successful
installation and resolution. The action exposes `editor-path` and `cache-hit`. Invalid paths,
native command failures, an invalid Editor candidate, or anything other than one matching candidate
fail the setup rather than selecting another Editor. Cache-service warnings retain the cache
action's own behavior; explicit errors are not suppressed by setup.

The cache is an optimization, not a guarantee that the Editor is compatible or authentic. The
resolver checks the candidate's version/revision PE metadata without starting Unity, but neither
that check, file existence, nor a cache hit verifies the Editor's checksum or provenance. Existing
runtime version and revision validation still applies after Unity starts; CLI SHA-256 verification
does not authenticate the Editor installation. Do not create tags or branches or use synthetic
identities to force a production-key miss: normal cache scope can still provide an exact match.

Create these Actions secrets for Unity Personal activation:

- `UNITY_EMAIL`: email address of the Unity ID used for CI.
- `UNITY_PASSWORD`: password of the Unity ID used for CI.

Use a real human Unity ID. Service accounts do not have a Unity Personal entitlement. Personal
activation does not use a serial number. The activation Action uses Unity Licensing Client and its
post-job handler releases the ephemeral runner's entitlement automatically. Third-party Actions are
pinned to audited commits rather than moving tags.

The repository is public, and Unity validation executes repository code. Daily uses GitHub's
`pull_request` event. For fork-originated pull requests, GitHub withholds repository secrets and
provides only a read-only `GITHUB_TOKEN`; GitHub Actions treats Dependabot pull requests as
fork-originated for these secret restrictions. This repository's policy separately trusts ordinary
non-draft branches in this repository: their writers may run their pull request head code with the
Unity activation credentials.

The workflows construct a temporary Unity project with Pure-Base and a verified Shader-Core release
as embedded packages. Shader-Core is pinned to the exact reviewed version `0.3.0`, matching the exact
dependency identity required by Pure Base for both Daily and release validation. Git line-ending
conversion is disabled, and the project is configured for Linear color space before Daily
validation. Shader-Core releases are pinned explicitly because compatibility across `0.x` versions
is not assumed; importer, ProjectSettings, and method-shape contracts are compatibility-sensitive.
Each version change requires explicit review and an updated pin. The version pin describes the CI
project and tested runtime expectation; it does not modify the Unity Editor, ProjectSettings, or VPM
configuration of a developer's existing workspace.

## Validation inputs and updates

The authoritative inputs for CI and release validation are:

| Input | Source and role |
| --- | --- |
| Shader-Core requirement | `package.json` → `vpmDependencies["jp.lilxyzw.shadercore"]`, an exact version. |
| Acquisition pins | `Tests/Release/ci-toolchain.json` → `shaderCore.archiveUrl` and `shaderCore.archiveSha256`, plus `unityCli.version` and `unityCli.sha256`. |
| Expanded Shader-Core identity | `Tests/Release/shader-core.sha256.json`, including package metadata and all file entries. |
| Unity version and revision | `Tests/Release/ConsumerProject/ProjectSettings/ProjectVersion.txt`. |
| Bootstrap dependency graph | `Tests/Release/first-bootstrap-package-profile.json`, a reviewed static expectation. |

For a compatible Shader-Core update, review the release and update the exact package requirement
and the matching version-specific official archive URL and lowercase SHA-256 in the toolchain.
Obtain that explicit archive from the reviewed source, verify its SHA-256 against the reviewed pin,
then generate the complete identity from the package root:

```powershell
pwsh -NoProfile -File ./Tests/Release/Build-PureBaseRelease.ps1 -WriteShaderCoreManifest -ShaderCoreArchivePath '<path-to-reviewed-archive.zip>'
```

Replace the placeholder with the actual archive path. The generator verifies the archive digest,
safely extracts it, verifies package metadata, and generates every identity entry from that same
extracted tree. It can run when the old identity is missing or describes the previous version; it
writes a temporary manifest before atomic replacement and preserves the existing manifest on
failure. Hashing an arbitrary local tree is not equivalent to verifying the release archive.
Ordinary validation instead requires the complete matching identity and fails closed for a missing
or inconsistent input; it does not learn an identity from the installed tree. Review the resulting
full identity and run the relevant existing checks, including Daily and, when needed, hosted Release
validation. Normal compatible updates do not require changing workflow or script version literals.

Review the bootstrap graph for compatibility when Shader-Core dependencies or Unity change; do not
automatically accept a graph observed during a run. For Unity updates, review ProjectVersion's
version and revision, the bootstrap profile, and the separate Daily and initializer runtime version
constants. Those constants are explicit compatibility checks, not automatically updated by
ProjectVersion. The `2022.3.22f1` baseline provenance and regeneration guard remain separate.
Fixed test fixture versions and the canonical ZIP fixture are independent test inputs; do not update
them merely to follow the production Shader-Core pin.

If Shader-Core's internal API, generated format, or UPM dependencies change, also review the
affected host, phase and generation checks and the bootstrap graph. Update an Action's reviewed
commit pin in its workflow or action after dependency review; contract tests check the expected
owner/repository and a 40-hex pin rather than requiring that same particular commit everywhere.
Pin format checks do not replace review of the pinned dependency.

## VRChat SDK parity boundary

The generated CI project intentionally does **not** install `com.vrchat.base`,
`com.vrchat.avatars`, or `com.vrchat.worlds`. No VRChat SDK editor initialization or Project Setup
code runs in CI.

`Tests/Release/ConsumerProject/ProjectSettings/QualitySettings.asset` is a reviewed snapshot copied
from a local Unity 2022.3.22f1 VRChat project after its VRC-named quality profiles had been
established. CI copies that snapshot into the generated project and validates the selected
`VRC High` profile and its exact shadow and MSAA values at runtime.

This distinction is intentional:

- The name `VRC High` records the snapshot's project provenance; it does not prove that the VRChat
  SDK is installed in CI.
- Daily validates Pure-Base rendering under the captured Unity `QualitySettings` values. It does not
  claim full behavioral equivalence with a project containing the VRChat SDK.
- After a VRChat SDK upgrade, reimport, or Project Setup operation, compare the local
  `ProjectSettings/QualitySettings.asset` with the committed snapshot. If any profile name or value
  changes, update the snapshot, the CI assertions, and any affected reviewed rendering baseline.
- Tests that depend on VRChat SDK APIs, scripting defines, import hooks, or build behavior must add
  separately pinned SDK packages. The QualitySettings snapshot is not a substitute for those tests.

## Repository configuration

Create a protected `release` environment. Requiring an approval before deployment is strongly
recommended.

Create these repository variables:

- `RELEASE_BRANCH`: the only branch from which an actual release may be dispatched. Use
  `future/fist-write` while that is the release branch, then change it when the release branch
  moves.
- `VPM_REPOSITORY`: the destination VPM repository in `owner/name` form.

Create these Actions secrets:

- `APP_CLIENT_ID`: GitHub App client ID.
- `APP_PRIVATE_KEY`: GitHub App private key.

Install the same GitHub App on both Pure-Base and the repository named by `VPM_REPOSITORY`.
On Pure-Base, grant repository **Contents: write**, **Administration: read**, **Actions: read**,
and **Workflows: write**. On the VPM repository, grant **Contents: write** only. The release
workflow creates separate installation tokens for Pure-Base and the VPM repository, with each
token restricted to its target repository and requested permissions.

Enable immutable releases in the Pure-Base repository settings before the first release. The
release script checks `GET /repos/{owner}/{repo}/immutable-releases` before running Unity release
validation and stops without changing the repository when the setting is not enabled.

## Workflows

`Daily` runs `Tests/Run-PureBaseRegression.ps1 -Mode Initialize` followed by `-Mode Daily` on every
push and on non-draft pull requests whose head branch belongs to Pure-Base. The authorization job
uses the tested `Resolve-PureBaseDailySource` helper as a best-effort operational source-selection
and runner-cost gate under the checked-in workflow. It rejects fork, draft, and Dependabot pull
requests before Unity runner allocation. Dependabot receives Unity coverage only after its change
is merged, through the unchanged `push` path. This resolver is not a protection against a malicious
pull request that changes workflow or helper code; the Unity credentials for trusted same-repository
branches rely on the repository's branch-writer trust policy.

`Release validation` is manual and read-only, and is the normal hosted Release consumer validation
path; do not run `Tests/Release/Run-PureBaseReleaseValidation.ps1` locally during ordinary
development. Immediately after the initial repository-state snapshot, the workflow provisions the
required Visual C++ runtime pairs before Shader-Core acquisition, Unity restore or license
activation. This step has a 15-minute timeout within the existing 240-minute validation job.
Configure/import runs through the Unity watchdog proxy. The audited release-validation runner
receives the real `Unity.exe`, because it intentionally rejects wrapper executables while proving
the required Unity version from the editor path. The
workflow uploads bounded evidence from the artifact root: bootstrap and per-run evidence, archive
ZIPs and digests, the Shader-Core manifest, run/cleanup summaries, the validated package, repository
state, and outer logs. It excludes the whole ConsumerProject, Unity cache, secrets, and arbitrary
crash dumps. The success exporter runs only on success; evidence upload always runs and rejects
missing expected files. A hosted exact-head run is required to establish results for a committed and
pushed revision.

Each new consumer run copies the full identity as `shader-core.sha256.json` into its run root;
the dependency-input index references that name and the workflow uploads it. Older run artifacts
retain their original versioned names and must be read using their own recorded layout; they are
not renamed or regenerated.

The first-bootstrap package profile is a static expectation for Unity `2022.3.28f1` (revision
`6bae5ce6b222`), not a profile learned from each run. Its accepted manifest has 48 direct entries
and its lock has 53 entries. The full graph comparison includes versions, dependency edges and empty
dependency maps, local package URIs, lock metadata, and the separately retained unprojected registry
URL evidence. In particular, Purchasing is `4.11.0` in the manifest and lock; it requires
`com.unity.services.core` `1.8.2`, while the observed lock resolves that package to `1.12.5`. The
Purchasing lock entry no longer has a `com.unity.modules.unityanalytics` edge; the module remains in
the graph. These values were adopted only after comparing the complete static profile with the full
original manifest and lock evidence, not by accepting the observed graph automatically.

A strict profile mismatch is a validation failure even when the workflow successfully uploads the
complete evidence needed to inspect it. Evidence receipt is not a successful Release validation.
The profile does not establish exact UPM solver behavior or future registry stability; their cause
and reproducibility remain unknown. IAP4 support has been unavailable since 2026-06-08. The
Unity `2022.3.22f1` numeric oracle, the provenance and scope of the `QualitySettings` snapshot, and
the mandatory `WarmUp` issue remain unresolved. Unity `2022.3.28f1` is not a VRChat target-version
guarantee; the snapshot does not prove SDK parity, and Player stripping has not been tested.

Release uses the runner's existing Chocolatey CLI; it does not bootstrap or upgrade Chocolatey.
The step requires an existing 64-bit Windows process and `System32`, resolves `choco.exe` as an
application, and accepts only a successfully observed 2.x CLI version. It checks the Visual C++
2010 pair (`msvcr100.dll`, `msvcp100.dll`) before the 2013 pair (`msvcr120.dll`, `msvcp120.dll`).
Only a missing pair triggers one synchronous package request, in this order:

| Package | Requested package version | Required `System32` DLL pair |
| --- | --- | --- |
| `vcredist2010` | `10.0.40219.32503` | `msvcr100.dll`, `msvcp100.dll` |
| `vcredist2013` | `12.0.40660.20180427` | `msvcr120.dll`, `msvcp120.dll` |

Each request uses only `https://community.chocolatey.org/api/v2/` and the arguments
`install <package> --version=<version> --source=https://community.chocolatey.org/api/v2/ -y --no-progress --requirechecksum --use-package-exit-codes`.
Only native exit `0` or `3010` is accepted, and the requested DLL pair must then exist; after both
checks all four DLLs are checked again. `3010` records that a reboot is required, but the workflow
does not reboot. The wrapper does not retry requests; there is no reinstall, fallback download or
old installer path in Release. Package-internal network retry behavior is not established here.
The resolver is called with `RequireExistingRuntime`: if a required DLL is missing, it fails before
creating an installer path, downloading, checking a signature or starting an installer. The switch
defaults to false, so the existing Daily caller and its prior runtime behavior are retained.

These are requested Chocolatey package versions when a pair is missing, not assertions about the
version of pre-existing DLLs. If both DLLs in a pair already exist, that pair is skipped and is not
proof that the fixed package or DLL version is installed. The packages may install x86 components
as well as x64 components. Release now relies on the preinstalled Chocolatey CLI, the selected feed
and package script, and required package checksums; this is not equivalent to the former explicit
same-bytes Microsoft installer Authenticode check. A checksum requirement does not establish package
authenticity or reproduce that signature check. DLL existence does not establish loadability,
authenticity or version. A package request or accepted exit alone also does not prove a new install
or successful Unity runtime use; the postchecks and the later full Release validation remain
separate evidence.

The workflow adds only
`${{ runner.temp }}\PureBase-Release-Validation-${{ github.run_id }}-${{ github.run_attempt }}\runtime-prerequisites\report.json`
to the existing always-uploaded artifact. This finite schema-version-2 report uses provider
`Chocolatey` and records run/attempt/job and intended/checkout SHA identity, the observed CLI version,
fixed source, package names/requested versions, pair pre/post booleans, `NotReached`,
`ExistingRuntime` or `InstallRequested` status, the observed native exit or null, reboot-required
state, final four-DLL booleans, fixed primary/secondary result codes and UTC time. It excludes
arbitrary paths, URI query values, environment dumps, raw exceptions and DLL FileVersion data.
Private temporary stdout/stderr files from Chocolatey are not uploaded; they are not a durable
evidence transfer mechanism. This Release diagnostic contract accepts the bounded report and
existing run/job results; it does not require cross-session receipt of the original EXE, nupkg, all
HTTP data, signature DER or complete raw Chocolatey logs. This narrower contract does not mean raw
records were acquired, does not resolve prior unknown diagnostic causes, and does not authorize a
new hosted run. Report initialization/save failures stop validation; a report-save failure is kept
secondary to the primary prerequisite failure when possible.

The existing Daily caller without runtime-evidence inputs continues to use the resolver's default
legacy path and return its single Unity watchdog proxy path. Release no longer passes the old
`RuntimeEvidence` identity inputs to that resolver; it passes the identity to the prerequisite
producer and uploads the one bounded report. These changes do not alter workflow discovery, add a CI
job, or change the existing bounded Release evidence list.

When a Daily runtime pair is missing, the resolver downloads the Microsoft installer and requires
a valid Authenticode signature with a Microsoft Corporation signer. It holds a read-only handle
against replacement while it copies, checks and runs the protected installer, then checks the
required files. If the pair already exists, no installer signature is checked. This differs from
Release's pinned Chocolatey request and checksum policy above; neither a pre-existing DLL nor a
cache hit proves authenticity or loadability.

The runtime prerequisite fixture observes this protection differently by platform: Windows
checks that a write open is rejected by the holder's sharing mode, while Linux probes contention
for a cooperative advisory exclusive lock. The Linux observation does not prevent arbitrary writers
and is not equivalent to Windows writer-share rejection. The platform-specific probe changes the
test observation, not the runtime resolver. Windows Pester results do not establish the Linux
runner's behavior; the existing Ubuntu Automation job must validate that platform separately.

The runner also captures the original `Packages/manifest.json` and
`Packages/packages-lock.json` bytes at the `pre-bootstrap`, `after-scene-bootstrap`, `quiescent`,
`second-after-scene`, and `second-quiescent` boundaries. Each stage is written below
`bootstrap/dependency-inputs/<stage>/`, with its `capture.json`; the parent
`bootstrap/dependency-inputs/index.json` records stage summaries, runner provenance, and the
separate primary-validation result and capture codes. Captured bytes retain their original encoding
markers, whitespace, and line endings. Before publication, the runner screens each raw document
against the allowed dependency-input schema; rejected raw bytes are withheld rather than saved in
the public artifact. This is a shared-read, handle-held capture and comparison, not an
operating-system atomic snapshot. `Partial` stage/index status and `NotReached` later stages describe
capture progress separately from the primary validation failure. A missing required input, privacy
rejection, capture or metadata failure, or loss of expected files during workflow upload makes the
evidence handoff incomplete; it is not a successful complete capture.

`package.json` is the sole release identity and version declaration. The `version` input to the
manual `Release` workflow is confirmation only: it must exactly match the checked-out package
version, and the workflow never writes or commits a package version. A release-preparation commit
therefore follows this hosted sequence:

1. Commit the intended package changes, including the exact `package.json` version, on the configured
  release branch.
2. Manually run `Release validation` for that exact commit SHA. This read-only producer validates
  the package and emits a deterministic Store-mode ZIP, its lowercase SHA-256 sidecar, and a
  schema-1 `release-validation.json` provenance manifest in one Actions artifact.
3. Manually run `Release` from the same release-branch SHA and confirm the same package version.
  It selects the latest matching `release-validation.yml` `workflow_dispatch` run by run number and
  attempt, downloads that run's unexpired artifact, and revalidates its provenance, ZIP, manifest,
  and digest. The downloaded ZIP is the only publication payload; Release never rebuilds it.

For the final committed and pushed SHA, the required hosted validation must succeed and its run
and attempt, checkout, consumer matrix, dependency input evidence, runtime report, ZIP, digest and
schema-1 manifest must be reviewed. In that same run, verify the actual producer's initial and final
repository-state steps and `repository-state.json`: the exact-SHA checkout is detached, the initial
branch is empty, and the successful final comparison confirms the branch, HEAD, clean working tree
and local branch/tag refs remain unchanged. The JSON contains the initial snapshot; the final step
result supplies the comparison. If either step cannot be confirmed, this guarantee is unverified.
A publisher detached-checkout fixture is a different process and cannot substitute for this check.

The selected validation run itself must be completed successfully. If the artifact is missing,
expired, malformed, or the latest matching run is non-success, queued, cancelled, or otherwise not
usable, run `Release validation` again for the same SHA. Release does not fall back to an older
successful run, a published asset, or a new ZIP build.

The workflow has strict fresh and resume states. A fresh release requires no tag and no GitHub
Release for the confirmed version. It may create only an annotated tag at the validated SHA; it
does not write the package, create a commit, or push the release branch. A resume requires both an
exact annotated tag and a matching draft or published Release at the same SHA. A tag-only failure
is not automatically repaired and requires operator investigation. Draft resume is limited to
repairing the generated badge body and uploading or reusing one exact-digest missing asset;
mismatched or duplicate assets fail closed. Published resume is allowed only while the configured
release branch still points to the same SHA. It verifies the immutable Release and asset digest and
does not mutate the published Release body or assets, including a legacy or missing badge.

Before the first production publication, operators may run `Release` with `preflight_only=true`
for the same branch, SHA, and version. This hosted no-mutation check exercises token permissions,
exact checkout, latest-run/artifact verification, release-state resolution, and the remote branch
tip gate, then records evidence without creating a tag, Release, asset, or dispatch. The production
Release path performs no Unity setup or validation, package write, commit, rebuild, or release-
branch push. It rechecks the release branch before each remote mutation, verifies the asset digest
before publish, verifies the published and immutable state afterward, and sends the existing VPM
dispatch last.

Stable and prerelease versions are supported; prereleases are published as GitHub prereleases.
Prerelease visibility in a VPM client depends on that client's behavior; this package does not
promise that every VCC client hides or displays prereleases in the same way.

`Sync VPM yanks` runs on a `vpm-yanks.json` push to the literal `master` branch or by manual
dispatch from `master`. It uses the existing `release` environment and the same `APP_CLIENT_ID` and
`APP_PRIVATE_KEY` secrets as Release. The GitHub App token is restricted to the configured
`VPM_REPOSITORY` and Contents write permission. The workflow checks the policy with the strict
repository helper before creating any repository dispatch request, then sends only the fixed
`sync-vpm-yanks` event with `packageName`, `sourceRepository`, and `policyCommitSha`. It writes the
policy SHA, entry count, and target repository to the run summary, but never logs Yank reason
bodies. The policy is desired state: a version entry means Yank and an absent entry means Unyank.

Keep `vpm-yanks.json` empty until the VPM receiver is ready and the confirmed release version is
registered in the VPM feed. Once both are confirmed, that released version may be added for the
end-to-end Yank/Unyank test as a separate approved policy update and synchronization from the
release artifacts. An empty policy is a no-op desired state, and no version may be added before its
release exists in the target feed. Feed and receiver updates are eventually consistent; a stale or
premature dispatch fails closed without changing the listing, so retry from `master` with the
current policy commit after propagation. For stale state or recovery after a receiver outage,
correct the desired state on `master` and rerun the workflow manually from `master`. The VPM
receiver, VPM repository, and existing `update-vpm` payload contract are outside this release
pipeline documentation change and remain unchanged. The reason value is public operational
documentation, not a secret channel. Never put secrets, credentials, personal data, or other
private information in it. ALCOM prerelease and package-feed behavior is implementation-specific
and is not guaranteed for other VCC clients.

`Automation tests` runs Pester on GitHub-hosted Linux runners. The tests cover stable and prerelease
version validation, fresh and resume release mode decisions, missing and mismatched tags, VPM
dispatch URLs and hashes, fork and draft PR rejection, immutable-release preflight behavior, the
VPM yank policy reader and sender workflow contracts, and the generated Unity project version,
QualitySettings snapshot, VRChat SDK exclusion, test framework, and Shader-Core pin.

The existing Ubuntu Pester job collects `.github/tests`, plus the portable
`.github/scripts/Install-VerifiedShaderCoreRelease.Tests.ps1` and
`Tests/Release/ShaderCoreValidationInputs.Tests.ps1` explicitly. It does not recursively run all
of `Tests`. The Windows-only `Tests/Release/Run-PureBaseReleaseValidation.Tests.ps1` suite uses
a fake Unity `.cmd` and remains a separate local Windows Pester validation when relevant.

Hosted workflow contract tests use stable IDs or unique supported action/script selectors, and
check required inputs, permissions, action identity and pin format, and security-relevant order;
display names, argument order and unrelated step order are not the intended contract. The narrow
helpers reject ambiguous selection and are not general YAML parsers. The Hosted Release suite owns
the required upload paths and private exclusions. Existing runner and exporter checks own raw input
capture, cleanup, privacy and the publication payload; upload alone does not establish validation
success. Do not assume that every implementation-spelling assertion has been removed.

`InstallPinnedUnityCli.Tests.ps1` exercises platform and malformed input rejection, official
versioned URI and SHA-256 selection, hashing the downloaded temporary file, preservation of an
existing destination on failure, placement after verification and temporary cleanup. Hosted Unity
contracts retain static guards for terminating download errors and a nonzero `install-path` exit:
the existing mocks do not independently prove those negative cases. Watchdog launch-failure
diagnostics check selection of 1800 seconds for configure and 3600 seconds for `-runTests`; they do
not demonstrate a real-time timeout kill. The configure workflow step also has a 30-minute outer
timeout.

### リポジトリの改行検査

`.gitattributes` が改行ポリシーの正本です。追跡対象のテキストは LF とし、明示的に
`binary` または `-text` と分類されたパスは検査対象から除外します。チェッカーは既存の
Automation の Pester 自動検出（`.github/tests`）に含まれるため、この検査のための新しい
workflow や job はありません。

`.github/scripts/Test-RepositoryLineEndings.ps1` は index blob と working tree のバイト列を
それぞれ独立して検査し、CRLF、混在した改行、単独の CR (`0x0D`) を検出します。失敗時は
パスと理由だけを出力して終了コード 1 を返し、ファイル内容は出力しません。Git の異常や
入力不備も失敗として扱う fail-closed の読み取り専用チェッカーです。

パッケージルートで実行するローカル確認:

```powershell
$packageRoot = (Get-Location).Path
pwsh -File .github/scripts/Test-RepositoryLineEndings.ps1 -RepositoryRoot $packageRoot
```

Pester の対象を絞った確認（Pester が利用可能な PowerShell で、パッケージルートから実行）:

```powershell
Invoke-Pester -Path .github/tests/RepositoryLineEndings.Tests.ps1
```

違反が報告された場合は、報告された追跡対象テキストのパスだけを LF に修正してください。
ignored または生成物を正規化せず、バイナリとして扱うべきかは `.gitattributes` を確認します。

`CodeQL` runs on GitHub-hosted Linux runners for C# and GitHub Actions. HLSL and PowerShell are not
CodeQL languages and remain covered by the Unity, release validation, and Pester automation tests.
