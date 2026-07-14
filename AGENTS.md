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

Do not add optional `.scmodule` files to this repository without explicit approval.

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
