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

Daily and release validation run on GitHub-hosted `windows-2022` runners. Each run installs Unity
2022.3.22f1 through `yamachu/unity-cli-actions`, activates Unity Personal through
`buildalon/activate-unity-license`, and exports the discovered Editor path to the existing validation
scripts. The Unity Editor installation is cached by operating system, architecture, and Unity version
to avoid repeating the full Editor installation on every run. The cache preparation job uses
`.github/actions/lookup-unity-editor-cache` for a metadata-only lookup. On a true hit, it does not
download, extract, or install an Editor archive. The dependent execution job performs the one real
`setup-unity-cli` restore and exposes the existing Editor path to validation.

On a non-true lookup result, including a cache-service warning normalized by the pinned upstream
fallback, the preparation job conditionally runs the pinned `yamachu/unity-cli-actions` restore
action. That fallback restores the Editor if another run saved the cache while the lookup was in
progress, or installs it and saves it when the independent preparation job completes. Cache misses
are therefore non-fatal fallback behavior. Malformed cache identity inputs still fail normally; the
helper does not use step-level `continue-on-error`.

The exact cache key is `unity-editor-${{ runner.os }}-${{ runner.arch }}-${{ inputs.unity-version }}`
for Unity `2022.3.22f1`, with no branch, commit, pull request, run, or restore-key component and no
cross-OS archive. GitHub cache scope can therefore let branches and pull requests reuse the
default-branch cache when the exact key and cache version match. The lookup helper supports only
Windows X64. It downloads the versioned `unity-windows-x64.exe` artifact directly from Unity's
CLI CDN, verifies its SHA-256, and only then places it as `unity.exe`. The expected checksum must
come from Unity's official version-specific manifest. Accepted CLI versions match
`^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:\.[0-9A-Za-z]+)*)?$`.

The helper fails closed for an unsupported platform or architecture, a malformed version or hash,
a download error, or a checksum mismatch. An existing CLI remains untouched until verification
succeeds. The Daily and Release validation workflows pin Unity CLI `1.0.0-beta.3` and provide its
version and checksum to the reusable helper. Updates to the version and checksum must be made
atomically at both workflow call sites and require hosted verification.

Do not create tags or branches, or use synthetic identities, to force a production-key miss: the
default-branch fallback for the exact key and cache version means this cannot guarantee a miss. Treat
static Pester fallback contracts as deterministic evidence; preserve naturally occurring miss logs
only as supplemental evidence.

The preparation checkout remains constrained to Daily's exact authorized ref or Release
validation's dispatched commit SHA. Both workflows retain read-only contents permission and
`persist-credentials: false`; the cache contains only the discovered Editor install root and no
Unity activation secrets.

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

The workflows construct a temporary Unity project with Pure-Base and Shader-Core checked out as
embedded packages. Shader-Core is pinned to the exact reviewed tag `0.1.9`, matching the exact
dependency identity required by Pure Base for both Daily and release validation. Git line-ending
conversion is disabled, and the project is configured for Linear color space before Daily
validation.
Pure Base does not automatically accept future `0.1.x` versions because Shader-Core has no declared
0.x compatibility guarantee, and its importer, ProjectSettings, and method-shape contracts are
compatibility-sensitive. Each future version requires explicit review and an updated pin.

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
Grant repository **Contents: write** on both installations and **Administration: read** on
Pure-Base. The release workflow creates separate installation tokens for Pure-Base and the VPM
repository, with each token restricted to its target repository and requested permissions.

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

`Release validation` is manual and read-only. Configure/import runs through the Unity watchdog
proxy. The audited release-validation runner receives the real `Unity.exe`, because it intentionally
rejects wrapper executables while proving the required Unity version from the editor path. The
workflow uploads the complete evidence directory, including a versioned copy of the audited package
ZIP.

`Release` is manual and requires the exact version currently stored in `update_trigger.json`.
`package.json` is the package manifest and version source after the workflow writes the requested
SemVer. For a new release, that version must be newer than `package.json`. Stable and prerelease
versions are supported; prereleases are published as GitHub prereleases. Prerelease visibility in a
VPM client depends on that client's behavior; this package does not promise that every VCC client
hides or displays prereleases in the same way. The workflow validates the pre-update package,
updates and commits `package.json`, pushes the version tag, builds a new audited ZIP from the
updated commit, creates a draft release, uploads the ZIP, publishes the release, and sends
`update-vpm` to the VPM repository using a GitHub App installation token.

If a run fails after the package version commit, rerun the workflow with the same version and
`resume` enabled. Resume is fail-closed: `update_trigger.json` and `package.json` must match exactly,
and an existing annotated release tag must point to HEAD. A missing tag or an advanced HEAD
requires operator investigation rather than auto-recovery. Resume mode reruns release validation,
resumes an existing draft, accepts an already published release only when its asset matches, and
retries the VPM dispatch. Creating and filling a draft before publication minimizes the immutable
release failure window.

`Sync VPM yanks` runs on a `vpm-yanks.json` push to the literal `master` branch or by manual
dispatch from `master`. It uses the existing `release` environment and the same `APP_CLIENT_ID` and
`APP_PRIVATE_KEY` secrets as Release. The GitHub App token is restricted to the configured
`VPM_REPOSITORY` and Contents write permission. The workflow checks the policy with the strict
repository helper before creating any repository dispatch request, then sends only the fixed
`sync-vpm-yanks` event with `packageName`, `sourceRepository`, and `policyCommitSha`. It writes the
policy SHA, entry count, and target repository to the run summary, but never logs Yank reason
bodies. The policy is desired state: a version entry means Yank and an absent entry means Unyank.

Keep `vpm-yanks.json` empty until the VPM receiver is ready and the target `0.1.0-beta.1` release is
registered in the VPM feed. Once both are confirmed, the first prerelease, `0.1.0-beta.1`, may be
added for the end-to-end Yank/Unyank test as a separate approved policy update and synchronization
from the release artifacts. An empty policy is a no-op desired state, and no version may be added
before its release exists in the target feed. Feed and receiver updates are eventually consistent;
a stale or premature dispatch fails closed without changing the listing, so retry from `master` with
the current policy commit after propagation. For stale state or recovery after a receiver outage,
correct the desired state on `master` and rerun the workflow manually from `master`. The reason
value is public operational documentation, not a secret channel. Never put secrets, credentials,
personal data, or other private information in it. ALCOM prerelease and package-feed behavior is
implementation-specific and is not guaranteed for other VCC clients.

`Automation tests` runs Pester on GitHub-hosted Linux runners. The tests cover stable and prerelease
version validation, fresh and resume release mode decisions, missing and mismatched tags, VPM
dispatch URLs and hashes, fork and draft PR rejection, immutable-release preflight behavior, the
VPM yank policy reader and sender workflow contracts, and the generated Unity project version,
QualitySettings snapshot, VRChat SDK exclusion, test framework, and Shader-Core pin.

`CodeQL` runs on GitHub-hosted Linux runners for C# and GitHub Actions. HLSL and PowerShell are not
CodeQL languages and remain covered by the Unity, release validation, and Pester automation tests.
