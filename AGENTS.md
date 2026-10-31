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

Defines project guidance for agents working on Pure-Base.
-->

# AGENTS.md

## Project

Pure-Base provides minimal base shaders for [Shader-Core](https://github.com/lilxyzw/Shader-Core).

The project is a shader host and foundation, not a feature-complete shader package.

Planned shader variants:

* `PureBase/Unlit`
* `PureBase/Toon`
* `PureBase/PBR`
* `PureBase/Hybrid`

Each shader must remain independently usable.

## Core principles

* Keep the project as small and maintainable as possible.
* Each base shader must render correctly without optional modules.
* Implement only the functionality required for the shader type to work.
* Expose the standard Shader-Core phases required by external modules.
* Share common implementation through HLSL includes instead of duplicating code.
* Prefer simple and predictable behavior over configurability.

## Scope

The base shaders may contain:

* Base texture and base color
* Basic UV handling
* Alpha and cutoff handling when required
* Normal mapping when required by the lighting model
* The minimum lighting implementation for each shader type
* Essential render states and shader passes
* Shader-Core data initialization and phase insertion points

## Out of scope

Do not add optional visual features such as:

* Rim light
* MatCap
* Decals
* Detail textures
* Additional emission effects
* Dissolve
* Distance fade
* Parallax
* Hair or anisotropic specular
* Clear coat
* Glitter
* Platform-specific integrations

These features belong in separate Shader-Core module packages.

Do not add optional product or release `.scmodule` files to this repository without explicit approval. Test-only `.scmodule` files used under the repository's test infrastructure may be added without separate approval, provided they are not included in the released package or otherwise exposed as product modules.

NonToon may be used as an implementation reference, but Pure-Base must not inherit its feature scope.

## Compatibility

* Preserve stable shader names and public property names.
* Do not change package identifiers or shader paths without explicit approval.
* Do not assume that a module is installed.
* Keep Shader-Core integration consistent with its current documented contracts.
* Do not add dependencies unless they are required for the base shaders to function.

The repository name is `Pure-Base`, but shader names use the `PureBase/` namespace.

## Changes

When modifying the project:

1. Keep changes focused on the requested task.
2. Avoid unrelated refactoring or feature additions.
3. Reuse shared code only when it keeps the individual shaders understandable.
4. Update documentation when public behavior, requirements, or shader names change.
5. Do not copy large implementations from other shaders without verifying that every part is necessary.

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
20 continue to apply.

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
Do not edit other functions or compress lines or remove blanks or comments
to create room. Review the complete diff and run the existing complete
runner Pester file. Do not alter the profile, workflow, cleanup, privacy,
or validation thresholds. Stop if any additional change is needed.
This is not a general grandfathering rule or a Codacy suppression, and it
does not authorize future evidence or profile changes.

## Persistent communication

Do not persist temporary orchestration labels in repository artifacts.

* Do not refer to work only as `Phase X`, `Step X`, `Stage X`, or similar plan-relative labels in code comments, commit messages, pull requests, documentation, changelogs, or issue descriptions.
* Describe the actual change, component, behavior, and reason in terms that remain understandable without access to the original implementation plan.
* Code comments must explain local behavior, constraints, or rationale rather than the order in which work was performed.
* Commit messages must summarize the concrete change instead of its position in an orchestration plan.
* When a numbered sequence is genuinely necessary, give every item a descriptive, self-contained name.

For example, use `Add the minimal BIRP forward pass` instead of `Implement Phase 2`.

This restriction applies to temporary development and orchestration phases. It does not prohibit Shader-Core phase names such as `base`, `light`, `shade`, or `postpixel`, which are part of the technical API.

## Validation

Before completing a change, verify that:

* Unity imports the package without project-caused errors.
* Every affected shader compiles.
* Every base shader works without optional modules.
* External Shader-Core phases remain available.
* No unintended shader variants, passes, dependencies, or features were added.

### Release validation

Do not run `Tests/Release/Run-PureBaseReleaseValidation.ps1` locally during
normal LLM-assisted development.

Release validation performs extensive read and write operations and must
normally be delegated to the repository's GitHub Actions
`Release validation` workflow.

Before requesting release validation:

1. Complete the relevant local Daily validation.
2. Ensure every input required by release validation is tracked by Git.
3. Commit and push the current changes to the working branch.
4. Trigger `.github/workflows/release-validation.yml` for that exact branch.
5. Wait for the workflow to complete and inspect its uploaded validation
   evidence when it fails.

Do not assume GitHub Actions can observe uncommitted or unpushed local files.
Do not commit generated ConsumerProject, Library, release ZIP, logs, or other
temporary validation artifacts unless the task explicitly requires a tracked
fixture or baseline update.

Run release validation locally only when the user explicitly requests local
CI-parity investigation or when debugging the release-validation runner itself.

### Daily validation from an open Unity Editor

When Unity MCP is connected to the project and the Editor is already open, prefer its
asynchronous test tools for LLM-driven local validation instead of starting a second Unity
process:

* wait for compilation and asset import to finish;
* run only the `PureBase.Tests.Daily` EditMode assembly;
* poll the returned test job until completion and report failed-test details;
* do not modify tracked package files or Shader-Core ProjectSettings while the job runs; and
* use `Tests/Run-PureBaseRegression.ps1 -Mode Daily` for CI/CD or isolated batch validation.

## License headers

Add the Apache License 2.0 notice to the beginning of every repository-owned text file that supports comments.

Use the comment syntax appropriate for the file type while preserving the following text:

```text
Copyright [yyyy] [name of copyright owner]

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
```

### Year policy

* Use the year in which the file was first created.
* Do not update the copyright year merely because the file was modified.
* Preserve the existing year or year range when editing a file that already has a license header.
* For files first created in 2026, use `Copyright 2026 [name of copyright owner]`.

### Placement

* The license notice must appear before imports, includes, declarations, documentation headings, or executable code.
* A required interpreter directive such as a Unix shebang may remain on the first line; place the license notice immediately after it.
* Preserve mandatory format-specific declarations when placing a comment before them would make the file invalid.

### Comment syntax

Use the native comment syntax of the file.

For C#, HLSL, ShaderLab, JavaScript, and similar files:

```text
/*
 * Copyright [yyyy] [name of copyright owner]
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
```

For Markdown files, use an HTML comment so the notice is not rendered:

```html
<!--
Copyright [yyyy] [name of copyright owner]

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
```

For shell scripts, YAML, TOML, Python, and other line-comment formats, prefix each line with the appropriate comment marker.

### Exclusions

Do not insert a license header into:

* `LICENSE` itself
* Binary files, images, archives, or other non-text assets
* Files whose format does not support comments, including strict JSON
* Unity-generated `.meta` files
* Generated code or generated artifacts
* Lock files
* Third-party or vendored files
* Files copied from another project that retain a different valid license notice

For files that cannot contain comments, keep the repository-level `LICENSE` file and applicable package metadata such as the `license` field in `package.json`.

Do not alter a third-party copyright or license notice without explicit approval.
