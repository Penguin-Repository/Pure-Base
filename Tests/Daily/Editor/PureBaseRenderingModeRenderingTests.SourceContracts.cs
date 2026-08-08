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

// Defines source-order contracts for the rendering-mode BIRP integration.

using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PureBase.Tests.Daily
{
	public sealed partial class PureBaseRenderingModeRenderingTests
	{
		/// <summary>Identifies the common BIRP fragment host whose ordering is part of the generated-source ABI.</summary>
		private const string BirpHostPath = "Packages/jp.penguin.purebase/Shaders/Common/birp_host.hlsl";

		/// <summary>Identifies the shared rendering-mode helper that owns mode clip and output-alpha semantics.</summary>
		private const string RenderingModeHelperPath = "Packages/jp.penguin.purebase/Shaders/Common/rendering_mode.hlsl";

		/// <summary>Identifies the shared operation that publishes the mode-specific output alpha.</summary>
		private const string RenderingModeOutputAlphaOperation = "PureBaseApplyRenderingModeOutputAlpha";

		/// <summary>Identifies the rendering-mode keyword whose output alpha preserves coverage.</summary>
		private const string TransparentRenderingModeKeyword = "PUREBASE_RENDERING_TRANSPARENT";

		/// <summary>Identifies the release-only postpixel alpha probe source.</summary>
		private const string PostPixelProbePath = "Packages/jp.penguin.purebase/Tests/Release/Modules/Standard/PostPixel/phase_postpixel.hlsl";

		/// <summary>Requires the shared mode-alpha helper to run after add and before fog, postpixel, and return.</summary>
		[Test]
		public void BirpHostPreservesModeAlphaFogPostPixelAndForwardAddSourceOrder()
		{
			string host = File.ReadAllText(BirpHostPath);
			string renderingModeHelper = File.ReadAllText(RenderingModeHelperPath);
			int addPhase = RequireIndex(host, "__SC_PHASE_add__");
			Match modeOutputAlphaCall = Regex.Match(host, @"\b" + Regex.Escape(RenderingModeOutputAlphaOperation) + @"\s*\(");
			Assert.That(modeOutputAlphaCall.Success, Is.True, "The BIRP host must call the shared rendering-mode output-alpha operation.");
			int modeOutputAlpha = modeOutputAlphaCall.Index;
			int fog = RequireIndex(host, "UNITY_APPLY_FOG");
			int postPixel = RequireIndex(host, "__SC_PHASE_postpixel__");
			int returnStatement = RequireIndex(host, "return sd.col;");
			StringAssert.Contains("#include \"Packages/jp.penguin.purebase/Shaders/Common/rendering_mode.hlsl\"", host);
			Assert.That(modeOutputAlpha, Is.GreaterThan(addPhase), "The shared mode-alpha helper must run after the add phase.");
			Assert.That(modeOutputAlpha, Is.LessThan(fog), "The shared mode-alpha helper must run before fog.");
			Assert.That(fog, Is.LessThan(postPixel), "Fog must occur before postpixel.");
			Assert.That(postPixel, Is.LessThan(returnStatement), "Postpixel must remain the final color mutation point before return.");
			StringAssert.Contains(RenderingModeOutputAlphaOperation, renderingModeHelper);
			StringAssert.Contains(TransparentRenderingModeKeyword, renderingModeHelper);
			StringAssert.Contains("coverage", renderingModeHelper);
			StringAssert.Contains(".a", renderingModeHelper);
			Assert.That(Regex.IsMatch(renderingModeHelper, @"\b1(?:\.0+)?\b"), Is.True, "The shared helper must distinguish Transparent coverage alpha from Opaque and Cutout alpha one.");
			string generatedProductSource = LoadProductSource("PureBase/Toon");
			Assert.That(Regex.IsMatch(generatedProductSource, @"\b" + Regex.Escape(RenderingModeOutputAlphaOperation) + @"\s*\("), Is.True, "The generated product source must retain the shared rendering-mode output-alpha operation.");
			StringAssert.Contains("Blend [_AddSrcBlend] [_AddDstBlend]", generatedProductSource);
			StringAssert.Contains("ColorMask RGB", generatedProductSource);
			StringAssert.Contains("sd.col.a = half(0.25)", File.ReadAllText(PostPixelProbePath));
		}

		/// <summary>Loads one generated product source subasset without modifying its import state.</summary>
		/// <param name="shaderName">The product shader name.</param>
		/// <returns>The generated source text.</returns>
		private static string LoadProductSource(string shaderName)
		{
			foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { "Packages/jp.penguin.purebase/Shaders" }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
				if (shader == null || !string.Equals(shader.name, shaderName, StringComparison.Ordinal))
					continue;
				foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
				{
					var source = asset as TextAsset;
					if (source != null && source.name == "Shader Source")
						return source.text;
				}
			}

			Assert.Fail("Generated source for product shader '" + shaderName + "' was unavailable.");
			return null;
		}

		/// <summary>Returns one required marker index with a diagnostic that keeps source-order failures local.</summary>
		/// <param name="source">The source text to inspect.</param>
		/// <param name="marker">The required marker.</param>
		/// <returns>The marker index.</returns>
		private static int RequireIndex(string source, string marker)
		{
			int index = source.IndexOf(marker, StringComparison.Ordinal);
			Assert.That(index, Is.GreaterThanOrEqualTo(0), "Required source marker '" + marker + "' was absent.");
			return index;
		}
	}
}

