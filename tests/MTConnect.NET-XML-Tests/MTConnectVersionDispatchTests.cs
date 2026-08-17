// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the MIT license.

// Pins the fix for the `MTConnectVersion.GetByNamespace` dispatch-chain
// omission: the switch capped out at `Namespaces.Version25.Match(ns)` and
// fell through to `return new Version()` (empty, "0.0") for any namespace
// declared by a document newer than v2.5 - even though `Namespaces.Version26`
// / `Namespaces.Version27` and `MTConnectVersions.Version26` /
// `MTConnectVersions.Version27` both already existed. A document declaring
// `urn:mtconnect.org:MTConnectStreams:2.7` (or `:2.6`) therefore resolved to
// an empty version instead of its real one.
//
// Fix (libraries/MTConnect.NET-XML/MTConnectVersion.cs):
//   - Prepended `Namespaces.Version27.Match(ns)` / `Namespaces.Version26.Match(ns)`
//     branches ahead of `Namespaces.Version25.Match(ns)`, so v2.6/v2.7
//     namespaces resolve highest-first, mirroring the existing pattern.
//   - Changed the fallback from `new Version()` to `MTConnectVersions.Max`
//     so an unrecognised namespace defaults to the latest supported release
//     rather than an empty version.
//
// `MTConnectVersion` and `Namespaces` are `internal` to
// MTConnect.NET-XML; this fixture reaches them via the assembly's
// `InternalsVisibleTo` grant to MTConnect.NET-XML-Tests.

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MTConnect.Tests.XML
{
    /// <summary>Pins the namespace-to-version dispatch chain in <see cref="MTConnectVersion.GetByNamespace"/>.</summary>
    [TestFixture]
    public class MTConnectVersionDispatchTests
    {
        /// <summary>Every currently-declared MTConnect Devices namespace paired with the version it must resolve to.</summary>
        /// <returns>The (namespace, expected version) pairs.</returns>
        public static IEnumerable<TestCaseData> DevicesNamespacesByVersion()
        {
            yield return new TestCaseData(Namespaces.Version10.Devices, MTConnectVersions.Version10).SetName("GetByNamespace_resolves_Devices_1_0");
            yield return new TestCaseData(Namespaces.Version11.Devices, MTConnectVersions.Version11).SetName("GetByNamespace_resolves_Devices_1_1");
            yield return new TestCaseData(Namespaces.Version12.Devices, MTConnectVersions.Version12).SetName("GetByNamespace_resolves_Devices_1_2");
            yield return new TestCaseData(Namespaces.Version13.Devices, MTConnectVersions.Version13).SetName("GetByNamespace_resolves_Devices_1_3");
            yield return new TestCaseData(Namespaces.Version14.Devices, MTConnectVersions.Version14).SetName("GetByNamespace_resolves_Devices_1_4");
            yield return new TestCaseData(Namespaces.Version15.Devices, MTConnectVersions.Version15).SetName("GetByNamespace_resolves_Devices_1_5");
            yield return new TestCaseData(Namespaces.Version16.Devices, MTConnectVersions.Version16).SetName("GetByNamespace_resolves_Devices_1_6");
            yield return new TestCaseData(Namespaces.Version17.Devices, MTConnectVersions.Version17).SetName("GetByNamespace_resolves_Devices_1_7");
            yield return new TestCaseData(Namespaces.Version18.Devices, MTConnectVersions.Version18).SetName("GetByNamespace_resolves_Devices_1_8");
            yield return new TestCaseData(Namespaces.Version20.Devices, MTConnectVersions.Version20).SetName("GetByNamespace_resolves_Devices_2_0");
            yield return new TestCaseData(Namespaces.Version21.Devices, MTConnectVersions.Version21).SetName("GetByNamespace_resolves_Devices_2_1");
            yield return new TestCaseData(Namespaces.Version22.Devices, MTConnectVersions.Version22).SetName("GetByNamespace_resolves_Devices_2_2");
            yield return new TestCaseData(Namespaces.Version23.Devices, MTConnectVersions.Version23).SetName("GetByNamespace_resolves_Devices_2_3");
            yield return new TestCaseData(Namespaces.Version24.Devices, MTConnectVersions.Version24).SetName("GetByNamespace_resolves_Devices_2_4");
            yield return new TestCaseData(Namespaces.Version25.Devices, MTConnectVersions.Version25).SetName("GetByNamespace_resolves_Devices_2_5");
            yield return new TestCaseData(Namespaces.Version26.Devices, MTConnectVersions.Version26).SetName("GetByNamespace_resolves_Devices_2_6");
            yield return new TestCaseData(Namespaces.Version27.Devices, MTConnectVersions.Version27).SetName("GetByNamespace_resolves_Devices_2_7");
        }

        /// <summary>Every currently-declared v2.6/v2.7 namespace (Assets, Devices, Error, Streams) paired with the version it must resolve to.</summary>
        /// <returns>The (namespace, expected version) pairs.</returns>
        public static IEnumerable<TestCaseData> V26AndV27NamespacesByKind()
        {
            yield return new TestCaseData(Namespaces.Version26.Assets, MTConnectVersions.Version26).SetName("GetByNamespace_resolves_Assets_2_6");
            yield return new TestCaseData(Namespaces.Version26.Devices, MTConnectVersions.Version26).SetName("GetByNamespace_resolves_Devices_2_6_ByKind");
            yield return new TestCaseData(Namespaces.Version26.Error, MTConnectVersions.Version26).SetName("GetByNamespace_resolves_Error_2_6");
            yield return new TestCaseData(Namespaces.Version26.Streams, MTConnectVersions.Version26).SetName("GetByNamespace_resolves_Streams_2_6");

            yield return new TestCaseData(Namespaces.Version27.Assets, MTConnectVersions.Version27).SetName("GetByNamespace_resolves_Assets_2_7");
            yield return new TestCaseData(Namespaces.Version27.Devices, MTConnectVersions.Version27).SetName("GetByNamespace_resolves_Devices_2_7_ByKind");
            yield return new TestCaseData(Namespaces.Version27.Error, MTConnectVersions.Version27).SetName("GetByNamespace_resolves_Error_2_7");
            yield return new TestCaseData(Namespaces.Version27.Streams, MTConnectVersions.Version27).SetName("GetByNamespace_resolves_Streams_2_7");
        }

        /// <summary>Pins that every currently-declared Devices namespace resolves to its matching version.</summary>
        /// <param name="ns">The namespace under test.</param>
        /// <param name="expected">The version <paramref name="ns"/> must resolve to.</param>
        [TestCaseSource(nameof(DevicesNamespacesByVersion))]
        public void GetByNamespace_returns_matching_version_for_every_declared_namespace(string ns, Version expected)
        {
            var actual = MTConnectVersion.GetByNamespace(ns);
            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>Pins that v2.6 and v2.7 resolve correctly for every document kind (Assets, Devices, Error, Streams), not just Devices.</summary>
        /// <param name="ns">The namespace under test.</param>
        /// <param name="expected">The version <paramref name="ns"/> must resolve to.</param>
        [TestCaseSource(nameof(V26AndV27NamespacesByKind))]
        public void GetByNamespace_returns_matching_version_for_v26_and_v27_across_document_kinds(string ns, Version expected)
        {
            var actual = MTConnectVersion.GetByNamespace(ns);
            Assert.That(actual, Is.EqualTo(expected));
        }

        // Regression guard for the specific bug: before the fix, a v2.7
        // namespace fell all the way through the chain (Version25 was the
        // highest branch present) and returned `new Version()` - equal to
        // "0.0", not `MTConnectVersions.Version27`.
        /// <summary>Pins that a v2.7 namespace does not fall through to an empty version.</summary>
        [Test]
        public void GetByNamespace_v27_namespace_does_not_fall_through_to_empty_version()
        {
            var actual = MTConnectVersion.GetByNamespace(Namespaces.Version27.Streams);
            Assert.That(actual, Is.Not.EqualTo(new Version()));
            Assert.That(actual, Is.EqualTo(MTConnectVersions.Version27));
        }

        /// <summary>Pins that an unrecognised namespace defaults to the latest supported version rather than an empty one.</summary>
        [Test]
        public void GetByNamespace_unknown_namespace_defaults_to_Max()
        {
            var actual = MTConnectVersion.GetByNamespace("urn:mtconnect.org:MTConnectStreams:99.9");
            Assert.That(actual, Is.EqualTo(MTConnectVersions.Max));
            Assert.That(actual, Is.Not.EqualTo(new Version()));
        }

        /// <summary>Pins that a <see langword="null"/> namespace also defaults to the latest supported version rather than an empty one.</summary>
        [Test]
        public void GetByNamespace_null_namespace_defaults_to_Max()
        {
            var actual = MTConnectVersion.GetByNamespace(null);
            Assert.That(actual, Is.EqualTo(MTConnectVersions.Max));
        }
    }
}
