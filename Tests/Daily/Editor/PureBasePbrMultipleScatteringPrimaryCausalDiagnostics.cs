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

// Defines bounded paired observer-disabled and observer-enabled causal diagnostics with fail-closed availability.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace PureBase.Tests.Daily
{
    /// <summary>Identifies the selection-budget mode for one causal primary invocation.</summary>
    internal enum PrimaryCausalMode
    {
        /// <summary>Uses a fresh finite selection budget of 512 reservations.</summary>
        Finite512,
        /// <summary>Runs without a selection reservation budget.</summary>
        NoSelectionBudget
    }

    /// <summary>Identifies the baseline terminal category expected for a fixed causal case.</summary>
    internal enum PrimaryCausalBaselineState
    {
        /// <summary>The primary run completes within its supported limits.</summary>
        Accepted,
        /// <summary>The next finite-budget reservation is rejected before scalar work.</summary>
        BudgetExhausted,
        /// <summary>An intrinsic recursive depth stop terminates the primary run.</summary>
        DepthCap,
        /// <summary>An evaluation limit terminates the primary run before a depth terminal.</summary>
        EvaluationCap,
        /// <summary>A global accumulated-error limit terminates the primary run.</summary>
        GlobalError,
        /// <summary>A primary execution fault terminates the primary run.</summary>
        Fault,
        /// <summary>A primary execution timeout terminates the primary run.</summary>
        Timeout,
        /// <summary>The terminal category is not eligible to authorize a repair.</summary>
        Other
    }

    /// <summary>Identifies the separate selection reservation observation for one attempt.</summary>
    internal enum ReservationObservationState
    {
        /// <summary>The finite reservation was accepted.</summary>
        Accepted,
        /// <summary>The finite reservation was rejected before scalar-kernel work.</summary>
        Rejected,
        /// <summary>The run has no selection budget and therefore no reservation event.</summary>
        NotApplicable
    }

    /// <summary>Identifies whether causal observation is currently available from the primary runner.</summary>
    internal enum PrimaryCausalAvailability
    {
        /// <summary>The primary path does not yet expose causal evidence.</summary>
        Unavailable,
        /// <summary>The primary path supplied complete causal evidence.</summary>
        Available
    }

    /// <summary>Stores observable cache and artifact identities before and after causal observation.</summary>
    internal readonly struct PrimaryCausalObserverIsolationSnapshot
    {
        /// <summary>Initializes exact identities or null when an observer-only path cannot observe them.</summary>
        internal PrimaryCausalObserverIsolationSnapshot(ulong? preCacheDigest, ulong? postCacheDigest, ulong? preArtifactDigest, ulong? postArtifactDigest)
        {
            PreCacheDigest = preCacheDigest; PostCacheDigest = postCacheDigest; PreArtifactDigest = preArtifactDigest; PostArtifactDigest = postArtifactDigest;
        }

        /// <summary>Gets the cache identity before observation, or null when unavailable.</summary>
        internal ulong? PreCacheDigest { get; }
        /// <summary>Gets the cache identity after observation, or null when unavailable.</summary>
        internal ulong? PostCacheDigest { get; }
        /// <summary>Gets the artifact identity before observation, or null when unavailable.</summary>
        internal ulong? PreArtifactDigest { get; }
        /// <summary>Gets the artifact identity after observation, or null when unavailable.</summary>
        internal ulong? PostArtifactDigest { get; }
        /// <summary>Gets whether every required isolation identity was actually observed.</summary>
        internal bool IsObserved => PreCacheDigest.HasValue && PostCacheDigest.HasValue && PreArtifactDigest.HasValue && PostArtifactDigest.HasValue;
    }

    /// <summary>Stores the complete result returned by the existing capture-free primary entry point.</summary>
    internal readonly struct PrimaryCausalDirectResult
    {
        /// <summary>Initializes every observable field from one direct primary result.</summary>
        internal PrimaryCausalDirectResult(double estimate, double error, double tolerance, int evaluations, int panels, int depth, string diagnostic)
        {
            Estimate = estimate; Error = error; Tolerance = tolerance; Evaluations = evaluations; Panels = panels; Depth = depth; Diagnostic = diagnostic;
        }

        /// <summary>Gets the direct estimated integral.</summary>
        internal double Estimate { get; }
        /// <summary>Gets the direct retained error estimate.</summary>
        internal double Error { get; }
        /// <summary>Gets the direct final tolerance.</summary>
        internal double Tolerance { get; }
        /// <summary>Gets the direct completed evaluation count.</summary>
        internal int Evaluations { get; }
        /// <summary>Gets the direct panel count.</summary>
        internal int Panels { get; }
        /// <summary>Gets the direct maximum recursion depth.</summary>
        internal int Depth { get; }
        /// <summary>Gets the direct terminal diagnostic, or null for acceptance.</summary>
        internal string Diagnostic { get; }
    }

    /// <summary>Identifies the outcome of an independent repair-authorization decision.</summary>
    internal enum PrimaryCausalGateResult
    {
        /// <summary>The evidence is internally valid but does not indicate a repair.</summary>
        NoRepair,
        /// <summary>The complete intrinsic depth evidence authorizes a later terminal split repair.</summary>
        AuthorizeTerminalSplitRepair,
        /// <summary>Evidence is incomplete, inconsistent, or ineligible and must fail closed.</summary>
        Reject
    }

    /// <summary>Stores the immutable fixed input and expected state for one causal invocation.</summary>
    internal readonly struct PrimaryCausalInvocation
    {
        /// <summary>Initializes one exact-bit primary input and its expected terminal category.</summary>
        internal PrimaryCausalInvocation(int coordinateIndex, double p, double ndotV, bool switchBranch, PrimaryCausalBaselineState baselineState)
        {
            CoordinateIndex = coordinateIndex; P = p; NdotV = ndotV; SwitchBranch = switchBranch; BaselineState = baselineState;
        }

        /// <summary>Gets the frozen source-order coordinate index.</summary>
        internal int CoordinateIndex { get; }
        /// <summary>Gets the exact roughness coordinate.</summary>
        internal double P { get; }
        /// <summary>Gets the exact view cosine coordinate.</summary>
        internal double NdotV { get; }
        /// <summary>Gets whether the switch epsilon branch is selected.</summary>
        internal bool SwitchBranch { get; }
        /// <summary>Gets the expected baseline terminal category.</summary>
        internal PrimaryCausalBaselineState BaselineState { get; }
    }

    /// <summary>Stores one mode-independent scalar-kernel attempt identity before reservation.</summary>
    internal readonly struct PrimaryCausalAttemptCore
    {
        /// <summary>Initializes the complete mode-independent identity for one scalar attempt.</summary>
        internal PrimaryCausalAttemptCore(int sequence, bool switchBranch, double p, double ndotV, string axis, double psi, double eta, double sample, double left, double right, int depth, int partitionLine, int partitionIndex, double preTransformEta, double rawX, double jacobian)
        {
            Sequence = sequence; SwitchBranch = switchBranch; P = p; NdotV = ndotV; Axis = axis; Psi = psi; Eta = eta; Sample = sample; Left = left; Right = right; Depth = depth; PartitionLine = partitionLine; PartitionIndex = partitionIndex; PreTransformEta = preTransformEta; RawX = rawX; Jacobian = jacobian;
        }

        /// <summary>Gets the run-local one-based observation sequence.</summary>
        internal int Sequence { get; }
        /// <summary>Gets the exact branch identity.</summary>
        internal bool SwitchBranch { get; }
        /// <summary>Gets the exact roughness input.</summary>
        internal double P { get; }
        /// <summary>Gets the exact view cosine input.</summary>
        internal double NdotV { get; }
        /// <summary>Gets the sample coordinate system.</summary>
        internal string Axis { get; }
        /// <summary>Gets the exact outer psi coordinate.</summary>
        internal double Psi { get; }
        /// <summary>Gets the exact scalar-kernel eta coordinate.</summary>
        internal double Eta { get; }
        /// <summary>Gets eta or raw x according to the sample coordinate system.</summary>
        internal double Sample { get; }
        /// <summary>Gets the exact left panel bound in the active coordinate system.</summary>
        internal double Left { get; }
        /// <summary>Gets the exact right panel bound in the active coordinate system.</summary>
        internal double Right { get; }
        /// <summary>Gets the recursion depth at attempt entry.</summary>
        internal int Depth { get; }
        /// <summary>Gets the initial eta partition line.</summary>
        internal int PartitionLine { get; }
        /// <summary>Gets the initial eta partition index.</summary>
        internal int PartitionIndex { get; }
        /// <summary>Gets the eta before an eta-x transformation.</summary>
        internal double PreTransformEta { get; }
        /// <summary>Gets the raw x coordinate for an eta-x transformation.</summary>
        internal double RawX { get; }
        /// <summary>Gets the deterministic raw-x Jacobian identity.</summary>
        internal double Jacobian { get; }
    }

    /// <summary>Stores the mode-specific reservation outcome separately from an attempt core.</summary>
    internal readonly struct ReservationObservation
    {
        /// <summary>Initializes one reservation observation without changing numerical identity.</summary>
        internal ReservationObservation(int sequence, ReservationObservationState state, int used, int limit, PrimaryCausalAttemptCore? core = null)
        {
            Sequence = sequence; State = state; Used = used; Limit = limit; Core = core;
        }

        /// <summary>Gets the associated attempt sequence.</summary>
        internal int Sequence { get; }
        /// <summary>Gets the mode-specific reservation state.</summary>
        internal ReservationObservationState State { get; }
        /// <summary>Gets the used finite reservation count after the observation.</summary>
        internal int Used { get; }
        /// <summary>Gets the finite reservation limit, or zero when not applicable.</summary>
        internal int Limit { get; }
        /// <summary>Gets the identified scalar core, including one rejected reservation that did not start work.</summary>
        internal PrimaryCausalAttemptCore? Core { get; }
    }

    /// <summary>Stores bounded causal observation surfaces or an explicit unavailable result.</summary>
    internal sealed class PrimaryCausalRun
    {
        /// <summary>Limits retained observations to the required first 513 attempts.</summary>
        private const int MaximumRetainedAttempts = 513;
        /// <summary>Stores the bounded immutable attempt-core prefix.</summary>
        private readonly PrimaryCausalAttemptCore[] attempts;
        /// <summary>Stores bounded mode-specific reservation observations.</summary>
        private readonly ReservationObservation[] reservations;
        /// <summary>Stores bounded invocation lineage records.</summary>
        private readonly PrimaryCausalLineageRecord[] lineage;
        /// <summary>Stores immutable terminal invocation identities.</summary>
        private readonly PrimaryCausalTerminalInvocation[] terminals;
        /// <summary>Stores explicit Psi-to-Eta or Psi-to-Eta-x invocation edges.</summary>
        private readonly PrimaryCausalCrossAxisEdge[] crossAxisEdges;
        /// <summary>Stores bounded online aggregates without raw sample retention.</summary>
        private readonly PrimaryCausalAggregate[] aggregates;
        /// <summary>Stores terminal ancestors independently from the attempt prefix.</summary>
        private readonly PrimaryCausalLineageRecord[] terminalAncestorChain;
        /// <summary>Stores the finite pre-reservation scalar core independently from reservation status.</summary>
        private readonly PrimaryCausalAttemptCore? preReservationAttemptCore;

        /// <summary>Initializes a bounded causal run result.</summary>
        internal PrimaryCausalRun(PrimaryCausalInvocation invocation, PrimaryCausalMode mode, PrimaryCausalAvailability availability, string unavailableReason, PrimaryCausalAttemptCore[] attempts, ReservationObservation[] reservations, PrimaryCausalLineageRecord[] lineage, PrimaryCausalTerminalInvocation[] terminals)
            : this(invocation, mode, availability, unavailableReason, attempts, reservations, lineage, terminals, Array.Empty<PrimaryCausalCrossAxisEdge>(), Array.Empty<PrimaryCausalAggregate>(), Array.Empty<PrimaryCausalLineageRecord>(), null, invocation.BaselineState, null, 0UL, null, null, null) { }

        /// <summary>Initializes complete bounded causal evidence with retained results and observer state.</summary>
        internal PrimaryCausalRun(PrimaryCausalInvocation invocation, PrimaryCausalMode mode, PrimaryCausalAvailability availability, string unavailableReason, PrimaryCausalAttemptCore[] attempts, ReservationObservation[] reservations, PrimaryCausalLineageRecord[] lineage, PrimaryCausalTerminalInvocation[] terminals, PrimaryCausalCrossAxisEdge[] crossAxisEdges, PrimaryCausalAggregate[] aggregates, PrimaryCausalLineageRecord[] terminalAncestorChain, PrimaryCausalContradictionTrace firstContradictionTrace, PrimaryCausalBaselineState terminalState, PrimaryCausalCompleteResult? completeResult, ulong modeCommonCoreDigest, ulong? preObserverStateDigest, ulong? postObserverStateDigest, PrimaryCausalObserverDisabledWitness observerDisabledWitness, PrimaryCausalObserverIsolationSnapshot? observerIsolationSnapshot = null, PrimaryCausalDirectResult? directResult = null, PrimaryCausalAttemptCore? preReservationAttemptCore = null)
        {
            if (availability == PrimaryCausalAvailability.Available && (!completeResult.HasValue || modeCommonCoreDigest == 0UL || !preObserverStateDigest.HasValue || !postObserverStateDigest.HasValue || !observerIsolationSnapshot.HasValue || !observerIsolationSnapshot.Value.IsObserved)) throw new ArgumentException("Available causal evidence requires complete results and observed isolation identities.");
            Invocation = invocation; Mode = mode; Availability = availability; UnavailableReason = unavailableReason; this.attempts = CopyBounded(attempts); this.reservations = CopyBounded(reservations); this.lineage = CopyBounded(lineage); this.terminals = Copy(terminals); this.crossAxisEdges = Copy(crossAxisEdges); this.aggregates = Copy(aggregates); this.terminalAncestorChain = Copy(terminalAncestorChain); this.preReservationAttemptCore = preReservationAttemptCore; FirstContradictionTrace = firstContradictionTrace; TerminalState = terminalState; CompleteResult = completeResult; ModeCommonCoreDigest = modeCommonCoreDigest; PreObserverStateDigest = preObserverStateDigest; PostObserverStateDigest = postObserverStateDigest; ObserverDisabledWitness = observerDisabledWitness; ObserverIsolationSnapshot = observerIsolationSnapshot; DirectResult = directResult;
        }

        /// <summary>Gets the immutable requested primary invocation.</summary>
        internal PrimaryCausalInvocation Invocation { get; }
        /// <summary>Gets the selected budget mode.</summary>
        internal PrimaryCausalMode Mode { get; }
        /// <summary>Gets whether causal evidence was observed.</summary>
        internal PrimaryCausalAvailability Availability { get; }
        /// <summary>Gets the explicit reason evidence is unavailable.</summary>
        internal string UnavailableReason { get; }
        /// <summary>Gets the bounded attempt-core prefix.</summary>
        internal IReadOnlyList<PrimaryCausalAttemptCore> Attempts => attempts;
        /// <summary>Gets reservation observations separate from attempt identity.</summary>
        internal IReadOnlyList<ReservationObservation> Reservations => reservations;
        /// <summary>Gets bounded causal invocation lineage.</summary>
        internal IReadOnlyList<PrimaryCausalLineageRecord> Lineage => lineage;
        /// <summary>Gets terminal identities eligible for observer-disabled matching.</summary>
        internal IReadOnlyList<PrimaryCausalTerminalInvocation> Terminals => terminals;
        /// <summary>Gets explicit cross-axis edges that are never labeled as same-axis children.</summary>
        internal IReadOnlyList<PrimaryCausalCrossAxisEdge> CrossAxisEdges => crossAxisEdges;
        /// <summary>Gets per-axis and per-partition online aggregate summaries.</summary>
        internal IReadOnlyList<PrimaryCausalAggregate> Aggregates => aggregates;
        /// <summary>Gets the retained terminal ancestor chain even after prefix retention fills.</summary>
        internal IReadOnlyList<PrimaryCausalLineageRecord> TerminalAncestorChain => terminalAncestorChain;
        /// <summary>Gets the finite pre-reservation 513th scalar core, or null when not retained.</summary>
        internal PrimaryCausalAttemptCore? PreReservationAttemptCore => preReservationAttemptCore;
        /// <summary>Gets the first exact contradiction trace, or null when none was observed.</summary>
        internal PrimaryCausalContradictionTrace FirstContradictionTrace { get; }
        /// <summary>Gets the observed terminal result independently from the expected baseline.</summary>
        internal PrimaryCausalBaselineState TerminalState { get; }
        /// <summary>Gets the complete retained result required for available causal evidence.</summary>
        internal PrimaryCausalCompleteResult? CompleteResult { get; }
        /// <summary>Gets the digest of the mode-common scalar attempt core.</summary>
        internal ulong ModeCommonCoreDigest { get; }
        /// <summary>Gets primary state identity before observation, or null when unavailable.</summary>
        internal ulong? PreObserverStateDigest { get; }
        /// <summary>Gets primary state identity after observation, or null when unavailable.</summary>
        internal ulong? PostObserverStateDigest { get; }
        /// <summary>Gets immutable observer-disabled terminal evidence when independently captured.</summary>
        internal PrimaryCausalObserverDisabledWitness ObserverDisabledWitness { get; }
        /// <summary>Gets immutable cache and artifact identities around causal observation.</summary>
        internal PrimaryCausalObserverIsolationSnapshot? ObserverIsolationSnapshot { get; }
        /// <summary>Gets the complete capture-free primary result paired with the observed run.</summary>
        internal PrimaryCausalDirectResult? DirectResult { get; }

        /// <summary>Clears a parsed witness that did not uniquely match an observed terminal identity.</summary>
        internal PrimaryCausalRun WithoutObserverDisabledWitness()
        {
            return new PrimaryCausalRun(Invocation, Mode, Availability, UnavailableReason, attempts, reservations, lineage, terminals, crossAxisEdges, aggregates, terminalAncestorChain, FirstContradictionTrace, TerminalState, CompleteResult, ModeCommonCoreDigest, PreObserverStateDigest, PostObserverStateDigest, null, ObserverIsolationSnapshot, DirectResult, preReservationAttemptCore);
        }

        /// <summary>Creates an unavailable identity fixture for parser-only contract tests.</summary>
        internal static PrimaryCausalRun UnavailableForParser(PrimaryCausalBaselineState state, PrimaryCausalTerminalInvocation[] terminals)
        {
            var invocation = new PrimaryCausalInvocation(0, 1.0d, 1.0d, false, state);
            return new PrimaryCausalRun(invocation, PrimaryCausalMode.NoSelectionBudget, PrimaryCausalAvailability.Unavailable, "Parser-only evidence does not establish runtime availability.", Array.Empty<PrimaryCausalAttemptCore>(), Array.Empty<ReservationObservation>(), Array.Empty<PrimaryCausalLineageRecord>(), terminals, Array.Empty<PrimaryCausalCrossAxisEdge>(), Array.Empty<PrimaryCausalAggregate>(), Array.Empty<PrimaryCausalLineageRecord>(), null, state, null, 0UL, null, null, null);
        }

        /// <summary>Copies only the required mode-common attempt prefix.</summary>
        private static T[] CopyBounded<T>(T[] values)
        {
            if (values == null || values.Length == 0) return Array.Empty<T>();
            int count = Math.Min(values.Length, MaximumRetainedAttempts); var copy = new T[count]; Array.Copy(values, copy, count); return copy;
        }

        /// <summary>Copies independently bounded observer surfaces without prefix truncation.</summary>
        private static T[] Copy<T>(T[] values)
        {
            if (values == null || values.Length == 0) return Array.Empty<T>();
            var copy = new T[values.Length]; Array.Copy(values, copy, values.Length); return copy;
        }
    }

    /// <summary>Executes paired observer-disabled and observer-enabled primary diagnostics.</summary>
    internal static class PrimaryCausalDiagnosticRunner
    {
        /// <summary>Returns bounded causal evidence paired with an unchanged observer-disabled primary result.</summary>
        internal static PrimaryCausalRun Run(PrimaryCausalInvocation invocation, PrimaryCausalMode mode)
        {
            AdaptiveSettings settings = FrozenCalibrationASettings(); AdaptiveResult directResult = Integrate(settings, invocation, mode, null); var direct = new PrimaryCausalDirectResult(directResult.Value, directResult.Error, directResult.Tolerance, directResult.Evaluations, directResult.Panels, directResult.Depth, directResult.Diagnostic);
            PrimaryCausalObserverDisabledWitness witness = null; if (directResult.Diagnostic != null) PrimaryCausalDiagnosticParser.TryParseObserverDisabledDepth(directResult.Diagnostic, out witness);
            bool hasPreIdentity = TryCaptureIsolationIdentities(out ulong preCache, out ulong preArtifact); var observer = new PrimaryCausalObserver(invocation); bool hasPreObserverState = TryCaptureObserverStateIdentity(out ulong preObserverState); AdaptiveResult observedResult = Integrate(settings, invocation, mode, observer); bool hasPostObserverState = TryCaptureObserverStateIdentity(out ulong postObserverState); bool hasPostIdentity = TryCaptureIsolationIdentities(out ulong postCache, out ulong postArtifact);
            PrimaryCausalObserverIsolationSnapshot? isolation = hasPreIdentity && hasPostIdentity ? new PrimaryCausalObserverIsolationSnapshot(preCache, postCache, preArtifact, postArtifact) : null;
            PrimaryCausalRun observed = observer.Complete(observedResult, mode, witness, direct, isolation, hasPreObserverState ? preObserverState : (ulong?)null, hasPostObserverState ? postObserverState : (ulong?)null);
            if (directResult.Diagnostic == null || PrimaryCausalDiagnosticParser.TryMatchObserverDisabledDepth(directResult.Diagnostic, observed, out _)) return observed;
            return observed.WithoutObserverDisabledWitness();
        }

        /// <summary>Runs one primary invocation with the mode's independent selection reservation policy.</summary>
        private static AdaptiveResult Integrate(AdaptiveSettings settings, PrimaryCausalInvocation invocation, PrimaryCausalMode mode, PrimaryCausalObserver observer)
        {
            if (mode == PrimaryCausalMode.NoSelectionBudget) return AdaptivePrimary.Integrate(settings, invocation.P, invocation.NdotV, invocation.SwitchBranch, null, default, null, observer);
            var budget = new SelectionExecutionBudget(512); var coordinate = new AdaptiveCoordinate(invocation.P, invocation.NdotV); var context = new SelectionExecutionContext(settings.Name, "causal-direct-parity", invocation.SwitchBranch ? "switch" : "normal", "causal", invocation.CoordinateIndex, coordinate, "primary");
            return AdaptivePrimary.Integrate(settings, coordinate.P, coordinate.V, invocation.SwitchBranch, budget, context, null, observer);
        }

        /// <summary>Creates the frozen calibration-a settings without reading selection state.</summary>
        private static AdaptiveSettings FrozenCalibrationASettings() => new AdaptiveSettings("calibration-a", 0.00004d, 0.0004d, 0.00001d, 0.0001d, 18, 65536, 1000000);

        /// <summary>Captures cache realization and artifact bytes without requesting the lazy cache value.</summary>
        private static bool TryCaptureIsolationIdentities(out ulong cacheDigest, out ulong artifactDigest)
        {
            cacheDigest = 0UL; artifactDigest = 0UL; FieldInfo field = typeof(PureBasePbrMultipleScatteringFurnaceOracle).GetField("SelectionCache", BindingFlags.NonPublic | BindingFlags.Static); var cache = field == null ? null : field.GetValue(null) as Lazy<AdaptiveSelection>;
            if (cache == null) return false;
            cacheDigest = Digest(cache.IsValueCreated ? new byte[] { 1 } : new byte[] { 0 }); string path = AdaptiveProtocol.CanonicalArtifactPath;
            if (!File.Exists(path)) { artifactDigest = Digest(new byte[] { 0 }); return true; }
            artifactDigest = Digest(File.ReadAllBytes(path)); return true;
        }

        /// <summary>Captures the full Unity random generator state without advancing it.</summary>
        private static bool TryCaptureObserverStateIdentity(out ulong digest)
        {
            digest = 0UL; string serialized = UnityEngine.JsonUtility.ToJson(UnityEngine.Random.state);
            if (string.IsNullOrEmpty(serialized)) return false;
            digest = Digest(Encoding.UTF8.GetBytes(serialized)); return true;
        }

        /// <summary>Hashes immutable observed bytes with a nonzero FNV-1a digest.</summary>
        private static ulong Digest(byte[] values)
        {
            ulong hash = 1469598103934665603UL; foreach (byte value in values) hash = (hash ^ value) * 1099511628211UL;
            return hash == 0UL ? 1UL : hash;
        }
    }

    /// <summary>Stores arithmetic fields parsed from one exact observer-disabled depth terminal.</summary>
    internal readonly struct PrimaryCausalDepthEvidence
    {
        /// <summary>Initializes parsed exact depth arithmetic evidence.</summary>
        internal PrimaryCausalDepthEvidence(double coarse, double fine, double inherited, double delta, double absolute, double relative, double error, double limit, double overLimit)
        {
            Coarse = coarse; Fine = fine; Inherited = inherited; Delta = delta; Absolute = absolute; Relative = relative; Error = error; Limit = limit; ErrorOverLimit = overLimit;
        }

        /// <summary>Gets the exact coarse estimate.</summary>
        internal double Coarse { get; }
        /// <summary>Gets the exact fine estimate.</summary>
        internal double Fine { get; }
        /// <summary>Gets the inherited inner error.</summary>
        internal double Inherited { get; }
        /// <summary>Gets the rule delta.</summary>
        internal double Delta { get; }
        /// <summary>Gets the absolute allocation.</summary>
        internal double Absolute { get; }
        /// <summary>Gets the relative allocation.</summary>
        internal double Relative { get; }
        /// <summary>Gets the terminal local error.</summary>
        internal double Error { get; }
        /// <summary>Gets the terminal local limit.</summary>
        internal double Limit { get; }
        /// <summary>Gets the terminal error-over-limit diagnostic.</summary>
        internal double ErrorOverLimit { get; }
    }

    /// <summary>Stores immutable parsed observer-disabled terminal evidence separately from causal records.</summary>
    internal sealed class PrimaryCausalObserverDisabledWitness
    {
        /// <summary>Initializes one parsed witness with its exact unmodified diagnostic text.</summary>
        internal PrimaryCausalObserverDisabledWitness(string rawDiagnostic, PrimaryCausalTerminalInvocation terminal, PrimaryCausalBaselineState category, string decision, PrimaryCausalDepthEvidence arithmetic, PrimaryCausalDecisionOrder? decisionOrder = null)
        {
            RawDiagnostic = rawDiagnostic ?? string.Empty; Terminal = terminal; Category = category; Decision = decision; Arithmetic = arithmetic; DecisionOrder = decisionOrder;
        }

        /// <summary>Gets the exact observer-disabled diagnostic text retained for audit.</summary>
        internal string RawDiagnostic { get; }
        /// <summary>Gets the parsed terminal identity.</summary>
        internal PrimaryCausalTerminalInvocation Terminal { get; }
        /// <summary>Gets the parsed terminal category.</summary>
        internal PrimaryCausalBaselineState Category { get; }
        /// <summary>Gets the parsed terminal decision.</summary>
        internal string Decision { get; }
        /// <summary>Gets the parsed terminal arithmetic.</summary>
        internal PrimaryCausalDepthEvidence Arithmetic { get; }
        /// <summary>Gets the independently retained terminal decision order, or null when unavailable.</summary>
        internal PrimaryCausalDecisionOrder? DecisionOrder { get; }
    }

    /// <summary>Parses only the exact observer-disabled primary depth diagnostic grammar.</summary>
    internal static class PrimaryCausalDiagnosticParser
    {
        /// <summary>Matches the full observer-disabled depth grammar without optional tokens.</summary>
        private static readonly Regex DepthGrammar = new Regex(@"^numerical-limit primary depth axis=(?<axis>[^ ]+) outer=(?<outer>none|[^ ]+) interval=\[(?<left>[^,]+),(?<right>[^\]]+)\] coarse=(?<coarse>[^ ]+) fine=(?<fine>[^ ]+) inheritedInnerError=(?<inherited>[^ ]+) ruleDelta=(?<delta>[^ ]+) absoluteLimit=(?<absolute>[^ ]+) relativeLimit=(?<relative>[^ ]+) error=(?<error>[^ ]+) limit=(?<limit>[^ ]+) errorOverLimit=(?<over>[^ ]+) depth=(?<depth>[^ ]+)$", RegexOptions.CultureInvariant);

        /// <summary>Matches one exact depth-cap terminal identity without testing runtime availability.</summary>
        internal static bool TryMatchObserverDisabledDepth(string text, PrimaryCausalRun run, out PrimaryCausalDepthEvidence evidence)
        {
            evidence = default;
            if (run == null || run.Invocation.BaselineState != PrimaryCausalBaselineState.DepthCap) return false;
            if (!TryParseObserverDisabledDepth(text, out PrimaryCausalObserverDisabledWitness witness)) return false;
            PrimaryCausalTerminalInvocation terminal = witness.Terminal;
            if (MatchingTerminals(run.Terminals, terminal.Axis, terminal.HasOuter, terminal.Outer, terminal.Left, terminal.Right, terminal.Depth) != 1) return false;
            evidence = witness.Arithmetic; return true;
        }

        /// <summary>Parses an immutable observer-disabled witness without consulting causal evidence.</summary>
        internal static bool TryParseObserverDisabledDepth(string text, out PrimaryCausalObserverDisabledWitness witness)
        {
            witness = null; Match match = DepthGrammar.Match(text ?? string.Empty); if (!match.Success) return false;
            if (!TryParseTerminal(match, out string axis, out bool hasOuter, out double outer, out double left, out double right, out int depth)) return false;
            if (!TryParseEvidence(match, out PrimaryCausalDepthEvidence arithmetic)) return false;
            var terminal = new PrimaryCausalTerminalInvocation(axis, hasOuter, outer, left, right, depth);
            witness = new PrimaryCausalObserverDisabledWitness(text ?? string.Empty, terminal, PrimaryCausalBaselineState.DepthCap, "depth-cap", arithmetic); return true;
        }

        /// <summary>Parses and round-trips terminal identity fields exactly.</summary>
        private static bool TryParseTerminal(Match match, out string axis, out bool hasOuter, out double outer, out double left, out double right, out int depth)
        {
            axis = match.Groups["axis"].Value; hasOuter = match.Groups["outer"].Value != "none"; outer = 0.0d; left = 0.0d; right = 0.0d; depth = 0;
            return axis.Length > 0 && (!hasOuter || TryParseFinite(match.Groups["outer"].Value, out outer)) && TryParseFinite(match.Groups["left"].Value, out left) && TryParseFinite(match.Groups["right"].Value, out right) && int.TryParse(match.Groups["depth"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out depth) && depth.ToString(CultureInfo.InvariantCulture) == match.Groups["depth"].Value;
        }

        /// <summary>Parses every required finite arithmetic field from exact grammar groups.</summary>
        private static bool TryParseEvidence(Match match, out PrimaryCausalDepthEvidence evidence)
        {
            evidence = default;
            if (!TryParseFinite(match.Groups["coarse"].Value, out double coarse) || !TryParseFinite(match.Groups["fine"].Value, out double fine) || !TryParseFinite(match.Groups["inherited"].Value, out double inherited) || !TryParseFinite(match.Groups["delta"].Value, out double delta) || !TryParseFinite(match.Groups["absolute"].Value, out double absolute) || !TryParseFinite(match.Groups["relative"].Value, out double relative) || !TryParseFinite(match.Groups["error"].Value, out double error) || !TryParseFinite(match.Groups["limit"].Value, out double limit) || !TryParseFinite(match.Groups["over"].Value, out double overLimit)) return false;
            evidence = new PrimaryCausalDepthEvidence(coarse, fine, inherited, delta, absolute, relative, error, limit, overLimit); return true;
        }

        /// <summary>Counts exact-bit terminal identity matches without selecting an arbitrary duplicate.</summary>
        private static int MatchingTerminals(IReadOnlyList<PrimaryCausalTerminalInvocation> terminals, string axis, bool hasOuter, double outer, double left, double right, int depth)
        {
            int count = 0;
            foreach (PrimaryCausalTerminalInvocation terminal in terminals) if (terminal.Axis == axis && terminal.HasOuter == hasOuter && (!hasOuter || SameBits(terminal.Outer, outer)) && SameBits(terminal.Left, left) && SameBits(terminal.Right, right) && terminal.Depth == depth) count++;
            return count;
        }

        /// <summary>Parses one finite binary64 token and requires its invariant round-trip spelling.</summary>
        private static bool TryParseFinite(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value) && value.ToString("R", CultureInfo.InvariantCulture) == text;
        }

        /// <summary>Compares binary64 values without numeric normalization.</summary>
        private static bool SameBits(double left, double right) => BitConverter.DoubleToInt64Bits(left) == BitConverter.DoubleToInt64Bits(right);
    }


}
