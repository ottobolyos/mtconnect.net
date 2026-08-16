// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the MIT license.

using MTConnect.Configurations;
using NUnit.Framework;
using System.Collections.Generic;
using System.Security.Authentication;

namespace MTConnect.AgentModule.MqttRelay.Tests
{
    /// <summary>
    /// Pins the SslProtocols-resolution contract for the MQTT relay
    /// module. The resolver turns the user-supplied
    /// <see cref="MqttRelayModuleConfiguration.SslProtocols"/> list
    /// into a bitwise-OR'd <see cref="SslProtocols"/> value the MQTT
    /// client stack accepts, and validates the input so a
    /// misconfiguration surfaces at module load rather than as a
    /// silent downgrade at connect time.
    /// </summary>
    [TestFixture]
    public class MqttRelayTlsProtocolResolverTests
    {
        /// <summary>Pins the default: a fresh MqttRelayModuleConfiguration ships with Tls12 (and Tls13 on frameworks that expose it) and the resolver accepts that default without error.</summary>
        [Test]
        public void Default_configuration_resolves_to_a_nonempty_bitmask_that_includes_Tls12()
        {
            var configuration = new MqttRelayModuleConfiguration();

            var resolved = MqttRelayTlsProtocolResolver.Resolve(configuration.SslProtocols);

            Assert.That(resolved, Is.Not.EqualTo(SslProtocols.None));
            Assert.That((resolved & SslProtocols.Tls12), Is.EqualTo(SslProtocols.Tls12),
                "The default protocol set must include Tls12.");
        }

#if NET48 || NET5_0_OR_GREATER
        /// <summary>Pins the modern-runtime default: on target frameworks where SslProtocols.Tls13 is defined, the shipped default includes Tls13 too.</summary>
        [Test]
        public void Default_configuration_includes_Tls13_on_modern_runtimes()
        {
            var configuration = new MqttRelayModuleConfiguration();

            var resolved = MqttRelayTlsProtocolResolver.Resolve(configuration.SslProtocols);

            Assert.That((resolved & SslProtocols.Tls13), Is.EqualTo(SslProtocols.Tls13),
                "On this framework the default protocol set must include Tls13.");
        }
#endif

        /// <summary>Pins explicit Tls12-only opt-in: user narrows the default to 1.2, resolver returns Tls12 alone.</summary>
        [Test]
        public void Explicit_Tls12_only_resolves_to_Tls12_bitmask()
        {
            var resolved = MqttRelayTlsProtocolResolver.Resolve(new List<string> { "Tls12" });

            Assert.That(resolved, Is.EqualTo(SslProtocols.Tls12));
        }

#if NET48 || NET5_0_OR_GREATER
        /// <summary>Pins explicit Tls13-only opt-in: user opts out of 1.2, resolver returns Tls13 alone.</summary>
        [Test]
        public void Explicit_Tls13_only_resolves_to_Tls13_bitmask()
        {
            var resolved = MqttRelayTlsProtocolResolver.Resolve(new List<string> { "Tls13" });

            Assert.That(resolved, Is.EqualTo(SslProtocols.Tls13));
        }

        /// <summary>Pins multi-protocol OR: user lists both 1.2 and 1.3, resolver returns the bitwise OR.</summary>
        [Test]
        public void Explicit_Tls12_plus_Tls13_resolves_to_bitwise_OR()
        {
            var resolved = MqttRelayTlsProtocolResolver.Resolve(new List<string> { "Tls12", "Tls13" });

            Assert.That(resolved, Is.EqualTo(SslProtocols.Tls12 | SslProtocols.Tls13));
        }
#endif

        /// <summary>Pins case-insensitive parse: user writes lowercase / mixed-case, resolver still matches the enum member.</summary>
        [Test]
        public void Protocol_names_parse_case_insensitively()
        {
            var resolvedLower = MqttRelayTlsProtocolResolver.Resolve(new List<string> { "tls12" });
            var resolvedMixed = MqttRelayTlsProtocolResolver.Resolve(new List<string> { "TLS12" });

            Assert.That(resolvedLower, Is.EqualTo(SslProtocols.Tls12));
            Assert.That(resolvedMixed, Is.EqualTo(SslProtocols.Tls12));
        }

        /// <summary>Pins validation: null list is a configuration error, not a silent 'no TLS' or 'default TLS' fallback.</summary>
        [Test]
        public void Null_list_throws_configuration_error()
        {
            Assert.Throws<MqttRelayConfigurationException>(
                () => MqttRelayTlsProtocolResolver.Resolve(null));
        }

        /// <summary>Pins validation: opt-out of all versions is a configuration error.</summary>
        [Test]
        public void Empty_list_throws_configuration_error()
        {
            Assert.Throws<MqttRelayConfigurationException>(
                () => MqttRelayTlsProtocolResolver.Resolve(new List<string>()));
        }

        /// <summary>Pins validation: unknown protocol name surfaces a clear error with the name that failed to parse.</summary>
        [Test]
        public void Unknown_protocol_name_throws_configuration_error()
        {
            var ex = Assert.Throws<MqttRelayConfigurationException>(
                () => MqttRelayTlsProtocolResolver.Resolve(new List<string> { "Tls99" }));

            Assert.That(ex.Message, Does.Contain("Tls99"));
        }

        /// <summary>Pins validation: a blank / whitespace entry is a configuration error.</summary>
        [Test]
        public void Blank_entry_throws_configuration_error()
        {
            Assert.Throws<MqttRelayConfigurationException>(
                () => MqttRelayTlsProtocolResolver.Resolve(new List<string> { "   " }));
        }

        /// <summary>Pins validation: 'None' is not a permitted opt-out; the surface accepts only concrete protocol names.</summary>
        [Test]
        public void None_entry_throws_configuration_error()
        {
            Assert.Throws<MqttRelayConfigurationException>(
                () => MqttRelayTlsProtocolResolver.Resolve(new List<string> { "None" }));
        }

        /// <summary>Pins validation: numeric input is rejected. Users must supply enum names so an accidental integer shift cannot silently rebind a version.</summary>
        [Test]
        public void Numeric_entry_throws_configuration_error()
        {
            Assert.Throws<MqttRelayConfigurationException>(
                () => MqttRelayTlsProtocolResolver.Resolve(new List<string> { "3072" }));
        }
    }
}
