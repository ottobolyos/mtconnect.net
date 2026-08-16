// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the MIT license.

using MTConnect.Configurations;
using NUnit.Framework;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MTConnect.AgentModule.MqttRelay.Tests
{
    /// <summary>
    /// Pins the YAML round-trip contract for the SslProtocols field on
    /// <see cref="MqttRelayModuleConfiguration"/>. The MTConnect
    /// module-configuration binder in
    /// <c>AgentApplicationConfiguration.GetConfiguration</c> uses
    /// YamlDotNet with the camelCase naming convention; these tests
    /// reproduce that binder shape so a future contributor cannot
    /// change the serialised name (<c>sslProtocols</c>) or the
    /// list-of-strings representation without breaking a pinned test.
    /// </summary>
    [TestFixture]
    public class MqttRelayModuleConfigurationSerializationTests
    {
        private static (ISerializer Serializer, IDeserializer Deserializer) BuildBinderPair()
        {
            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            return (serializer, deserializer);
        }

        /// <summary>Pins the default: round-trip through YAML preserves the SslProtocols default list unchanged.</summary>
        [Test]
        public void Default_configuration_round_trips_SslProtocols_via_yaml()
        {
            var original = new MqttRelayModuleConfiguration();
            var (serializer, deserializer) = BuildBinderPair();

            var yaml = serializer.Serialize(original);
            var roundTripped = deserializer.Deserialize<MqttRelayModuleConfiguration>(yaml);

            Assert.That(roundTripped.SslProtocols, Is.Not.Null);
            CollectionAssert.AreEqual(original.SslProtocols, roundTripped.SslProtocols);
        }

        /// <summary>Pins the serialised field name: the property surfaces as camelCase 'sslProtocols' in YAML - the shape the module-configuration binder expects.</summary>
        [Test]
        public void SslProtocols_serialises_as_camelCase_field_name()
        {
            var configuration = new MqttRelayModuleConfiguration();
            var (serializer, _) = BuildBinderPair();

            var yaml = serializer.Serialize(configuration);

            Assert.That(yaml, Does.Contain("sslProtocols:"),
                "The binder uses camelCase; the field name must serialise as 'sslProtocols'.");
        }

        /// <summary>Pins that a user-authored YAML list of protocol names deserialises correctly and is unchanged from the input.</summary>
        [Test]
        public void User_authored_yaml_list_deserialises_to_expected_string_list()
        {
            var (_, deserializer) = BuildBinderPair();
            var yaml = "sslProtocols:\n  - Tls12\n  - Tls13\n";

            var configuration = deserializer.Deserialize<MqttRelayModuleConfiguration>(yaml);

            Assert.That(configuration.SslProtocols, Is.Not.Null);
            CollectionAssert.AreEqual(new[] { "Tls12", "Tls13" }, configuration.SslProtocols);
        }

        /// <summary>Pins that opting into a single protocol via a one-entry list is preserved end-to-end.</summary>
        [Test]
        public void Single_entry_list_round_trips_via_yaml()
        {
            var original = new MqttRelayModuleConfiguration
            {
                SslProtocols = new System.Collections.Generic.List<string> { "Tls13" }
            };
            var (serializer, deserializer) = BuildBinderPair();

            var yaml = serializer.Serialize(original);
            var roundTripped = deserializer.Deserialize<MqttRelayModuleConfiguration>(yaml);

            Assert.That(roundTripped.SslProtocols.Single(), Is.EqualTo("Tls13"));
        }
    }
}
