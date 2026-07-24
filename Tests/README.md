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

Daily requires the Unity Editor to be closed. The runner rejects an open project, captures its NUnit and Unity log artifacts outside the package Git root, and verifies the protected state before and after the run.

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
> Since release validation involves extensive read and write operations, it is recommended to perform it in a CI/CD environment rather than locally.

`Tests/Release/Run-PureBaseReleaseValidation.ps1` is the release consumer lane. It requires `-UnityEditorPath` and accepts an external `-ArtifactDirectory`; `-KeepConsumer` retains the consumer directory for inspection.

The runner builds the audited release ZIP and validates it in one disposable external `ConsumerProject` directory. Cold resets remove only that consumer directory's `Library`, while the runner verifies the remaining immutable consumer inputs. Unless `-KeepConsumer` is specified, the consumer directory is removed after validation.

The release ZIP excludes `Tests/**` and test-only `*.scmodule` files. Tracked `.scmodule` files are allowed only within the package-owned `Tests/**` fixture boundary.
