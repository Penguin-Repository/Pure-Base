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

Defines the static-analysis and bounded size rules continued from the package AGENTS.md.
-->

# Size policy

## Static-analysis policy

* Keep each source file under 500 lines. Codacy reports files with 500 or more lines as `medium` and files with 1000 or more lines as `critical`; split files before reaching the first threshold.
* Keep each function under 50 lines. Codacy reports functions with 50 or more lines as `medium` and functions with 100 or more lines as `critical`; extract focused helpers before reaching the first threshold.
* Keep each function's cyclomatic complexity below 20. Codacy reports cyclomatic complexity of 20 or more as `critical`; simplify control flow or split responsibilities before reaching that threshold.
* Do not add `IEquatable<T>`, `Equals`, or `GetHashCode` to private value types solely to silence static analysis.
* Add value equality only when it is an intentional contract or the type is used by comparison or hash-based APIs.
* When an analyzer finding is not applicable, record a concrete rationale showing that the type has no equality or hash-based use instead of disabling the rule globally.
* Unity Roslyn Analyzers, PSScriptAnalyzer, Jackson Linter, Agentlinter, SonarC#, and markdownlint are not supported by local Codacy CLI analysis; check them through Codacy Cloud, and do not treat their absence from local output as validation.

### Bounded bootstrap package profile update

For the single static bootstrap package profile update, synchronization of
the existing public-version-mismatch test mutation, and synchronization of
the one existing workflow upload expectation described below, accept only
the existing file-length and profile-function-length excesses relative to
baseline commit `8b0f7155241f69dba89635eb96325ead9329b9bb`, plus the single
test-file line permitted below:

* `Tests/Release/Run-PureBaseReleaseValidation.ps1`: 3031 physical lines.
* `Tests/Release/Run-PureBaseReleaseValidation.Tests.ps1`: 1444 physical lines.
* `Get-FirstBootstrapPackageProfile`: 130 lines.

The runner must not exceed 3031 physical lines, and
`Get-FirstBootstrapPackageProfile` must not exceed 130 lines. The test file
may exceed its 1444-line baseline by at most one physical line, solely for
the upload expectation specified below, and must not exceed 1445 physical
lines. This exception does not apply to `Assert-EditorClosed`, which is 40
lines at the baseline, or to any other function. All other function-length
requirements and the requirement that cyclomatic complexity remain below
20 continue to apply. If these conditions cannot be met, stop and seek a
separate explicit decision rather than expanding this exception.

The permitted profile changes are limited to:

* Change the Purchasing direct and lock versions from `4.9.3` to `4.11.0`.
* Remove the Purchasing dependency edge to `unityanalytics` version `1.0.0`,
  while retaining the `unityanalytics` module itself.
* Change the Purchasing requirement for `services.core` from `1.8.1` to `1.8.2`.
* Change the Core lock version from `1.12.4` to `1.12.5`.

Synchronize the existing public-version-mismatch test mutation by replacing
its previous `Replace('4.9.3', '4.11.0')` literal direction with
`Replace('4.11.0', '4.9.3')`. Preserve the existing rejection behavior,
original-byte preservation, validity detection, and Pester assertions, and
retain the independent full-field comparison against the original JSON.
No new assertion lines are required.

In the existing `Release validation workflow evidence export contracts`
test, append only `($artifactRoot + '\runtime-prerequisites\report.json')`
to `$expectedArtifactPaths`, immediately after the existing `*.log` entry,
and add the required comma to that preceding entry. This permits one new
physical array-entry line, not a new assertion or test case. Preserve the
ordered full-array `Should -Be` assertion and every other export and upload
assertion. Do not change the workflow, runtime prerequisite producer, or
any other expected path. This allowance applies only to this one-time
synchronization with the existing schemaVersion 2 report upload contract;
it does not authorize any future upload-path change.

Do not make unrelated changes, introduce helpers or paths, move suites, or
change unrelated control flow, capture, privacy, cleanup, or validator
behavior. Do not compress lines or remove blank or comment lines to create
room. Avoid unnecessary label or explanatory changes in the two files.
Confirm the update is data-only against the complete diff.

This exception is limited to the update specified above. It does not suppress
Codacy findings, establish a general grandfathering rule, or authorize future
profile exceptions. Adoption of the full dependency graph requires separate
explicit approval. If additional changes are necessary to satisfy these
conditions, stop and seek a separate decision rather than expanding this
exception.

### Bounded per-invocation evidence separation

For the single correction that gives the rendering-mode validation row its
own evidence directory, use commit
`f0777009b2b53887686afad1d12c818ed721e967` as the baseline. Only for this
correction, the file-length limits below replace the limits of the preceding
bootstrap-profile exception; that exception's edit scope is not extended.

* `Tests/Release/Run-PureBaseReleaseValidation.ps1`: baseline 3031 physical
    lines; permit at most two added physical lines, with a maximum of 3033.
* `Tests/Release/Run-PureBaseReleaseValidation.Tests.ps1`: baseline and
    maximum 1445 physical lines; no added physical lines.
* `New-InitialValidationMatrix`: baseline 11 lines, maximum 13 lines,
    including the function declaration and closing brace.

In `New-InitialValidationMatrix`, create a separate module-free contract for
the `rendering-mode-contract` row and set only that contract's `runLabel` to
`rendering-mode-contract`. Use two separate assignment lines and reference
that contract in the existing row. Preserve the first row's contract, all
filters, ordering, selections, reset flags, and every other contract field.
Do not change `New-ModuleFreeContract` or the evidence-writing functions.

In the existing initial-matrix Pester assertion, change only the rendering
row's expected `runLabel` and its explanatory message. Preserve the first
row's expectation, all other conditions, assertions, cases, and harness code.
Do not add assertion lines, helpers, source files, suites, or runtime guards.

This accepts only the specified existing file-length excesses and the two
assignment lines. It grants no function-length or complexity exception:
modified functions must remain under 50 lines and complexity below 20.
If these conditions cannot be met, stop and seek a separate explicit decision.
Do not edit other functions or compress lines or remove blanks or comments
to create room. Review the complete diff and run the existing complete
runner Pester file. Do not alter the profile, workflow, cleanup, privacy,
or validation thresholds. Stop if any additional change is needed.
This is not a general grandfathering rule or a Codacy suppression, and it
does not authorize future evidence or profile changes.

### Bounded CI simplification size exception

For the CI simplification authorized by plan v1.1 and its execution brief,
use baseline commit
`a46257acf59f94f0ff63cb47e91da82af2c5b3e4`. This is a separate, narrow
allowance for the specified PowerShell input/schema/identity/profile
migration, stable artifact naming, verified CLI setup, and meaning-based
contract-test changes. It applies independently of the historical bootstrap
profile and rendering-mode evidence exceptions above; those exceptions remain
limited to their original changes and neither prohibits nor expands this
allowance.

The exact baseline physical line counts for the enumerated files are:

| File | Baseline physical lines |
| --- | ---: |
| `.github/scripts/Install-VerifiedShaderCoreRelease.ps1` | 267 |
| `.github/actions/lookup-unity-editor-cache/Install-PinnedUnityCli.ps1` | 77 |
| `.github/tests/HostedUnityReviewContracts.Tests.ps1` | 461 |
| `.github/tests/HostedUnityReviewContracts.Helpers.ps1` | 230 |
| `.github/tests/HostedReleaseReviewContracts.Tests.ps1` | 291 |
| `Tests/Release/Build-PureBaseRelease.ps1` | 590 |
| `Tests/Release/Build-PureBaseRelease.Tests.ps1` | 369 |
| `Tests/Release/Run-PureBaseReleaseValidation.ps1` | 3033 |
| `Tests/Release/Run-PureBaseReleaseValidation.Tests.ps1` | 1445 |
| `Tests/Release/shader-core-0.3.0.sha256.json` | 802 |

Only the existing overages in this table are excepted. Files already at or
above 500 physical lines must not exceed their listed baseline counts; files
below 500 remain subject to the usual under-500 limit. Within these exact
files, any function that was already at or above 50 lines at baseline may
retain that existing excess, but must not exceed its own baseline length.
If an additional file or function would need an exception, stop and obtain a
separate explicit decision. The following are confirmed examples, not an
exhaustive allowlist:

| File | Existing function | Baseline lines |
| --- | --- | ---: |
| `.github/scripts/Install-VerifiedShaderCoreRelease.ps1` | `Install-VerifiedShaderCoreRelease` | 145 |
| `.github/actions/lookup-unity-editor-cache/Install-PinnedUnityCli.ps1` | `Install-PinnedUnityCli` | 62 |
| `Tests/Release/Build-PureBaseRelease.ps1` | `Get-RecursiveIdentityManifest` | 63 |
| `Tests/Release/Build-PureBaseRelease.ps1` | `Assert-ShaderCoreIdentity` | 68 |
| `Tests/Release/Run-PureBaseReleaseValidation.ps1` | `Get-FirstBootstrapPackageProfile` | 130 |

This allowance applies only to pre-existing excesses in the exact files
listed above; it does not permit an exception for any other file or a future
baseline. Existing functions below 50 lines, new functions, and new files
remain subject to the usual limits: files under 500 physical lines, functions
under 50 lines, and cyclomatic complexity below 20. No unrelated refactor is
required solely to remove an existing permitted excess. Do not compress code
or remove blank or comment lines to make room.
The only moved content allowed to retain an existing file-length excess is
the unchanged identity JSON moved from
`Tests/Release/shader-core-0.3.0.sha256.json` to
`Tests/Release/shader-core.sha256.json`; its content must remain unchanged.

This exception authorizes no other files, future baseline changes, or
general grandfathering, and does not suppress analyzer findings. If an
additional file or function would need an exception, stop and obtain a
separate decision.

### Bounded Daily authorization assertion repair

For the single repair of the existing authorization assertion in
`.github/tests/PureBase.Automation.Tests.ps1`, use baseline commit
`c81b96bf47e378550acc9a824e96fb83e1c0b1c4`. The file has 2161 physical lines
at that baseline. This separate exception permits that existing file-length
overage and only the minimum additional physical assertion lines necessary
for this repair; it does not grant a general file-length allowance.

Limit the repair to the existing test
`uses the tested authorization function before allocating the Unity runner`.
Replace its obsolete preparation-job adjacency expectation with assertions
that the actual `unity-daily` job depends on `authorize`, has the allowed
job-level gate, and checks out the authorized exact ref. Preserve the existing
authorization and checkout guarantees; do not weaken, skip, or remove them.
Do not edit unrelated tests, functions, or sections, or add helpers, suites,
frameworks, or unrelated assertion lines. Existing file/function/complexity
rules continue to apply to new or changed functions; unchanged functions are
not a reason to expand this repair. Do not compress lines, remove blanks or
comments, or broadly split the file to make room.

This allowance is specific to the stated assertion repair against the stated
baseline. It does not authorize runtime fixture or C# changes, workflow
changes, additional CI runs or external writes, suppress Codacy findings, or
establish future exceptions. If the repair needs more than the minimum
assertion-line increase or any out-of-scope change, stop and request a
separate decision.

### Bounded rendering-mode source-contract timeout allowance

For the single finite-timeout update to
`Tests/Daily/Editor/PureBaseRenderingModeRenderingTests.SourceContracts.cs`,
use baseline commit `7580077aaab7ae6d54ac17bbb127ee8afe9d56bb`. At that
baseline, the file is 1143 physical lines and 55782 bytes. The existing method
`BirpHostPreservesModeAlphaFogPostPixelAndForwardAddSourceOrder` spans 71
physical lines, from its declaration on line 114 through its closing brace on
line 184. Count neither attributes nor XML documentation as method lines, and
do not count a trailing newline as an extra physical line.

Only this file and method may retain their existing size excess for this
change. Permit at most 24 added physical lines in the file and method, with
maximum sizes of 1167 file lines and 95 method lines. The added lines are
limited to readable expansion of the five finite-timeout regex evaluations,
a method-local `TimeSpan.FromSeconds(2)` value, and a concise rationale
comment. The five evaluations are the mode-alpha `Regex.Match`, fog
`Regex.Match`, fog `Regex.Matches(...).Count`, helper alpha-one
`Regex.IsMatch`, and generated-Toon mode-alpha `Regex.IsMatch`. This maximum
is a limit, not a target.

Pass `RegexOptions.None` and the finite timeout through the existing
compatible overloads. Preserve every pattern, input, assertion, evaluation
order, and `Count` meaning. `Regex.Escape` is not an evaluation and is not
covered. The acceptance condition is that timeout exceptions from all five
evaluations reach NUnit as failures, including during `Matches` enumeration
for `Count`; do not catch, retry, or ignore them. Do not use an
infinite timeout, change global defaults, add helpers, types, files, branches,
or complexity exceptions, or edit another method. Do not remove blank or
comment lines, compress code, or split unrelated code to create room.

This is a one-change allowance only. It does not suppress Codacy findings,
authorize a future baseline or general size exception, or extend any existing
bootstrap, run-label, CI-simplification, or authorization exception. If the
baseline, target, or required increase differs from these limits, stop and
request a separate decision.
