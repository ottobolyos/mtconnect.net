// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the MIT license.

using NUnit.Framework;

namespace MTConnect.Tests.XML
{
    /// <summary>
    /// Pins the namespace-to-version dispatch in <c>MTConnectVersion.GetByNamespace</c>,
    /// in particular that v2.6/v2.7 resolve to their own version rather than falling
    /// through, and that any unrecognized namespace (including a future spec version
    /// this build has not been rebuilt against) falls through to
    /// <see cref="MTConnectVersions.Max"/> instead of an older reader path or an
    /// empty <see cref="System.Version"/>.
    /// </summary>
    [TestFixture]
    public class MTConnectVersionTests
    {
        /// <summary>The v2.6 Streams namespace resolves to <see cref="MTConnectVersions.Version26"/>.</summary>
        [Test]
        public void GetByNamespace_Version26Namespace_ResolvesToVersion26()
        {
            var version = MTConnectVersion.GetByNamespace("urn:mtconnect.org:MTConnectStreams:2.6");
            Assert.That(version, Is.EqualTo(MTConnectVersions.Version26));
        }

        /// <summary>The v2.7 Streams namespace resolves to <see cref="MTConnectVersions.Version27"/>.</summary>
        [Test]
        public void GetByNamespace_Version27Namespace_ResolvesToVersion27()
        {
            var version = MTConnectVersion.GetByNamespace("urn:mtconnect.org:MTConnectStreams:2.7");
            Assert.That(version, Is.EqualTo(MTConnectVersions.Version27));
        }

        /// <summary>
        /// A namespace one minor version ahead of the newest enumerated reader (e.g. a
        /// future v99.9 the library has not been rebuilt against) falls through to
        /// <see cref="MTConnectVersions.Max"/> rather than to an older reader path or an
        /// empty <see cref="System.Version"/>.
        /// </summary>
        [Test]
        public void GetByNamespace_UnrecognizedFutureNamespace_FallsThroughToMax()
        {
            var version = MTConnectVersion.GetByNamespace("urn:mtconnect.org:MTConnectStreams:99.9");
            Assert.That(version, Is.EqualTo(MTConnectVersions.Max));
        }

        /// <summary>A <see langword="null"/> namespace falls through to <see cref="MTConnectVersions.Max"/>.</summary>
        [Test]
        public void GetByNamespace_NullNamespace_FallsThroughToMax()
        {
            var version = MTConnectVersion.GetByNamespace(null);
            Assert.That(version, Is.EqualTo(MTConnectVersions.Max));
        }
    }
}
