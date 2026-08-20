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

// Freezes adaptive-oracle selection, witness calibration, and strict v4 serialization.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace PureBase.Tests.Daily
{
    /// <summary>Freezes adaptive-oracle selection, witness calibration, and strict v4 serialization.</summary>
    internal static partial class AdaptiveProtocol
    {
        internal const int CoefficientCount = 16;
        internal const double GuardSquared = 0.000001d;
        internal static readonly string PackageRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "jp.penguin.purebase"));
        internal static string CanonicalArtifactPath => Path.Combine(PackageRoot, "Tests", "Fixtures", "Data", "pbr-multiple-scattering-kronrod-oracle-v4.json");
        private static readonly AdaptiveCoordinate[] Original = Grid(new[] { 0.089d, 0.25d, 0.5d, 1.0d }, new[] { 0.0d, 0.05d, 0.5d, 1.0d });
        private static readonly AdaptiveCoordinate[] Stress = Grid(new[] { 0.089d, 0.09d, 0.1d, 0.125d, 0.25d }, new[] { 0.0d, 0.001d, 0.01d, 0.05d, 0.1d });
        private static readonly AdaptiveCoordinate[] Training = ChebyshevGrid();
        private static readonly AdaptiveCoordinate[] Validation = Grid(new[] { 0.089d, 0.09d, 0.1d, 0.125d, 0.25d, 0.5d, 1.0d }, new[] { 0.0d, 0.001d, 0.01d, 0.05d, 0.1d, 0.5d, 1.0d });
        private static readonly AdaptiveSettings[] Candidates =
        {
            new AdaptiveSettings("calibration-a", 0.00004d, 0.0004d, 0.00001d, 0.0001d, 18, 65536, 1000000),
            new AdaptiveSettings("calibration-b", 0.00001d, 0.0001d, 0.0000025d, 0.000025d, 22, 262144, 4000000),
            new AdaptiveSettings("calibration-c", 0.0000025d, 0.000025d, 0.000000625d, 0.00000625d, 26, 1048576, 16000000)
        };

        /// <summary>Gets the positive NdotH-squared transition for the GGX denominator clamp.</summary>
        internal static bool TryGetDistributionNdotHSquared(double p, out double value)
        {
            double roughnessFourth = Math.Pow(p, 4.0d); double root = Math.Sqrt(GuardSquared / Math.PI);
            value = (1.0d - root) / (1.0d - roughnessFourth);
            return value > 0.0d && value < 1.0d;
        }

        /// <summary>Builds a stable ASCII record without host or elapsed-time values.</summary>
        internal static string BuildArtifact(AdaptiveSelection selection) => BuildArtifactRecord(selection);

        /// <summary>Fits the fixed 16-term cubic with deterministic unpivoted Householder QR.</summary>
        private static float[] SolveFit(double[] energy)
        {
            int rows = energy.Length; var matrix = new double[rows * CoefficientCount]; var target = new double[rows];
            for (int row = 0; row < rows; row++) { Monomials(matrix, row, Training[row]); target[row] = 1.0d / energy[row] - 1.0d; }
            for (int column = 0; column < CoefficientCount; column++) Reflect(matrix, target, column, rows);
            var result = new float[CoefficientCount];
            for (int row = CoefficientCount - 1; row >= 0; row--) { double sum = target[row]; for (int column = row + 1; column < CoefficientCount; column++) sum -= matrix[row * CoefficientCount + column] * result[column]; result[row] = (float)(sum / matrix[row * CoefficientCount + row]); }
            return result;
        }

        /// <summary>Applies one Householder reflector without pivoting the declared basis.</summary>
        private static void Reflect(double[] matrix, double[] target, int column, int rows)
        {
            double norm = 0.0d; for (int row = column; row < rows; row++) norm += matrix[row * CoefficientCount + column] * matrix[row * CoefficientCount + column];
            double alpha = matrix[column * CoefficientCount + column] >= 0.0d ? -Math.Sqrt(norm) : Math.Sqrt(norm); var vector = new double[rows - column];
            for (int row = column; row < rows; row++) vector[row - column] = matrix[row * CoefficientCount + column]; vector[0] -= alpha;
            double scale = 0.0d; foreach (double value in vector) scale += value * value; scale = 2.0d / scale;
            for (int targetColumn = column; targetColumn < CoefficientCount; targetColumn++) Apply(matrix, vector, scale, column, rows, targetColumn);
            double projection = 0.0d; for (int row = column; row < rows; row++) projection += vector[row - column] * target[row]; for (int row = column; row < rows; row++) target[row] -= scale * vector[row - column] * projection;
            matrix[column * CoefficientCount + column] = alpha; for (int row = column + 1; row < rows; row++) matrix[row * CoefficientCount + column] = 0.0d;
        }

        /// <summary>Applies a reflector to one deterministic matrix column.</summary>
        private static void Apply(double[] matrix, double[] vector, double scale, int start, int rows, int column)
        {
            double projection = 0.0d; for (int row = start; row < rows; row++) projection += vector[row - start] * matrix[row * CoefficientCount + column];
            for (int row = start; row < rows; row++) matrix[row * CoefficientCount + column] -= scale * vector[row - start] * projection;
        }

        /// <summary>Writes one row-major cubic basis from the immutable training order.</summary>
        private static void Monomials(double[] matrix, int row, AdaptiveCoordinate point)
        {
            int offset = row * CoefficientCount; double x = Normalize(point.P, 0.089d, 1.0d); double y = Normalize(point.V, 0.0d, 1.0d); double xp = 1.0d;
            for (int xi = 0; xi < 4; xi++) { double yp = 1.0d; for (int yi = 0; yi < 4; yi++) { matrix[offset + xi * 4 + yi] = xp * yp; yp *= y; } xp *= x; }
        }

        /// <summary>Evaluates the float nested-Horner model using binary16 input widening.</summary>
        private static float Evaluate(float[] coefficients, float p, float v)
        {
            float x = Normalize(Mathf.HalfToFloat(Mathf.FloatToHalf(p)), 0.089f, 1.0f); float y = Normalize(Mathf.HalfToFloat(Mathf.FloatToHalf(v)), 0.0f, 1.0f); float value = 0.0f;
            for (int degree = 3; degree >= 0; degree--) { int offset = degree * 4; value = value * x + ((coefficients[offset + 3] * y + coefficients[offset + 2]) * y + coefficients[offset + 1]) * y + coefficients[offset]; }
            return value;
        }

        /// <summary>Persists a selected record only if its exact bytes already agree.</summary>
        private static void Persist(string artifact)
        {
            string path = CanonicalArtifactPath;
            byte[] expected = CanonicalArtifactBytes(artifact);
            if (File.Exists(path) && !BytesEqual(File.ReadAllBytes(path), expected)) throw new InvalidOperationException("The existing v4 record is not byte-identical.");
            if (!File.Exists(path)) File.WriteAllBytes(path, expected);
        }

        /// <summary>Encodes a record only when it satisfies the canonical ASCII and LF byte contract.</summary>
        internal static byte[] CanonicalArtifactBytesForTest(string artifact) => CanonicalArtifactBytes(artifact);

        /// <summary>Tests a raw record byte sequence against the canonical ASCII and terminal-LF contract.</summary>
        internal static bool IsCanonicalArtifactBytesForTest(byte[] bytes)
        {
            try { ValidateCanonicalArtifactBytes(bytes); return true; }
            catch (InvalidOperationException) { return false; }
        }

        /// <summary>Encodes and validates the exact raw bytes used for strict record persistence.</summary>
        private static byte[] CanonicalArtifactBytes(string artifact)
        {
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(artifact); ValidateCanonicalArtifactBytes(bytes); return bytes;
        }

        /// <summary>Rejects a BOM, carriage return, non-ASCII byte, or noncanonical terminal LF sequence.</summary>
        private static void ValidateCanonicalArtifactBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes[bytes.Length - 1] != (byte)'\n' || (bytes.Length > 1 && bytes[bytes.Length - 2] == (byte)'\n')) throw new InvalidOperationException("The v4 record must end with exactly one LF.");
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) throw new InvalidOperationException("The v4 record must not contain a UTF-8 BOM.");
            for (int index = 0; index < bytes.Length; index++) if (bytes[index] == (byte)'\r' || bytes[index] > 0x7F) throw new InvalidOperationException("The v4 record must contain ASCII LF-only bytes.");
        }

        /// <summary>Compares two raw byte arrays without applying text decoding or newline normalization.</summary>
        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (int index = 0; index < left.Length; index++) if (left[index] != right[index]) return false;
            return true;
        }

        /// <summary>Formats an IEEE-754 bit pattern in stable uppercase hexadecimal.</summary>
        private static string Bits(double value) => "0x" + BitConverter.DoubleToInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture);
        /// <summary>Formats an invariant round-trip double.</summary>
        private static string D(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        /// <summary>Formats an invariant round-trip float.</summary>
        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        /// <summary>Maps a scalar to the fixed cubic domain.</summary>
        private static double Normalize(double value, double minimum, double maximum) => ((value - minimum) / (maximum - minimum)) * 2.0d - 1.0d;
        /// <summary>Maps a float to the fixed cubic domain.</summary>
        private static float Normalize(float value, float minimum, float maximum) => ((value - minimum) / (maximum - minimum)) * 2.0f - 1.0f;
        /// <summary>Creates a row-major Cartesian grid.</summary>
        private static AdaptiveCoordinate[] Grid(double[] p, double[] v) { var result = new AdaptiveCoordinate[p.Length * v.Length]; int index = 0; foreach (double roughness in p) foreach (double view in v) result[index++] = new AdaptiveCoordinate(roughness, view); return result; }
        /// <summary>Combines grids while preserving first occurrence and row-major ordering.</summary>
        private static AdaptiveCoordinate[] Combine(AdaptiveCoordinate[] first, AdaptiveCoordinate[] second) { var values = new List<AdaptiveCoordinate>(first); foreach (AdaptiveCoordinate point in second) if (!values.Contains(point)) values.Add(point); return values.ToArray(); }
        /// <summary>Creates the immutable original 13x13 Chebyshev-Lobatto grid.</summary>
        private static AdaptiveCoordinate[] ChebyshevGrid() { var result = new AdaptiveCoordinate[169]; int index = 0; for (int p = 0; p < 13; p++) for (int v = 0; v < 13; v++) result[index++] = new AdaptiveCoordinate(Chebyshev(p, 0.089d, 1.0d), Chebyshev(v, 0.0d, 1.0d)); return result; }
        /// <summary>Maps an index to a Chebyshev-Lobatto coordinate.</summary>
        private static double Chebyshev(int index, double minimum, double maximum) => minimum + (maximum - minimum) * (1.0d + Math.Cos(Math.PI * index / 12.0d)) * 0.5d;
    }

    /// <summary>Defines immutable tolerances, witness hierarchy, caps, and deterministic ordering.</summary>
    internal readonly struct AdaptiveSettings
    {
        /// <summary>Initializes immutable selected and witness limits.</summary>
        internal AdaptiveSettings(string name, double absolute, double relative, double witnessAbsolute, double witnessRelative, int maxDepth, int maxPanels, int maxEvaluations) { Name = name; Absolute = absolute; Relative = relative; WitnessAbsolute = witnessAbsolute; WitnessRelative = witnessRelative; MaxDepth = maxDepth; MaxPanels = maxPanels; MaxEvaluations = maxEvaluations; }
        /// <summary>Gets the deterministic settings identifier.</summary>
        internal string Name { get; }
        /// <summary>Gets the selected absolute tolerance.</summary>
        internal double Absolute { get; }
        /// <summary>Gets the selected relative tolerance.</summary>
        internal double Relative { get; }
        /// <summary>Gets the witness absolute tolerance.</summary>
        internal double WitnessAbsolute { get; }
        /// <summary>Gets the witness relative tolerance.</summary>
        internal double WitnessRelative { get; }
        /// <summary>Gets the selected recursion depth cap.</summary>
        internal int MaxDepth { get; }
        /// <summary>Gets the selected panel cap.</summary>
        internal int MaxPanels { get; }
        /// <summary>Gets the selected evaluation cap.</summary>
        internal int MaxEvaluations { get; }
        /// <summary>Creates the fixed stricter witness for a selected candidate.</summary>
        internal AdaptiveSettings Witness() => new AdaptiveSettings(Name + "-witness", WitnessAbsolute, WitnessRelative, WitnessAbsolute, WitnessRelative, MaxDepth + 2, MaxPanels * 2, MaxEvaluations * 2);
        /// <summary>Calculates the global adaptive error tolerance for one final value.</summary>
        internal double Tolerance(double value) => Absolute + Relative * Math.Abs(value);
        /// <summary>Calculates the independent witness error tolerance for one final value.</summary>
        internal double WitnessTolerance(double value) => WitnessAbsolute + WitnessRelative * Math.Abs(value);
    }

    /// <summary>Identifies a path without sharing transform, splitter, rule, scheduler, or accumulator implementations.</summary>
    internal readonly struct AdaptiveIdentity
    {
        /// <summary>Initializes one independently implemented path identity.</summary>
        internal AdaptiveIdentity(string transform, string splitter, string rule, string scheduler, string accumulator) { Transform = transform; Splitter = splitter; Rule = rule; Scheduler = scheduler; Accumulator = accumulator; }
        /// <summary>Gets the path coordinate transformation.</summary>
        internal string Transform { get; }
        /// <summary>Gets the analytic partition strategy.</summary>
        internal string Splitter { get; }
        /// <summary>Gets the embedded integration rule.</summary>
        internal string Rule { get; }
        /// <summary>Gets the deterministic recursive scheduler.</summary>
        internal string Scheduler { get; }
        /// <summary>Gets the retained sum accumulator.</summary>
        internal string Accumulator { get; }
    }
    /// <summary>Stores one immutable roughness and view-cosine coordinate.</summary>
    internal readonly struct AdaptiveCoordinate
    {
        /// <summary>Initializes one roughness and view-cosine coordinate.</summary>
        internal AdaptiveCoordinate(double p, double v) { P = p; V = v; }
        /// <summary>Gets the perceptual roughness coordinate.</summary>
        internal double P { get; }
        /// <summary>Gets the view-cosine coordinate.</summary>
        internal double V { get; }
        /// <summary>Gets an invariant diagnostic representation.</summary>
        internal string Text => "p=" + P.ToString("R", CultureInfo.InvariantCulture) + ",ndotV=" + V.ToString("R", CultureInfo.InvariantCulture);
    }
    /// <summary>Stores one globally bounded or fail-closed adaptive integration result.</summary>
    internal readonly struct AdaptiveResult
    {
        /// <summary>Initializes one bounded integration result and its deterministic resource evidence.</summary>
        internal AdaptiveResult(double value, double error, double tolerance, int evaluations, int panels, int depth, string diagnostic) { Value = value; Error = error; Tolerance = tolerance; Evaluations = evaluations; Panels = panels; Depth = depth; Diagnostic = diagnostic; }
        /// <summary>Gets the estimated integral.</summary>
        internal double Value { get; }
        /// <summary>Gets the retained error estimate.</summary>
        internal double Error { get; }
        /// <summary>Gets the final acceptance tolerance.</summary>
        internal double Tolerance { get; }
        /// <summary>Gets completed sample evaluations.</summary>
        internal int Evaluations { get; }
        /// <summary>Gets accepted and rejected panel attempts.</summary>
        internal int Panels { get; }
        /// <summary>Gets the maximum reached recursion depth.</summary>
        internal int Depth { get; }
        /// <summary>Gets the first failure diagnostic, when present.</summary>
        internal string Diagnostic { get; }
        /// <summary>Gets whether the finite result passed its final tolerance without a cap failure.</summary>
        internal bool IsAccepted => Diagnostic == null && PureBasePbrMultipleScatteringReference.IsFinite(Value) && PureBasePbrMultipleScatteringReference.IsFinite(Error) && Error <= Tolerance;
    }
    /// <summary>Pairs a quadrature value with the error retained by its accepted descendant panels.</summary>
    internal readonly struct AdaptiveEstimate
    {
        /// <summary>Initializes an estimate with its retained accepted-leaf error.</summary>
        internal AdaptiveEstimate(double value, double error) { Value = value; Error = error; }
        /// <summary>Gets the estimated value.</summary>
        internal double Value { get; }
        /// <summary>Gets the retained error estimate.</summary>
        internal double Error { get; }
        /// <summary>Scales both an estimate and its absolute error.</summary>
        internal static AdaptiveEstimate Scale(AdaptiveEstimate value, double scale) => new AdaptiveEstimate(value.Value * scale, value.Error * Math.Abs(scale));
    }
    /// <summary>Stores raw evidence at one coordinate across the two independently implemented paths.</summary>
    internal readonly struct AdaptiveEvidence
    {
        /// <summary>Initializes independent selected, witness, and cross-path evidence.</summary>
        internal AdaptiveEvidence(AdaptiveCoordinate point, AdaptiveResult primary, AdaptiveResult witness, AdaptiveResult cross) { Point = point; Primary = primary; Witness = witness; Cross = cross; CrossDifference = Math.Abs(primary.Value - cross.Value); }
        /// <summary>Gets the evaluated coordinate.</summary>
        internal AdaptiveCoordinate Point { get; }
        /// <summary>Gets the selected primary result.</summary>
        internal AdaptiveResult Primary { get; }
        /// <summary>Gets the stricter primary witness result.</summary>
        internal AdaptiveResult Witness { get; }
        /// <summary>Gets the independent cross-check result.</summary>
        internal AdaptiveResult Cross { get; }
        /// <summary>Gets the selected-to-cross absolute difference.</summary>
        internal double CrossDifference { get; }
        /// <summary>Gets whether all selected, witness, cross, and agreement gates pass.</summary>
        internal bool Passes => Primary.IsAccepted && Witness.IsAccepted && Cross.IsAccepted && Primary.Error <= Primary.Tolerance && Witness.Error <= Witness.Tolerance && Cross.Error <= Cross.Tolerance && Math.Abs(Primary.Value - Witness.Value) <= 0.0001d && CrossDifference <= 0.001d;
    }
    /// <summary>Stores branch-local canonical fit comparison statistics.</summary>
    internal readonly struct AdaptiveFit
    {
        /// <summary>Gets the empty noncanonical fit evidence.</summary>
        internal static readonly AdaptiveFit Empty = new AdaptiveFit(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, true);
        /// <summary>Initializes selected and witness fit stability evidence.</summary>
        internal AdaptiveFit(float coefficientDelta, float gainDelta, float p95, float maximum, float witnessP95, float witnessMaximum, bool improves) { CoefficientDelta = coefficientDelta; GainDelta = gainDelta; P95 = p95; Maximum = maximum; WitnessP95 = witnessP95; WitnessMaximum = witnessMaximum; Improves = improves; }
        /// <summary>Gets the maximum selected-to-witness coefficient difference.</summary>
        internal float CoefficientDelta { get; }
        /// <summary>Gets the maximum selected-to-witness gain difference.</summary>
        internal float GainDelta { get; }
        /// <summary>Gets the selected compact-grid p95 error.</summary>
        internal float P95 { get; }
        /// <summary>Gets the selected compact-grid maximum error.</summary>
        internal float Maximum { get; }
        /// <summary>Gets the witness compact-grid p95 error.</summary>
        internal float WitnessP95 { get; }
        /// <summary>Gets the witness compact-grid maximum error.</summary>
        internal float WitnessMaximum { get; }
        /// <summary>Gets whether high-roughness compensation improves the baseline.</summary>
        internal bool Improves { get; }
        /// <summary>Gets whether every frozen fit threshold passes.</summary>
        internal bool Stable => CoefficientDelta <= 0.0005f && GainDelta <= 0.0005f && P95 <= 0.0095f && Maximum <= 0.0195f && Math.Abs(P95 - WitnessP95) <= 0.0005f && Math.Abs(Maximum - WitnessMaximum) <= 0.0005f && Improves;
    }
    /// <summary>Stores a branch's raw adaptive and fit evidence.</summary>
    internal sealed class AdaptiveBranch { internal AdaptiveBranch(AdaptiveEvidence[] values, AdaptiveFit fit, bool passes, bool stressStable) { Values = values; Fit = fit; Passes = passes; StressStable = stressStable; } internal AdaptiveEvidence[] Values { get; } internal AdaptiveFit Fit { get; } internal bool Passes { get; } internal bool StressStable { get; } }
    /// <summary>Stores the selected candidate and complete deterministic branch evidence.</summary>
    /// <summary>Stores one candidate's complete calibration, original-grid, and canonical selection evidence.</summary>
    internal sealed class AdaptiveCandidateEvidence
    {
        /// <summary>Initializes immutable evidence for one attempted candidate.</summary>
        internal AdaptiveCandidateEvidence(AdaptiveSettings settings, AdaptiveSelection calibration, AdaptiveSelection original, AdaptiveSelection canonical, string reason)
        {
            Settings = settings;
            Calibration = calibration;
            Original = original;
            Canonical = canonical;
            Reason = reason;
        }

        /// <summary>Gets the candidate's effective settings and caps.</summary>
        internal AdaptiveSettings Settings { get; }
        /// <summary>Gets the combined Original-and-Stress calibration evidence.</summary>
        internal AdaptiveSelection Calibration { get; }
        /// <summary>Gets the independently repeated Original-grid evidence.</summary>
        internal AdaptiveSelection Original { get; }
        /// <summary>Gets canonical fit evidence when the calibration gates permitted it.</summary>
        internal AdaptiveSelection Canonical { get; }
        /// <summary>Gets the deterministic rejection or selection reason.</summary>
        internal string Reason { get; }
    }

    /// <summary>Stores the selected candidate and complete deterministic branch evidence.</summary>
    internal sealed class AdaptiveSelection
    {
        /// <summary>Initializes selected branch evidence and optional candidate-ladder history.</summary>
        internal AdaptiveSelection(AdaptiveSettings protocol, AdaptiveBranch normal, AdaptiveBranch switchBranch, bool selected, bool stressStable, AdaptiveCandidateEvidence[] candidateLadder = null)
        {
            Protocol = protocol;
            Normal = normal;
            Switch = switchBranch;
            IsSelected = selected;
            StressStable = stressStable;
            CandidateLadder = candidateLadder ?? new AdaptiveCandidateEvidence[0];
        }

        /// <summary>Gets the effective selected protocol.</summary>
        internal AdaptiveSettings Protocol { get; }
        /// <summary>Gets normal-epsilon branch evidence.</summary>
        internal AdaptiveBranch Normal { get; }
        /// <summary>Gets switch-epsilon branch evidence.</summary>
        internal AdaptiveBranch Switch { get; }
        /// <summary>Gets whether every frozen selection gate passed.</summary>
        internal bool IsSelected { get; }
        /// <summary>Gets whether the noncanonical calibration gates passed.</summary>
        internal bool StressStable { get; }
        /// <summary>Gets every attempted candidate in deterministic candidate order.</summary>
        internal AdaptiveCandidateEvidence[] CandidateLadder { get; }
    }
}
