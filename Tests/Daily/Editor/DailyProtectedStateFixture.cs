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

// Applies the Daily read-only state contract inside both batchmode and open-Editor test runs.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;

namespace PureBase.Tests.Daily
{
    /// <summary>Rejects persistent project or tracked-tree changes made during a Daily run.</summary>
    [SetUpFixture]
    public sealed class DailyProtectedStateFixture
    {
        private const string ProjectSettingsRelativePath =
            "ProjectSettings/jp.lilxyzw.shadercore.asset";

        private ProtectedStateSnapshot before;

        /// <summary>Captures the protected state before any Daily test executes.</summary>
        [OneTimeSetUp]
        public void CaptureProtectedState()
        {
            before = ProtectedStateSnapshot.Capture();
        }

        /// <summary>Verifies that Daily left all protected persistent state unchanged.</summary>
        [OneTimeTearDown]
        public void AssertProtectedStateUnchanged()
        {
            Assert.That(before, Is.Not.Null, "Daily protected state was not captured.");
            before.AssertUnchanged(ProtectedStateSnapshot.Capture());
        }

        /// <summary>Stores the protected Shader-Core settings and Git-tracked tree hashes.</summary>
        private sealed class ProtectedStateSnapshot
        {
            private ProtectedStateSnapshot(
                string projectSettingsHash,
                string trackedTreeHash,
                int trackedFileCount
            )
            {
                ProjectSettingsHash = projectSettingsHash;
                TrackedTreeHash = trackedTreeHash;
                TrackedFileCount = trackedFileCount;
            }

            private string ProjectSettingsHash { get; }

            private string TrackedTreeHash { get; }

            private int TrackedFileCount { get; }

            /// <summary>Captures one deterministic snapshot of all protected persistent state.</summary>
            public static ProtectedStateSnapshot Capture()
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string projectSettingsPath = Path.Combine(
                    projectRoot,
                    ProjectSettingsRelativePath.Replace('/', Path.DirectorySeparatorChar)
                );
                Assert.That(
                    File.Exists(projectSettingsPath),
                    Is.True,
                    $"Protected Shader-Core settings file '{projectSettingsPath}' was not found."
                );

                PackageInfo packageInfo = PackageInfo.FindForAssembly(
                    typeof(DailyProtectedStateFixture).Assembly
                );
                Assert.That(
                    packageInfo,
                    Is.Not.Null,
                    "Could not resolve the package that owns the Daily test assembly."
                );
                Assert.That(
                    packageInfo.resolvedPath,
                    Is.Not.Empty,
                    "The Daily test package has no resolved filesystem path."
                );

                string packageRoot = Path.GetFullPath(packageInfo.resolvedPath);
                string gitRoot = RunGit(packageRoot, "rev-parse", "--show-toplevel").Trim();
                Assert.That(gitRoot, Is.Not.Empty, "Could not resolve the package Git root.");
                gitRoot = Path.GetFullPath(gitRoot);

                string trackedOutput = RunGit(
                    gitRoot,
                    "-c",
                    "core.quotepath=false",
                    "ls-files",
                    "--cached",
                    "-z"
                );
                string[] trackedFiles = trackedOutput.Split(
                    new[] { '\0' },
                    StringSplitOptions.RemoveEmptyEntries
                );
                Assert.That(
                    trackedFiles,
                    Is.Not.Empty,
                    $"Package Git root '{gitRoot}' has no tracked files to snapshot."
                );
                Array.Sort(trackedFiles, StringComparer.Ordinal);

                var treeEntries = new StringBuilder();
                foreach (string relativePath in trackedFiles)
                {
                    string fullPath = Path.Combine(
                        gitRoot,
                        relativePath.Replace('/', Path.DirectorySeparatorChar)
                    );
                    Assert.That(
                        File.Exists(fullPath),
                        Is.True,
                        $"Tracked file '{fullPath}' is missing from the working tree."
                    );

                    if (treeEntries.Length > 0)
                        treeEntries.Append('\n');
                    treeEntries.Append(relativePath);
                    treeEntries.Append('\0');
                    treeEntries.Append(GetFileSha256(fullPath));
                }

                return new ProtectedStateSnapshot(
                    GetFileSha256(projectSettingsPath),
                    GetTextSha256(treeEntries.ToString()),
                    trackedFiles.Length
                );
            }

            /// <summary>Asserts that a later snapshot matches this snapshot exactly.</summary>
            public void AssertUnchanged(ProtectedStateSnapshot after)
            {
                Assert.That(after, Is.Not.Null);
                var changes = new List<string>();
                if (
                    !string.Equals(
                        ProjectSettingsHash,
                        after.ProjectSettingsHash,
                        StringComparison.Ordinal
                    )
                )
                {
                    changes.Add(ProjectSettingsRelativePath);
                }

                if (
                    !string.Equals(TrackedTreeHash, after.TrackedTreeHash, StringComparison.Ordinal)
                    || TrackedFileCount != after.TrackedFileCount
                )
                {
                    changes.Add("package Git tracked tree");
                }

                Assert.That(
                    changes,
                    Is.Empty,
                    $"Daily changed protected state: {string.Join(", ", changes)}."
                );
            }
        }

        /// <summary>Runs Git without a shell and returns its standard output.</summary>
        private static string RunGit(string workingDirectory, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = BuildProcessArguments(arguments),
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using (Process process = Process.Start(startInfo))
            {
                Assert.That(process, Is.Not.Null, "Git process could not be started.");
                string standardOutput = process.StandardOutput.ReadToEnd();
                string standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();
                Assert.That(
                    process.ExitCode,
                    Is.Zero,
                    $"Git command failed in '{workingDirectory}': {standardError.Trim()}"
                );
                return standardOutput;
            }
        }

        /// <summary>Builds one safely quoted native process argument string.</summary>
        private static string BuildProcessArguments(IEnumerable<string> arguments)
        {
            var builder = new StringBuilder();
            foreach (string argument in arguments)
            {
                if (builder.Length > 0)
                    builder.Append(' ');
                builder.Append(QuoteProcessArgument(argument));
            }

            return builder.ToString();
        }

        /// <summary>Quotes one argument using the Windows command-line escaping contract.</summary>
        private static string QuoteProcessArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument))
                return "\"\"";

            bool requiresQuotes = false;
            foreach (char character in argument)
            {
                if (char.IsWhiteSpace(character) || character == '"')
                {
                    requiresQuotes = true;
                    break;
                }
            }

            if (!requiresQuotes)
                return argument;

            var builder = new StringBuilder();
            builder.Append('"');
            int backslashCount = 0;
            foreach (char character in argument)
            {
                if (character == '\\')
                {
                    backslashCount++;
                    continue;
                }

                if (character == '"')
                {
                    builder.Append('\\', (backslashCount * 2) + 1);
                    builder.Append('"');
                    backslashCount = 0;
                    continue;
                }

                builder.Append('\\', backslashCount);
                backslashCount = 0;
                builder.Append(character);
            }

            builder.Append('\\', backslashCount * 2);
            builder.Append('"');
            return builder.ToString();
        }

        /// <summary>Returns the lowercase SHA-256 digest for one file.</summary>
        private static string GetFileSha256(string path)
        {
            using (SHA256 hasher = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return ToLowerHex(hasher.ComputeHash(stream));
            }
        }

        /// <summary>Returns the lowercase SHA-256 digest for UTF-8 text.</summary>
        private static string GetTextSha256(string text)
        {
            using (SHA256 hasher = SHA256.Create())
            {
                return ToLowerHex(hasher.ComputeHash(Encoding.UTF8.GetBytes(text)));
            }
        }

        /// <summary>Formats a digest without separators.</summary>
        private static string ToLowerHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
