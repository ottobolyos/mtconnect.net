// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the MIT license.

using System;
using System.IO;
using MTConnect.Agents;
using MTConnect.Configurations;
using NUnit.Framework;

namespace MTConnect.Tests.Common.Configurations
{
    /// <summary>
    /// Pins the load-time migration bridge that mirrors <see cref="AgentConfiguration.InputValidationLevel"/>
    /// onto <see cref="AgentConfiguration.DeviceValidationLevel"/> when the source configuration omits the
    /// <c>deviceValidationLevel</c> key.
    ///
    /// Motivation: pre-split, a single <see cref="InputValidationLevel"/> knob gated both Observation/Asset
    /// validation and Device-tree validation. The split introduced by PR #218 leaves consumers who only set
    /// <c>inputValidationLevel</c> silently downgraded on the Device-tree side. The migration bridge
    /// preserves the pre-split expectation.
    ///
    /// Also pins the two setter guards from the same PR: an out-of-range integer on either enum property
    /// raises <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    [TestFixture]
    [Category("DeviceValidationLevel")]
    public class DeviceValidationLevelMigrationTests
    {
        // ---------------------------------------------------------------
        // JSON load path: implicit InputValidationLevel mirror
        // ---------------------------------------------------------------

        /// <summary>Pins that a JSON config with only <c>inputValidationLevel: 3</c> (Strict) loads with <see cref="DeviceValidationLevel.Strict"/>.</summary>
        [Test]
        public void ReadJson_InputValidationLevel_Strict_Only_Mirrors_Onto_DeviceValidationLevel()
        {
            // The shipped JsonSerializerOptions treat enums as their integer ordinals — no
            // JsonStringEnumConverter registered — so the fixture wires the ordinal directly.
            var path = WriteTempJson("{\"inputValidationLevel\":3}");
            try
            {
                var config = AgentConfiguration.ReadJson(path);

                Assert.That(config, Is.Not.Null, "the loader must not have swallowed the config");
                Assert.That(config!.InputValidationLevel, Is.EqualTo(InputValidationLevel.Strict));
                Assert.That(config.DeviceValidationLevel, Is.EqualTo(DeviceValidationLevel.Strict),
                    "when the JSON omits deviceValidationLevel, the loader must mirror InputValidationLevel " +
                    "so pre-split consumers keep getting Strict Device-tree validation");
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>Pins that an explicit <c>deviceValidationLevel</c> beats the mirror even when both are present in JSON.</summary>
        [Test]
        public void ReadJson_Explicit_DeviceValidationLevel_Beats_Mirror()
        {
            // Deliberately set the two knobs to different arms so the mirror
            // path would corrupt DeviceValidationLevel if it fired anyway.
            var path = WriteTempJson(
                "{\"inputValidationLevel\":3,\"deviceValidationLevel\":0}");
            try
            {
                var config = AgentConfiguration.ReadJson(path);

                Assert.That(config, Is.Not.Null);
                Assert.That(config!.InputValidationLevel, Is.EqualTo(InputValidationLevel.Strict));
                Assert.That(config.DeviceValidationLevel, Is.EqualTo(DeviceValidationLevel.Ignore),
                    "an explicit deviceValidationLevel key must NOT be silently overwritten by the mirror");
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>Pins that a JSON config with neither knob set still loads with both defaulted to Warning.</summary>
        [Test]
        public void ReadJson_Empty_Object_Loads_With_Warning_Defaults()
        {
            var path = WriteTempJson("{}");
            try
            {
                var config = AgentConfiguration.ReadJson(path);

                Assert.That(config, Is.Not.Null);
                Assert.That(config!.InputValidationLevel, Is.EqualTo(InputValidationLevel.Warning));
                Assert.That(config.DeviceValidationLevel, Is.EqualTo(DeviceValidationLevel.Warning));
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>Pins that the mirror covers every enum arm, not just Strict.</summary>
        [TestCase(InputValidationLevel.Ignore, DeviceValidationLevel.Ignore)]
        [TestCase(InputValidationLevel.Warning, DeviceValidationLevel.Warning)]
        [TestCase(InputValidationLevel.Remove, DeviceValidationLevel.Remove)]
        [TestCase(InputValidationLevel.Strict, DeviceValidationLevel.Strict)]
        public void ReadJson_InputValidationLevel_Only_Mirrors_All_Arms(
            InputValidationLevel input,
            DeviceValidationLevel expectedMirrored)
        {
            var path = WriteTempJson($"{{\"inputValidationLevel\":{(int)input}}}");
            try
            {
                var config = AgentConfiguration.ReadJson(path);

                Assert.That(config, Is.Not.Null);
                Assert.That(config!.DeviceValidationLevel, Is.EqualTo(expectedMirrored));
            }
            finally
            {
                File.Delete(path);
            }
        }

        // ---------------------------------------------------------------
        // Programmatic Normalize()
        // ---------------------------------------------------------------

        /// <summary>Pins that a caller who builds an <see cref="AgentConfiguration"/> in code and calls <see cref="AgentConfiguration.Normalize"/> gets the same mirror.</summary>
        [Test]
        public void Normalize_Mirrors_InputValidationLevel_When_DeviceValidationLevel_Not_Explicit()
        {
            var config = new AgentConfiguration();
            config.InputValidationLevel = InputValidationLevel.Remove;

            // Precondition: the ctor default is Warning. Verify the assignment above did NOT touch
            // DeviceValidationLevel — that is the whole point of the flag.
            Assert.That(config.DeviceValidationLevel, Is.EqualTo(DeviceValidationLevel.Warning));

            config.Normalize();

            Assert.That(config.DeviceValidationLevel, Is.EqualTo(DeviceValidationLevel.Remove));
        }

        /// <summary>Pins that an explicit assignment to <see cref="AgentConfiguration.DeviceValidationLevel"/> disables the mirror on subsequent <see cref="AgentConfiguration.Normalize"/> calls.</summary>
        [Test]
        public void Normalize_Skips_Mirror_When_DeviceValidationLevel_Explicitly_Set()
        {
            var config = new AgentConfiguration();
            config.InputValidationLevel = InputValidationLevel.Strict;
            config.DeviceValidationLevel = DeviceValidationLevel.Ignore;

            config.Normalize();

            Assert.That(config.DeviceValidationLevel, Is.EqualTo(DeviceValidationLevel.Ignore),
                "an explicit DeviceValidationLevel assignment must sticky-suppress the mirror");
        }

        // ---------------------------------------------------------------
        // Setter validation (F-SEC-003)
        // ---------------------------------------------------------------

        /// <summary>Pins that an out-of-range integer cast to <see cref="DeviceValidationLevel"/> is rejected at the setter.</summary>
        [Test]
        public void DeviceValidationLevel_Setter_Rejects_Undefined_Enum_Value()
        {
            var config = new AgentConfiguration();
            Assert.Throws<ArgumentOutOfRangeException>(
                () => config.DeviceValidationLevel = (DeviceValidationLevel)42);
        }

        /// <summary>Pins that an out-of-range integer cast to <see cref="InputValidationLevel"/> is rejected at the setter.</summary>
        [Test]
        public void InputValidationLevel_Setter_Rejects_Undefined_Enum_Value()
        {
            var config = new AgentConfiguration();
            Assert.Throws<ArgumentOutOfRangeException>(
                () => config.InputValidationLevel = (InputValidationLevel)99);
        }

        /// <summary>Pins that every defined enum arm is accepted (no false positives from the guard).</summary>
        [TestCase(DeviceValidationLevel.Ignore)]
        [TestCase(DeviceValidationLevel.Warning)]
        [TestCase(DeviceValidationLevel.Remove)]
        [TestCase(DeviceValidationLevel.Strict)]
        public void DeviceValidationLevel_Setter_Accepts_Every_Defined_Arm(DeviceValidationLevel arm)
        {
            var config = new AgentConfiguration();
            Assert.DoesNotThrow(() => config.DeviceValidationLevel = arm);
            Assert.That(config.DeviceValidationLevel, Is.EqualTo(arm));
        }

        // ---------------------------------------------------------------
        // Fixture harness
        // ---------------------------------------------------------------

        private static string WriteTempJson(string json)
        {
            var path = Path.Combine(Path.GetTempPath(), $"agent-config-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, json);
            return path;
        }
    }
}
