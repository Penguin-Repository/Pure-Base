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

// Defines the ForwardAdd-only fog signal used by the Unlit runtime probe.
#define PUREBASE_UNLIT_FORWARD_ADD_FOG_SENTINEL 1

#if defined(UNITY_PASS_FORWARDADD)
sd.add += half3(0.25, 0.125, 0.0625);
#endif
