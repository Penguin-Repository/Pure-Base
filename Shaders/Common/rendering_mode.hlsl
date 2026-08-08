/*
 * Copyright 2026 Penguin
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

// Defines shared rendering-mode coverage and output-alpha contracts for Pure-Base hosts.

#ifndef PUREBASE_RENDERING_MODE_INCLUDED
#define PUREBASE_RENDERING_MODE_INCLUDED

/// <summary>Applies the Cutout coverage threshold only when neither opaque nor transparent mode is selected.</summary>
void PureBaseApplyRenderingModeClip(half coverage)
{
	#if !defined(PUREBASE_RENDERING_OPAQUE) && !defined(PUREBASE_RENDERING_TRANSPARENT)
	clip(coverage - _Cutoff);
	#endif
}

/// <summary>Writes coverage alpha for Transparent and opaque alpha for Opaque and Cutout output.</summary>
void PureBaseApplyRenderingModeOutputAlpha(inout half4 color, half coverage)
{
	#if defined(PUREBASE_RENDERING_TRANSPARENT)
	color.a = coverage;
	#else
	color.a = 1;
	#endif
}

#endif
