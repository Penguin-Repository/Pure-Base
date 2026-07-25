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

Daily and release validation run on GitHub-hosted `windows-latest` runners. Each run installs Unity
2022.3.22f1 through `yamachu/unity-cli-actions`, activates Unity Personal through
`buildalon/activate-unity-license`, and exports the discovered Editor path to the existing validation
scripts. The Unity Editor installation is cached by operating system, architecture, and Unity version
to avoid repeating the full Editor installation on every run.

Create these Actions secrets for Unity Personal activation:

- `UNITY_EMAIL`: email address of the Unity ID used for CI.
- `UNITY_PASSWORD`: password of the Unity ID used for CI.

Use a real human Unity ID. Service accounts do not have a Unity Personal entitlement. Personal
activation does not use a serial number. The activation Action uses Unity Licensing Client and its
post-job handler releases the ephemeral runner's entitlement automatically. Third-party Actions are
pinned to audited commits rather than moving tags.

The repository is public, and Unity validation executes repository code. The Daily workflow rejects
fork pull requests and runs pull request code only when the head branch belongs to this repository.

The workflows construct a temporary Unity project with Pure-Base and Shader-Core checked out as
embedded packages. Shader-Core is pinned to the exact reviewed tag `0.1.9`, matching the exact
dependency identity required by Pure Base for both Daily and release validation. Git line-ending
conversion is disabled, and the project is configured for Linear color space before Daily
validation.
Pure Base does not automatically accept future `0.1.x` versions because Shader-Core has no declared
0.x compatibility guarantee, and its importer, ProjectSettings, and method-shape contracts are
compatibility-sensitive. Each future version requires explicit review and an updated pin.

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
uses the tested `Resolve-PureBaseDailySource` helper from the base workflow checkout. Fork pull
requests are rejected before Unity is installed and without checking out or executing their code on
the Windows runner.

`Release validation` is manual and read-only. It runs the full release consumer validation and
uploads the complete evidence directory, including a versioned copy of the audited package ZIP.

`Release` is manual and requires the exact version currently stored in `update_trigger.json`.
For a new release, that version must be newer than `package.json`. The workflow validates the
pre-update package, updates and commits `package.json`, pushes the version tag, builds a new audited
ZIP from the updated commit, creates a draft release, uploads the ZIP, publishes the release, and
sends `update-vpm` to the VPM repository using a GitHub App installation token.

If a run fails after the package version commit, rerun the workflow with the same version and
`resume` enabled. Resume mode reruns release validation, verifies that HEAD contains the selected
package version, creates a missing tag when necessary, resumes an existing draft, accepts an
already published release only when its asset matches, and retries the VPM dispatch. Creating and
filling a draft before publication minimizes the immutable release failure window.

`Automation tests` runs Pester on GitHub-hosted Linux runners. The tests cover stable version
validation, fresh and resume release mode decisions, missing and mismatched tags, VPM dispatch URLs
and hashes, fork and draft PR rejection, immutable-release preflight behavior, and the generated
Unity project version, Linear color space, text serialization, and Shader-Core pin.

`CodeQL` runs on GitHub-hosted Linux runners for C# and GitHub Actions. HLSL and PowerShell are not
CodeQL languages and remain covered by the Unity, release validation, and Pester automation tests.
