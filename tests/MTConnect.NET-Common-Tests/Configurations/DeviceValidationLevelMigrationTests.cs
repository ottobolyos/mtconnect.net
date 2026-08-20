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

        /// <summary>
        /// Positive-arm coverage FLOOR for <see cref="InputValidationLevel"/>.
        /// The sibling assertion for <c>DeviceValidationLevel</c> above ensured no
        /// false-positive rejections; the parallel <c>InputValidationLevel</c>
        /// setter uses the same <c>Enum.IsDefined</c> guard (AgentConfiguration.cs:176)
        /// and must be pinned identically so a regression that widens or narrows
        /// the accepted arm set on the input axis fails loudly.
        /// </summary>
        [TestCase(InputValidationLevel.Ignore)]
        [TestCase(InputValidationLevel.Warning)]
        [TestCase(InputValidationLevel.Remove)]
        [TestCase(InputValidationLevel.Strict)]
        public void InputValidationLevel_Setter_Accepts_Every_Defined_Arm(InputValidationLevel arm)
        {
            var config = new AgentConfiguration();
            Assert.DoesNotThrow(() => config.InputValidationLevel = arm);
            Assert.That(config.InputValidationLevel, Is.EqualTo(arm));
        }

        /// <summary>
        /// Boundary coverage FLOOR for the <see cref="DeviceValidationLevel"/> setter guard
        /// (AgentConfiguration.cs:151). The setter uses <see cref="Enum.IsDefined(Type, object)"/>
        /// which rejects EVERY ordinal that is not a defined arm — the surviving valid arms
        /// are 0..3. Pin the four boundary shapes the FLOOR names — negative one, first
        /// invalid over-max ordinal, and the two integer extremes — so a regression that
        /// swaps <c>IsDefined</c> for a permissive range check (for example <c>value &lt;= Strict</c>,
        /// which would accept -1) fails on the first case rather than sneaking past the
        /// existing single (42) coverage.
        /// </summary>
        [TestCase(-1)]
        [TestCase(4)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void DeviceValidationLevel_Setter_Rejects_Out_Of_Range_Boundary(int ordinal)
        {
            var config = new AgentConfiguration();
            Assert.Throws<ArgumentOutOfRangeException>(
                () => config.DeviceValidationLevel = (DeviceValidationLevel)ordinal,
                $"DeviceValidationLevel setter must reject ordinal {ordinal} — Enum.IsDefined guard is strict against every value outside 0..3.");
        }

        /// <summary>
        /// Boundary coverage FLOOR for the <see cref="InputValidationLevel"/> setter guard
        /// (AgentConfiguration.cs:176). Same four shapes as the DeviceValidationLevel
        /// boundary — the guard is textually identical and must stay strict on both axes.
        /// </summary>
        [TestCase(-1)]
        [TestCase(4)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void InputValidationLevel_Setter_Rejects_Out_Of_Range_Boundary(int ordinal)
        {
            var config = new AgentConfiguration();
            Assert.Throws<ArgumentOutOfRangeException>(
                () => config.InputValidationLevel = (InputValidationLevel)ordinal,
                $"InputValidationLevel setter must reject ordinal {ordinal} — Enum.IsDefined guard is strict against every value outside 0..3.");
        }

        /// <summary>
        /// Pins the setter exception shape — <c>paramName</c> is <c>"value"</c> and the
        /// <c>ActualValue</c> carries the offending enum ordinal so callers logging the
        /// exception get the diagnostic. A regression that throws a bare
        /// <c>ArgumentException</c> (dropping the paramName / actual-value tuple) would
        /// still satisfy the coarser <c>Assert.Throws&lt;ArgumentOutOfRangeException&gt;</c>
        /// gates above only because ArgumentOutOfRangeException derives from
        /// ArgumentException — but the message shape carrying the two documented pieces of
        /// diagnostic is a contract callers depend on. This test pins that shape.
        /// </summary>
        [Test]
        public void DeviceValidationLevel_Setter_Exception_Carries_ParamName_And_ActualValue()
        {
            var config = new AgentConfiguration();
            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => config.DeviceValidationLevel = (DeviceValidationLevel)99);

            Assert.That(ex!.ParamName, Is.EqualTo("value"),
                "paramName is a documented invariant — subscribers log it verbatim.");
            Assert.That(ex.ActualValue, Is.EqualTo((DeviceValidationLevel)99),
                "ActualValue carries the offending ordinal so the diagnostic names the caller's mistake.");
            Assert.That(ex.Message, Does.Contain("DeviceValidationLevel"),
                "Message must name the enum so subscribers can distinguish DVL vs IVL failures.");
        }

        /// <summary>Same exception-shape pin as above, but for <see cref="InputValidationLevel"/>.</summary>
        [Test]
        public void InputValidationLevel_Setter_Exception_Carries_ParamName_And_ActualValue()
        {
            var config = new AgentConfiguration();
            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => config.InputValidationLevel = (InputValidationLevel)99);

            Assert.That(ex!.ParamName, Is.EqualTo("value"));
            Assert.That(ex.ActualValue, Is.EqualTo((InputValidationLevel)99));
            Assert.That(ex.Message, Does.Contain("InputValidationLevel"));
        }

        // ---------------------------------------------------------------
        // Direct Normalize() — every arm, not just Remove
        // ---------------------------------------------------------------

        /// <summary>
        /// Pins the programmatic-<see cref="AgentConfiguration.Normalize"/> mirror across
        /// every <see cref="InputValidationLevel"/> arm — not just <c>Remove</c> as the
        /// original single test covered. The <c>ReadJson</c>-driven parametrised test
        /// above exercises the mirror THROUGH the loader; this test exercises the mirror
        /// DIRECTLY so a regression that skips the mirror on a specific arm (for example
        /// an off-by-one enum-cast bug producing wrong ordinals for arms 0 or 3) fails
        /// on the pinned arm rather than being masked by the ordinal happening to align.
        /// </summary>
        [TestCase(InputValidationLevel.Ignore, DeviceValidationLevel.Ignore)]
        [TestCase(InputValidationLevel.Warning, DeviceValidationLevel.Warning)]
        [TestCase(InputValidationLevel.Remove, DeviceValidationLevel.Remove)]
        [TestCase(InputValidationLevel.Strict, DeviceValidationLevel.Strict)]
        public void Normalize_Mirrors_Every_InputValidationLevel_Arm(
            InputValidationLevel input,
            DeviceValidationLevel expectedMirrored)
        {
            var config = new AgentConfiguration();
            config.InputValidationLevel = input;

            config.Normalize();

            Assert.That(config.DeviceValidationLevel, Is.EqualTo(expectedMirrored),
                $"Normalize must mirror InputValidationLevel.{input} onto DeviceValidationLevel.{expectedMirrored} because DeviceValidationLevel was never assigned explicitly.");
        }

        /// <summary>
        /// Pins Normalize idempotency: calling <see cref="AgentConfiguration.Normalize"/>
        /// a second time is a stable no-op — the DeviceValidationLevel value is the
        /// mirrored value, not the ctor default. A regression that reset the
        /// nullable backing field to null inside Normalize (making the mirror
        /// re-fire) would silently overwrite a subsequent explicit assignment;
        /// pinning idempotency catches that class of bug.
        /// </summary>
        [Test]
        public void Normalize_Is_Idempotent_On_Repeat_Calls()
        {
            var config = new AgentConfiguration();
            config.InputValidationLevel = InputValidationLevel.Strict;

            config.Normalize();
            Assert.That(config.DeviceValidationLevel, Is.EqualTo(DeviceValidationLevel.Strict),
                "first Normalize mirrors the input axis onto the device axis.");

            config.Normalize();
            Assert.That(config.DeviceValidationLevel, Is.EqualTo(DeviceValidationLevel.Strict),
                "second Normalize must be a stable no-op — the mirrored value must not regress to the ctor default.");
        }

        /// <summary>
        /// Pins that setting <see cref="AgentConfiguration.InputValidationLevel"/>
        /// AFTER an explicit <see cref="AgentConfiguration.DeviceValidationLevel"/>
        /// assignment does NOT re-arm the mirror. The DVL setter populates the
        /// nullable backing field to a non-null value and the IVL setter never
        /// touches it — a caller who explicitly set DVL then later set IVL must
        /// not have DVL silently overwritten on the next Normalize call.
        /// </summary>
        [Test]
        public void Normalize_Explicit_DeviceValidationLevel_Then_Later_InputValidationLevel_Assignment_Does_Not_Rearm_Mirror()
        {
            var config = new AgentConfiguration();
            config.DeviceValidationLevel = DeviceValidationLevel.Ignore;
            // The DVL setter populated the nullable backing field to non-null. Later
            // IVL assignment must not reset it to null.
            config.InputValidationLevel = InputValidationLevel.Strict;

            config.Normalize();

            Assert.That(config.DeviceValidationLevel, Is.EqualTo(DeviceValidationLevel.Ignore),
                "later InputValidationLevel assignment must not re-arm the mirror — the explicit-DVL latch is sticky.");
        }

        /// <summary>
        /// Pins sticky suppression across every <see cref="DeviceValidationLevel"/> arm —
        /// the existing single-arm sticky-suppression test only covered
        /// <c>Strict → Ignore</c>. A regression that resets the nullable backing
        /// field to null only on a specific arm (for example on the ctor-default
        /// arm) would slip past the single existing test.
        /// </summary>
        [TestCase(DeviceValidationLevel.Ignore)]
        [TestCase(DeviceValidationLevel.Warning)]
        [TestCase(DeviceValidationLevel.Remove)]
        [TestCase(DeviceValidationLevel.Strict)]
        public void Normalize_Sticky_Suppression_Holds_For_Every_Explicit_Arm(DeviceValidationLevel explicitArm)
        {
            var config = new AgentConfiguration();
            // Pick an IVL arm that is DIFFERENT from the explicit DVL arm so the mirror
            // path would corrupt DVL if the latch failed. Both enums share ordinals so
            // the cast is meaningful.
            config.InputValidationLevel = explicitArm == DeviceValidationLevel.Strict
                ? InputValidationLevel.Ignore
                : InputValidationLevel.Strict;
            config.DeviceValidationLevel = explicitArm;

            config.Normalize();

            Assert.That(config.DeviceValidationLevel, Is.EqualTo(explicitArm),
                $"the explicit-DVL latch must stick for arm {explicitArm} regardless of the divergent IVL value.");
        }

        // ---------------------------------------------------------------
        // YAML load path — parallel to ReadJson mirror
        // ---------------------------------------------------------------

        /// <summary>
        /// Pins the YAML load path invokes <see cref="AgentConfiguration.Normalize"/> —
        /// the docstring on <c>Normalize</c> lists <c>ReadYaml</c> as an invocation site
        /// (AgentConfiguration.cs:240) but the existing fixture only exercises the JSON
        /// path. A regression that dropped the <c>configuration.Normalize()</c> call from
        /// <c>ReadYaml</c> (AgentConfiguration.cs:439) would silently downgrade YAML-loading
        /// consumers to the ctor-default DeviceValidationLevel on every arm change of
        /// InputValidationLevel — a diagnostic-silent behaviour break.
        /// </summary>
        [Test]
        public void ReadYaml_InputValidationLevel_Only_Mirrors_Onto_DeviceValidationLevel()
        {
            var path = WriteTempYaml("inputValidationLevel: 3\n");
            try
            {
                var config = AgentConfiguration.ReadYaml(path);

                Assert.That(config, Is.Not.Null, "the YAML loader must not have swallowed the config.");
                Assert.That(config!.InputValidationLevel, Is.EqualTo(InputValidationLevel.Strict));
                Assert.That(config.DeviceValidationLevel, Is.EqualTo(DeviceValidationLevel.Strict),
                    "the YAML load path must invoke Normalize so pre-split consumers keep the mirrored DeviceValidationLevel.");
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>Pins that YAML explicit <c>deviceValidationLevel</c> beats the mirror — sister of the JSON test.</summary>
        [Test]
        public void ReadYaml_Explicit_DeviceValidationLevel_Beats_Mirror()
        {
            var path = WriteTempYaml(
                "inputValidationLevel: 3\ndeviceValidationLevel: 0\n");
            try
            {
                var config = AgentConfiguration.ReadYaml(path);

                Assert.That(config, Is.Not.Null);
                Assert.That(config!.InputValidationLevel, Is.EqualTo(InputValidationLevel.Strict));
                Assert.That(config.DeviceValidationLevel, Is.EqualTo(DeviceValidationLevel.Ignore),
                    "an explicit deviceValidationLevel key in YAML must NOT be silently overwritten by the mirror.");
            }
            finally
            {
                File.Delete(path);
            }
        }

        // ---------------------------------------------------------------
        // Load-time enum-value validation — dime H2 + M4
        // ---------------------------------------------------------------

        /// <summary>
        /// Pins that a JSON config with an out-of-range integer for
        /// <c>inputValidationLevel</c> throws an <see cref="ArgumentException"/>
        /// whose message names the offending configuration path — the pre-fix
        /// <c>catch { }</c> silently returned null, hiding the actionable
        /// setter diagnostic from the operator. Dime cycle-1 finding H2
        /// (security-audit A09 + code-review F-CR-241-04) required the swallow
        /// be replaced; M4 required the path be attached.
        /// </summary>
        [Test]
        public void ReadJson_Invalid_Enum_Ordinal_Throws_ArgumentException_With_Path()
        {
            var path = WriteTempJson("{\"inputValidationLevel\":42}");
            try
            {
                var ex = Assert.Throws<ArgumentException>(() => AgentConfiguration.ReadJson(path));
                Assert.That(ex!.Message, Does.Contain(path),
                    "the wrapped exception must carry the configuration path so operators can trace the bad key back to its file.");
                Assert.That(ex.Message, Does.Contain("InputValidationLevel"),
                    "the wrapped exception must preserve the setter's actionable message naming the failing enum.");
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// Sibling of the JSON test — the YAML load path must also surface the
        /// enum-out-of-range setter exception (unwrapping any deserialiser-level
        /// wrapper — YamlDotNet nests AOORE inside its own container) with the
        /// configuration path attached.
        /// </summary>
        [Test]
        public void ReadYaml_Invalid_Enum_Ordinal_Throws_ArgumentException_With_Path()
        {
            var path = WriteTempYaml("inputValidationLevel: 42\n");
            try
            {
                var ex = Assert.Throws<ArgumentException>(() => AgentConfiguration.ReadYaml(path));
                Assert.That(ex!.Message, Does.Contain(path),
                    "the wrapped exception must carry the configuration path so operators can trace the bad key back to its file.");
                Assert.That(ex.Message, Does.Contain("InputValidationLevel"),
                    "the unwrapped setter message must survive so callers still see the actionable diagnostic.");
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// Pins that a malformed-but-parseable-shape JSON (invalid syntax that
        /// makes JsonSerializer throw a non-enum exception) preserves the
        /// documented null-return contract — the H2 fix converts silent swallow
        /// into Trace.TraceError diagnostic but must NOT change the return
        /// contract for non-enum failures.
        /// </summary>
        [Test]
        public void ReadJson_Malformed_Json_Returns_Null_Preserving_Loader_Contract()
        {
            var path = WriteTempJson("{ this is not valid json ]");
            try
            {
                AgentConfiguration config = new AgentConfiguration();
                Assert.DoesNotThrow(() => config = AgentConfiguration.ReadJson(path),
                    "non-enum parse failures must not throw — the documented loader contract is null-on-failure.");
                Assert.That(config, Is.Null,
                    "the loader must return null for malformed input to preserve pre-fix caller contracts.");
            }
            finally
            {
                File.Delete(path);
            }
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

        private static string WriteTempYaml(string yaml)
        {
            var path = Path.Combine(Path.GetTempPath(), $"agent-config-{Guid.NewGuid():N}.yaml");
            File.WriteAllText(path, yaml);
            return path;
        }
    }
}
