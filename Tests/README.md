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

# The package-owned `Tests/` directory provides deterministic Pure-Base regression checks, fixed Shader-Core test hosts, and explicit tooling for observing and regenerating the canonical validation baseline

This directory is the source of truth for persistent Pure-Base validation: test assemblies, fixtures, baselines, test-only modules, and validation runners are maintained here.

## Operating lanes

These lanes have different write boundaries. Run the commands below from the package root, `Packages/jp.penguin.purebase`.

### Initialize

`Initialize` is an explicit setup lane. It configures the fixed Shader-Core test hosts from the package manifest and applies their serialized module selections. It is not part of normal Daily validation or baseline regeneration.

```powershell
.\Tests\Run-PureBaseRegression.ps1 -Mode Initialize
```

This lane is write-capable because it updates the Shader-Core ProjectSettings state and reimports the configured host assets when the state changes. The four product shaders remain separate from the test hosts and must have empty module selections.

### Daily

`Daily` is the normal read-only lane driven by `Tests/Run-PureBaseRegression.ps1`. It runs the current Unity project once and executes only the `PureBase.Tests.Daily` EditMode test assembly.

```powershell
.\Tests\Run-PureBaseRegression.ps1 -Mode Daily
```

For local LLM-assisted development, the same Daily assembly may instead be run through
[Unity MCP](https://github.com/CoplayDev/unity-mcp) while the Editor remains open:

1. Wait until Unity is neither compiling nor importing assets.
2. Call `run_tests` with `mode: EditMode` and
   `assembly_names: PureBase.Tests.Daily`.
3. Poll the returned job with `get_test_job` until it reaches `succeeded` or `failed`.
   A `wait_timeout` of 30 to 60 seconds is recommended.
4. Do not edit tracked package files or Shader-Core ProjectSettings while the job is
   running. The Daily assembly verifies their before/after hashes and fails if they change.

Example tool arguments:

```json
{
  "mode": "EditMode",
  "assembly_names": "PureBase.Tests.Daily",
  "include_failed_tests": true,
  "include_details": false
}
```

The Unity MCP lane is intended for interactive local validation by an LLM. It does not
replace the PowerShell runner: CI/CD and other isolated validation must continue to use
`Run-PureBaseRegression.ps1 -Mode Daily`, which additionally owns external NUnit and log
artifacts and independently verifies the same protected state around the Unity process.

The PowerShell Daily runner requires the Unity Editor to be closed. It rejects an open project, captures its NUnit and Unity log artifacts outside the package Git root, and verifies the protected state before and after the run.

Daily does not:

- bake or regenerate fixtures;
- save package fixtures or the canonical baseline;
- mutate Shader-Core ProjectSettings; or
- mutate the product shader module selections.

Daily GREEN numeric assertions cover the committed fixture and baseline in the approved environment. Dynamic lightmaps are recorded as metadata with the status `NOT_DETERMINISTIC_IN_BATCH_EDITMODE`; they are excluded from Daily GREEN numeric assertions.

## Environment and baseline

The validation fixture and canonical baseline require all of the following:

- Unity `2022.3.22f1`;
- the Built-in Render Pipeline (BIRP);
- D3D11; and
- Linear color space.

The canonical numeric baseline is:

`Tests/Baselines/birp-d3d11-2022.3.22f1.json`

Daily reads this baseline. Daily never creates or replaces it.

## Rendering-mode coverage

The rendering-mode contract covered by the package validation inputs is:

| Mode | Covered behavior |
| --- | --- |
| Opaque | Uncut and unblended rendering, queue `2000`, `One Zero`, and `ZWrite 1`; lighting contributions enabled. |
| Cutout | Coverage clipping, the default keyword-free state, queue override `-1` resolving to `AlphaTest 2450`, and lighting contributions enabled. |
| Transparent | Alpha blending with base `SrcAlpha OneMinusSrcAlpha` and additional-light `SrcAlpha One`, queue `3000`, `ZWrite 0`, and disabled `ShadowCaster`/`Meta`. |

The coverage checks also verify that the final alpha from `postpixel` controls the `ForwardBase` and `ForwardAdd` source alpha. All source shaders retain four pass declarations. Editor migration is explicit: Inspector opening or refresh does not migrate or dirty legacy materials, while mode changes and `Assets/PureBase/Resync Rendering Mode` synchronize derived state.

## Observation, apply, and regeneration

Observation, reviewed apply, and regeneration are explicit write-capable operations separate from the normal Daily lane.

- **Observation** captures one read-only scene observation into an external candidate artifact. The candidate path is required to be absolute and outside both the package and the Unity `Assets` import scope. Observation does not write the canonical baseline.
- **Reviewed apply** consumes a schema-validated, independently reviewed candidate and writes only the candidate's approved exact baseline to `Tests/Baselines/birp-d3d11-2022.3.22f1.json`. The write is performed through the audited transaction boundary; it does not bake, recapture, or widen ranges.
- **Regeneration** is an explicit fixture-bake and baseline-write operation. It validates Unity `2022.3.22f1`, BIRP, D3D11, and Linear color space before writing, and audits the transaction so unrelated durable project or package changes are rejected.

The generator exposes these operations through explicit editor or batch entry points. No operation automatically widens numeric ranges. An observation produces exact captured values for review; changing the canonical baseline requires an explicit reviewed operation.

## Shader-Core module boundary

The following product shaders must remain module-free for Daily:

- `PureBase/Unlit`
- `PureBase/Toon`
- `PureBase/PBR`
- `PureBase/Hybrid`

Fixed test-host module selections belong to the Initialize lane. They must not leak into the product shader rows used by Daily.

## Release validation

> [!Important]
> Do not run `Tests/Release/Run-PureBaseReleaseValidation.ps1` locally during normal
> LLM-assisted development. Release validation is the hosted producer lane; the production
> Release workflow consumes its exact-SHA artifact.

`Tests/Release/Run-PureBaseReleaseValidation.ps1` is the release consumer lane. It requires `-UnityEditorPath` and accepts an external `-ArtifactDirectory`; `-KeepConsumer` retains the consumer directory for inspection.

The hosted `Release validation` workflow checks out the release-preparation commit at its exact
SHA, runs this consumer lane, and verifies that the package checkout and refs remain unchanged. It
then exports one validation artifact with this layout:

```text
pure-base-release-validation-<run-id>-<run-attempt>/
   validated-package/
      jp.penguin.purebase-<version>.zip
      jp.penguin.purebase-<version>.zip.sha256
      release-validation.json
```

The ZIP is deterministic and uses Store mode. The sidecar contains one lowercase SHA-256 line.
The schema-1 manifest binds the ZIP to the repository, exact head SHA, head branch, workflow run
ID and attempt, package version, asset name, and SHA-256. The producer creates and uploads this
evidence only after the audited ZIP and all provenance checks succeed.

The `Release` workflow is the consumer. It must be dispatched from the same release branch and
exact SHA after validation. It selects the latest matching `release-validation.yml` run and attempt,
requires that run to be completed successfully, requires one unexpired artifact with matching
provenance, and verifies the downloaded ZIP against both the manifest and sidecar. Artifact expiry,
latest-run failure, or any provenance/digest mismatch requires a new hosted validation run for the
same SHA; an older successful run and a Release-side rebuild are not fallback paths. The published
asset is the downloaded validated ZIP itself.

Release does not run Unity validation, rebuild the ZIP, write or commit package files, or push the
release branch. It rechecks the release branch before remote mutations and performs digest,
published-state, and immutable-release verification before the existing VPM dispatch, which is
last. A published resume leaves a legacy or missing badge body unchanged. `preflight_only=true` provides an optional hosted no-mutation check before first production
publication. The VPM receiver, repository, and existing dispatch payload contract remain outside
this release validation boundary.

The runner builds the audited release ZIP and validates it in one disposable external `ConsumerProject` directory. Cold resets remove only that consumer directory's `Library`, while the runner verifies the remaining immutable consumer inputs. Unless `-KeepConsumer` is specified, the consumer directory is removed after validation.

The release ZIP excludes `Tests/**` and test-only `*.scmodule` files. Tracked `.scmodule` files are allowed only within the package-owned `Tests/**` fixture boundary.
