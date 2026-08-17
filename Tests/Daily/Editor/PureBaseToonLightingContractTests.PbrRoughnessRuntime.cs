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

// Owns direct and reflection fixture setup for PBR perceptual-roughness GPU observations.

using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace PureBase.Tests.Daily
{
    /// <summary>Provides roughness-specific extensions to the isolated BIRP capture scope.</summary>
    public sealed partial class PureBaseToonLightingContractTests
    {
        /// <summary>Owns direct and reflection capture additions for the PBR roughness floor contracts.</summary>
        private partial class ToonLightingCaptureRuntimeScope
        {
            /// <summary>Renders one full 64x64 direct PBR visibility frame with measured incidence coordinates.</summary>
            public PbrVisibilityObservation RenderPbrVisibilityReference(string shaderName, string passName, float metallic, float roughness, Vector3 normal, string incidence, Vector3? lightDirectionOverride = null)
            {
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                Vector3 normalizedNormal = normal.normalized;
                Vector3 lightDirection = lightDirectionOverride ?? Vector3.Reflect(Vector3.forward, normalizedNormal).normalized;
                Material material = CreatePbrRoughnessMaterial(shaderName, passName, roughness, metallic);
                LightCaptureRequest request = passName == "ForwardAdd"
                    ? CreateLightCaptureRequest(normalizedNormal, new Vector4(0.015f, 0.012f, 0.009f, 1.0f), new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 1.0f), ShCoefficients.Zero, LightType.Point, 4.0f)
                    : CreateDirectionalLightCaptureRequest(normalizedNormal, new Vector4(0.015f, 0.012f, 0.009f, 1.0f), new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0.0f), ShCoefficients.Zero);
                Color[] pixels = passName == "ForwardAdd"
                    ? RenderPbrVisibilityLightDifference(material, request)
                    : RenderPbrVisibilityWithLights(material, request);
                return new PbrVisibilityObservation(shaderName, passName, metallic, roughness, incidence, pixels, normalizedNormal, lightDirection.normalized, Vector3.back, request, camera, meshFilter.transform);
            }

            /// <summary>Renders a low-radiance metallic direct observation through an explicit forward pass.</summary>
            public Color RenderPbrRoughnessDirect(string shaderName, string passName, float roughness, Vector3 normal)
            {
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                Material material = CreatePbrRoughnessMaterial(shaderName, passName, roughness, 1.0f);
                Vector3 lightDirection = Vector3.Reflect(Vector3.forward, normal.normalized).normalized;
                if (passName == "ForwardAdd")
                    return RenderLightDifference(material, CreateLightCaptureRequest(normal, new Vector4(0.015f, 0.012f, 0.009f, 1.0f), new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 1.0f), ShCoefficients.Zero, LightType.Point, 4.0f));
                return RenderWithLights(material, CreateDirectionalLightCaptureRequest(normal, new Vector4(0.015f, 0.012f, 0.009f, 1.0f), new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0.0f), ShCoefficients.Zero));
            }

            /// <summary>Renders direct-light-free metallic reflection from fixture-owned mip-distinct cubemap data.</summary>
            public Color RenderPbrRoughnessReflection(string shaderName, float roughness)
            {
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                ConfigurePbrRoughnessReflection();
                Material material = CreatePbrRoughnessMaterial(shaderName, "ForwardBase", roughness, 1.0f);
                return RenderWithLights(material, CreateDirectionalLightCaptureRequest(Vector3.back, Vector4.zero, new Vector4(0.0f, 0.0f, -1.0f, 0.0f), ShCoefficients.Zero));
            }

            /// <summary>Creates a high-albedo metallic PBR-family material without a direct-diffuse contribution.</summary>
            private Material CreatePbrRoughnessMaterial(string shaderName, string passName, float roughness, float metallic)
            {
                Material material = CreateProductMaterial(shaderName, passName, metallic);
                material.SetTexture("_BaseTexture", Texture2D.whiteTexture);
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Roughness", roughness);
                material.SetInteger("_UseUnityStandardDiffuseBrightness", 0);
                return material;
            }

            /// <summary>Renders one explicit light configuration and copies all 64x64 linear float samples before cleanup.</summary>
            private Color[] RenderPbrVisibilityWithLights(Material material, LightCaptureRequest request)
            {
                var lightObjects = new System.Collections.Generic.List<GameObject>();
                try
                {
                    InjectShGlobals(request.coefficients);
                    ApplyShProperties(request.coefficients);
                    meshFilter.sharedMesh = CreateNormalControlledQuad(request.normal);
                    renderer.sharedMaterial = material;
                    renderer.enabled = true;
                    CreateLights(lightObjects, request);
                    camera.Render();
                    Assert.That(camera.actualRenderingPath, Is.EqualTo(RenderingPath.Forward), "Visibility capture requires the BIRP Forward camera path.");
                    return ReadPixels();
                }
                finally
                {
                    renderer.enabled = false;
                    renderer.SetPropertyBlock(null);
                    DestroyGameObjects(lightObjects);
                }
            }

            /// <summary>Renders one and two equivalent Point lights, returning the full isolated second-light frame.</summary>
            private Color[] RenderPbrVisibilityLightDifference(Material material, LightCaptureRequest request)
            {
                request.lightCount = 1;
                Color[] oneLight = RenderPbrVisibilityWithLights(material, request);
                request.lightCount = 2;
                Color[] twoLights = RenderPbrVisibilityWithLights(material, request);
                var difference = new Color[oneLight.Length];
                for (int index = 0; index < difference.Length; index++)
                    difference[index] = twoLights[index] - oneLight[index];
                return difference;
            }

            /// <summary>Installs a transient custom reflection cubemap with distinct finite colors in every mip level.</summary>
            private void ConfigurePbrRoughnessReflection()
            {
                var cubemap = new Cubemap(8, TextureFormat.RGBAFloat, true) { hideFlags = HideFlags.HideAndDontSave };
                pbrBrightnessResources.Add(cubemap);
                for (int mip = 0; mip < cubemap.mipmapCount; mip++)
                {
                    Color color = new Color(0.12f + (mip * 0.21f), 0.08f + (mip * 0.13f), 0.04f + (mip * 0.07f), 1.0f);
                    int size = Mathf.Max(1, cubemap.width >> mip);
                    Color[] pixels = CreatePbrRoughnessMipPixels(size, color);
                    foreach (CubemapFace face in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
                        cubemap.SetPixels(pixels, face, mip);
                }

                cubemap.Apply(false, true);
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cubemap;
                RenderSettings.reflectionIntensity = 1.0f;
            }

            /// <summary>Creates the uniformly colored pixels assigned to one owned cubemap mip level.</summary>
            private static Color[] CreatePbrRoughnessMipPixels(int size, Color color)
            {
                var pixels = new Color[size * size];
                for (int index = 0; index < pixels.Length; index++)
                    pixels[index] = color;
                return pixels;
            }
        }

        /// <summary>Stores a complete PBR visibility frame and its measured fixture inputs.</summary>
        private readonly struct PbrVisibilityObservation
        {
            /// <summary>Initializes one frame observation.</summary>
            public PbrVisibilityObservation(string shaderName, string passName, float metallic, float roughness, string incidence, Color[] pixels, Vector3 normal, Vector3 lightDirection, Vector3 viewDirection, LightCaptureRequest request, Camera camera, Transform meshTransform)
            {
                ShaderName = shaderName;
                PassName = passName;
                Metallic = metallic;
                Roughness = roughness;
                Incidence = incidence;
                Pixels = pixels;
                Normal = normal;
                LightDirection = lightDirection;
                ViewDirection = viewDirection;
                MeasuredNdotL = Vector3.Dot(normal, lightDirection);
                MeasuredNdotV = Vector3.Dot(normal, viewDirection);
                LightPlusView = lightDirection + viewDirection;
                HalfVector = LightPlusView.sqrMagnitude > 0.0f ? LightPlusView.normalized : Vector3.zero;
                LightType = request.lightType;
                LightColor = request.lightColor;
                LightRange = request.range;
                LightTransformPosition = request.lightType == LightType.Directional ? Vector3.zero : new Vector3(request.lightPosition.x, request.lightPosition.y, request.lightPosition.z);
                LightTransformRotation = request.lightType == LightType.Directional ? Quaternion.LookRotation(-lightDirection, Vector3.up) : Quaternion.identity;
                CameraPosition = camera.transform.position;
                CameraRotation = camera.transform.rotation;
                CameraOrthographicSize = camera.orthographicSize;
                CameraNearClipPlane = camera.nearClipPlane;
                CameraFarClipPlane = camera.farClipPlane;
                CameraRenderingPath = camera.actualRenderingPath.ToString();
                MeshPosition = meshTransform.position;
                MeshRotation = meshTransform.rotation;
                MeshScale = meshTransform.lossyScale;
            }

            /// <summary>Gets the full linear ARGBFloat frame.</summary>
            public Color[] Pixels { get; }

            /// <summary>Gets the center frame sample.</summary>
            public Color Center => Pixels[31 + (31 * 64)];

            /// <summary>Gets the measured light cosine.</summary>
            public float MeasuredNdotL { get; }

            /// <summary>Gets the measured view cosine.</summary>
            public float MeasuredNdotV { get; }

            /// <summary>Gets the measured uniform mesh normal.</summary>
            public Vector3 Normal { get; }

            /// <summary>Gets the measured normalized light vector.</summary>
            public Vector3 LightDirection { get; }

            /// <summary>Gets the measured normalized camera view vector.</summary>
            public Vector3 ViewDirection { get; }

            /// <summary>Gets the measured unnormalized light-plus-view vector.</summary>
            public Vector3 LightPlusView { get; }

            /// <summary>Gets the measured half vector, or zero when the light-plus-view vector degenerates.</summary>
            public Vector3 HalfVector { get; }

            /// <summary>Gets the real Unity light type used for this observation.</summary>
            public LightType LightType { get; }

            /// <summary>Gets the linear light color requested for this observation.</summary>
            public Vector4 LightColor { get; }

            /// <summary>Gets the configured Point-light range.</summary>
            public float LightRange { get; }

            /// <summary>Gets the generated light transform position.</summary>
            public Vector3 LightTransformPosition { get; }

            /// <summary>Gets the generated light transform rotation.</summary>
            public Quaternion LightTransformRotation { get; }

            /// <summary>Gets the capture camera transform position.</summary>
            public Vector3 CameraPosition { get; }

            /// <summary>Gets the capture camera transform rotation.</summary>
            public Quaternion CameraRotation { get; }

            /// <summary>Gets the capture camera orthographic size.</summary>
            public float CameraOrthographicSize { get; }

            /// <summary>Gets the capture camera near clip plane.</summary>
            public float CameraNearClipPlane { get; }

            /// <summary>Gets the capture camera far clip plane.</summary>
            public float CameraFarClipPlane { get; }

            /// <summary>Gets the actual camera rendering path used by the render.</summary>
            public string CameraRenderingPath { get; }

            /// <summary>Gets the mesh transform position at render time.</summary>
            public Vector3 MeshPosition { get; }

            /// <summary>Gets the mesh transform rotation at render time.</summary>
            public Quaternion MeshRotation { get; }

            /// <summary>Gets the mesh transform lossy scale at render time.</summary>
            public Vector3 MeshScale { get; }

            /// <summary>Gets the human-readable observation label.</summary>
            public string Label => ShaderName + " " + PassName + " m=" + Metallic + " r=" + Roughness + " " + Incidence;

            /// <summary>Gets the deterministic diagnostic filename.</summary>
            public string FileName => ShaderName.Replace("/", "-") + "-" + PassName + "-m" + Metallic.ToString("0") + "-r" + Roughness.ToString("0.###") + "-" + Incidence + ".png";

            /// <summary>Gets whether every RGB frame sample is finite.</summary>
            public bool FrameFinite
            {
                get
                {
                    foreach (Color pixel in Pixels)
                        if (!float.IsFinite(pixel.r) || !float.IsFinite(pixel.g) || !float.IsFinite(pixel.b)) return false;
                    return true;
                }
            }

            /// <summary>Gets whether the center RGB sample is finite.</summary>
            public bool CenterFinite => float.IsFinite(Center.r) && float.IsFinite(Center.g) && float.IsFinite(Center.b);

            /// <summary>Gets the source shader name.</summary>
            public string ShaderName { get; }

            /// <summary>Gets the rendered forward-pass name.</summary>
            public string PassName { get; }

            /// <summary>Gets the material metallic value.</summary>
            public float Metallic { get; }

            /// <summary>Gets the material perceptual roughness.</summary>
            public float Roughness { get; }

            /// <summary>Gets the measured incidence label.</summary>
            public string Incidence { get; }
        }
    }
}
