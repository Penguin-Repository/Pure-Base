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

// Encodes the direct-only ForwardAdd aggregate direction for fixture readback.

#if defined(UNITY_PASS_FORWARDADD)
/// <summary>Identifies the ForwardAdd direct-direction Shade diagnostic source.</summary>
#define PUREBASE_TEST_TOON_OPENLIT_GAMMA_SENTINEL_SHADE 1

sd.col.rgb = sd.L * half(0.5) + half(0.5);
sd.add = 0;
sd.postadd = 0;
#endif
