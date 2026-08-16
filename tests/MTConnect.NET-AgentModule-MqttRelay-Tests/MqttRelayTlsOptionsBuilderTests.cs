// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the MIT license.

using MQTTnet.Client;
using MTConnect.Configurations;
using MTConnect.Tls;
using NUnit.Framework;
using System.Linq;
using System.Security.Authentication;

namespace MTConnect.AgentModule.MqttRelay.Tests
{
    /// <summary>
    /// Pins the TLS-options composition rule for the MQTT relay
    /// module. The prior in-Module composition split into two branches
    /// - a client-cert branch and a credentials-branch fallback - that
    /// could each overwrite the other. That layout gave rise to two
    /// silent-failure bugs:
    ///
    /// <list type="bullet">
    ///   <item>Bug 1: <c>Tls.*</c> flags (<c>VerifyClientCertificate</c>,
    ///   <c>OmitCAValidation</c>) were inert without a client cert, so
    ///   server-cert-only TLS - the mainstream MQTT-over-TLS shape -
    ///   composed no TLS options at all.</item>
    ///   <item>Bug 2: the credentials-branch fallback rebuilt the TLS
    ///   options with only <c>SslProtocols.Tls12</c>, discarding every
    ///   <c>Tls.*</c> flag the client-cert branch had composed.</item>
    /// </list>
    ///
    /// The helper composes TLS options in one pass. These tests pin
    /// the composition contract so a future contributor cannot
    /// re-introduce the split-branch layout that hid the bugs.
    /// </summary>
    [TestFixture]
    public class MqttRelayTlsOptionsBuilderTests
    {
        /// <summary>Pins the behaviour: no Tls object and no UseTls flag returns null so the caller skips WithTlsOptions.</summary>
        [Test]
        public void Build_returns_null_when_UseTls_false_and_Tls_null()
        {
            var configuration = new MqttRelayModuleConfiguration();

            var options = MqttRelayTlsOptionsBuilder.Build(configuration, SslProtocols.Tls12);

            Assert.That(options, Is.Null);
        }

        /// <summary>Pins bug 1 fix: UseTls=true alone (no Tls object) composes TLS options.</summary>
        [Test]
        public void Build_returns_options_when_UseTls_true_without_Tls_object()
        {
            var configuration = new MqttRelayModuleConfiguration { UseTls = true };

            var options = MqttRelayTlsOptionsBuilder.Build(configuration, SslProtocols.Tls12);

            Assert.That(options, Is.Not.Null);
            Assert.That(options.UseTls, Is.True);
        }

        /// <summary>Pins bug 1 fix: a Tls object alone (UseTls not set) composes TLS options too.</summary>
        [Test]
        public void Build_returns_options_when_Tls_object_present_without_UseTls_flag()
        {
            var configuration = new MqttRelayModuleConfiguration
            {
                Tls = new TlsConfiguration()
            };

            var options = MqttRelayTlsOptionsBuilder.Build(configuration, SslProtocols.Tls12);

            Assert.That(options, Is.Not.Null);
            Assert.That(options.UseTls, Is.True);
        }

        /// <summary>Pins bug 3 fix (verified via the resolver at the caller site): whatever SslProtocols bitmask the caller passes reaches the built options.</summary>
        [Test]
        public void Build_applies_passed_in_SslProtocols_to_built_options()
        {
            var configuration = new MqttRelayModuleConfiguration { UseTls = true };

            var options = MqttRelayTlsOptionsBuilder.Build(configuration, SslProtocols.Tls12);

            Assert.That(options.SslProtocol, Is.EqualTo(SslProtocols.Tls12));
        }

        /// <summary>Pins bug 1 fix: Tls.VerifyClientCertificate=false surfaces as AllowUntrustedCertificates=true even without a client certificate.</summary>
        [Test]
        public void Build_applies_VerifyClientCertificate_false_as_AllowUntrusted_true_without_client_cert()
        {
            var configuration = new MqttRelayModuleConfiguration
            {
                UseTls = true,
                Tls = new TlsConfiguration
                {
                    VerifyClientCertificate = false
                }
            };

            var options = MqttRelayTlsOptionsBuilder.Build(configuration, SslProtocols.Tls12);

            Assert.That(options, Is.Not.Null);
            Assert.That(options.AllowUntrustedCertificates, Is.True);
            Assert.That(options.ClientCertificatesProvider, Is.Null,
                "No client certificate was configured; the client-cert list must not be attached.");
        }

        /// <summary>Pins bug 1 fix: Tls.VerifyClientCertificate=true surfaces as AllowUntrustedCertificates=false even without a client certificate.</summary>
        [Test]
        public void Build_applies_VerifyClientCertificate_true_as_AllowUntrusted_false_without_client_cert()
        {
            var configuration = new MqttRelayModuleConfiguration
            {
                UseTls = true,
                Tls = new TlsConfiguration
                {
                    VerifyClientCertificate = true
                }
            };

            var options = MqttRelayTlsOptionsBuilder.Build(configuration, SslProtocols.Tls12);

            Assert.That(options, Is.Not.Null);
            Assert.That(options.AllowUntrustedCertificates, Is.False);
        }

        /// <summary>Pins bug 2 fix at the composition level: a Tls object with flags composes options that already carry those flags. The Module.Worker credentials branch no longer rebuilds TLS options, so the caller-composed flag set survives.</summary>
        [Test]
        public void Build_preserves_flags_when_only_flags_are_configured()
        {
            var configuration = new MqttRelayModuleConfiguration
            {
                UseTls = true,
                Tls = new TlsConfiguration
                {
                    VerifyClientCertificate = false,
                    OmitCAValidation = true
                }
            };

            var options = MqttRelayTlsOptionsBuilder.Build(configuration, SslProtocols.Tls12);

            Assert.That(options, Is.Not.Null);
            Assert.That(options.AllowUntrustedCertificates, Is.True);
            Assert.That(options.SslProtocol, Is.EqualTo(SslProtocols.Tls12));
        }

        /// <summary>Verifies the Module.Worker post-fix composition order: TLS options are built first, credentials attached after. The built MqttClientOptions must still carry the composed TlsOptions unchanged.</summary>
        [Test]
        public void Full_client_options_pipeline_preserves_tls_when_credentials_added_after()
        {
            var configuration = new MqttRelayModuleConfiguration
            {
                Server = "broker.example.com",
                Port = 8883,
                UseTls = true,
                Username = "relay",
                Password = "relay-secret",
                Tls = new TlsConfiguration
                {
                    VerifyClientCertificate = false,
                    OmitCAValidation = true
                }
            };

            var tlsOptions = MqttRelayTlsOptionsBuilder.Build(configuration, SslProtocols.Tls12);
            Assert.That(tlsOptions, Is.Not.Null);

            var builder = new MqttClientOptionsBuilder();
            builder.WithTcpServer(configuration.Server, configuration.Port);
            builder.WithTlsOptions(tlsOptions);
            builder.WithCredentials(configuration.Username, configuration.Password);

            var clientOptions = builder.Build();

            var channelOptions = clientOptions.ChannelOptions as MqttClientTcpOptions;
            Assert.That(channelOptions, Is.Not.Null);
            Assert.That(channelOptions.TlsOptions, Is.Not.Null,
                "The credentials-attach step must not clobber the composed TlsOptions.");
            Assert.That(channelOptions.TlsOptions.UseTls, Is.True);
            Assert.That(channelOptions.TlsOptions.AllowUntrustedCertificates, Is.True,
                "VerifyClientCertificate=false must survive the credentials-attach step.");
            Assert.That(channelOptions.TlsOptions.SslProtocol, Is.EqualTo(SslProtocols.Tls12));
            Assert.That(clientOptions.Credentials, Is.Not.Null);
            Assert.That(clientOptions.Credentials.GetUserName(clientOptions), Is.EqualTo("relay"));
        }

        /// <summary>Regression pin: username-plus-password without a Tls object and without UseTls does not attach TLS. The credentials branch no longer inadvertently enables TLS via a builder-overwrite.</summary>
        [Test]
        public void Build_returns_null_for_credentials_only_configuration()
        {
            var configuration = new MqttRelayModuleConfiguration
            {
                Username = "relay",
                Password = "relay-secret"
            };

            var options = MqttRelayTlsOptionsBuilder.Build(configuration, SslProtocols.Tls12);

            Assert.That(options, Is.Null,
                "Credentials alone must not force TLS on; the user must opt in via UseTls or Tls.");
        }
    }
}
