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

# Pure Base

Language: [日本語](README.ja.md)

![GitHub Total Downloads](https://img.shields.io/github/downloads/Penguin-Repository/Pure-Base/total?label=GitHub%20Release%20downloads)
![Downloads latest](https://img.shields.io/github/downloads/Penguin-Repository/Pure-Base/latest/total)

[![Automation tests](https://github.com/Penguin-Repository/Pure-Base/actions/workflows/automation-tests.yml/badge.svg)](https://github.com/Penguin-Repository/Pure-Base/actions/workflows/automation-tests.yml)
[![CodeQL](https://github.com/Penguin-Repository/Pure-Base/actions/workflows/codeql.yml/badge.svg)](https://github.com/Penguin-Repository/Pure-Base/actions/workflows/codeql.yml)
[![Daily](https://github.com/Penguin-Repository/Pure-Base/actions/workflows/daily.yml/badge.svg)](https://github.com/Penguin-Repository/Pure-Base/actions/workflows/daily.yml)

[![Release validation](https://github.com/Penguin-Repository/Pure-Base/actions/workflows/release-validation.yml/badge.svg)](https://github.com/Penguin-Repository/Pure-Base/actions/workflows/release-validation.yml)
[![Release](https://github.com/Penguin-Repository/Pure-Base/actions/workflows/release.yml/badge.svg)](https://github.com/Penguin-Repository/Pure-Base/actions/workflows/release.yml)

Pure Base is a Unity package that provides four base shaders for Shader-Core.

Instead of including a large collection of optional effects, it provides a small and understandable foundation that can be extended with Shader-Core modules when needed.

> [!IMPORTANT]
> Pure Base is an unofficial project developed independently from Shader-Core, NonToon, and lilToon.
>
> Generative AI is used during development.

## What is included

| Shader | Intended use |
| --- | --- |
| `PureBase/Unlit` | A display that is not affected by scene lighting |
| `PureBase/Toon` | Anime-style lighting with clearly separated light and shadow |
| `PureBase/PBR` | Standard material rendering with metallic and roughness controls |
| `PureBase/Hybrid` | Toon-style diffuse lighting combined with physically based reflections |

Every shader can be used without installing an optional module.

## Supported environment

- Unity 2022.3
- Built-in Render Pipeline
- Shader-Core 0.1.9

URP and transparent material blending are not supported. Transparent areas use Cutout rendering.

## Installation

Pure Base can be installed through VRChat Creator Companion, ALCOM, or another VPM-compatible package manager.

### 1. Add the package repository

Open the following link to add Penguin VPM Repository:

[Add Penguin VPM Repository](vcc://vpm/addRepo?url=https://raw.githubusercontent.com/Penguin-Repository/VPM-Repository/refs/heads/master/vpm.json)

If the link does not open, paste this URL into your package manager's repository settings:

```text
https://raw.githubusercontent.com/Penguin-Repository/VPM-Repository/refs/heads/master/vpm.json
```

If you have not already added the Shader-Core repository, add this URL as well:

```text
https://lilxyzw.github.io/vpm-repos/vpm.json
```

### 2. Add Pure Base to a project

1. Open the Unity project in your package manager.
2. Find `PureBase` in the package list.
3. Select the version you want and add it to the project.
4. Confirm that Shader-Core 0.1.9 is installed with it.

Pure Base is currently distributed as a prerelease. Some package managers hide prerelease packages by default, so you may need to enable prerelease or development-version visibility.

## Basic use

1. Create a new material in Unity.
2. Open the material's shader menu and select `PureBase`.
3. Choose `Unlit`, `Toon`, `PBR`, or `Hybrid` for the intended look.
4. Set the base color, texture, and other available properties.
5. Add Shader-Core modules when additional effects are needed.

For a simple starting point, choose `Toon` for anime-style materials or `PBR` for general-purpose materials.

## Notes

- Pure Base is intentionally kept small.
- Effects such as rim lighting, MatCap, emission, and dissolve are expected to be supplied by separate Shader-Core modules.
- Behavior and usage may change while the package is in prerelease.
- When reporting a problem, include the Unity, Pure Base, and Shader-Core versions you used.

## Technical documentation

This README is intended to be enough for installation and basic use.

Implementation details, release operations, compatibility contracts, and validation procedures are collected in [Technical information](Docs/technical-information.md).

## License and support

Pure Base is released under the Apache License 2.0. See [LICENSE](LICENSE) for details.

Pure Base and Penguin do not accept financial support. Bug reports, suggestions, pull requests, code, and patches are welcome.
