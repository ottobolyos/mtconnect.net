// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the MIT license.

using System;
using System.Linq;
using MTConnect.Agents;
using MTConnect.Configurations;
using MTConnect.Devices;
using MTConnect.Devices.DataItems;
using MTConnect.Observations;
using NUnit.Framework;

namespace MTConnect.Tests.Common.Agents
{
    /// <summary>
    /// Pins the value-class-aware empty-Result contract on
    /// <see cref="MTConnectAgent.AddObservation(string, MTConnect.Input.IObservationInput, bool?, bool?, bool?, bool)"/>.
    ///
    /// The MTConnect Standard, Part 2 - Devices Information Model classifies each DataItem
    /// by the shape of value its Result carries; the SDK coerces null / empty / whitespace
    /// Results only when the DataItem's value class forbids the empty string:
    /// <list type="bullet">
    ///   <item>Numeric DataItems (all Samples per the "Sample MUST always be reported in
    ///     float" requirement, and the numeric-typed Events enumerated in the SysML
    ///     model - PART_COUNT, LINE_NUMBER, BLOCK_COUNT, HARDNESS, TOOL_OFFSET, and
    ///     kindred integer/float Result types) are always coerced.</item>
    ///   <item>Enumeration Events (EXECUTION, CONTROLLER_MODE, AVAILABILITY, and every
    ///     other Type with a controlled vocabulary) are coerced by default; the
    ///     <see cref="IAgentConfiguration.AllowEmptyResultForEnumEvents"/> escape hatch
    ///     preserves the empty Result when set to <c>true</c>.</item>
    ///   <item>Free-form String Events (PROGRAM, MESSAGE, TOOL_ID, ASSET_CHANGED, and
    ///     every other non-vocabulary Type) preserve the empty Result verbatim: the
    ///     standard's default value type for <c>Observation::result</c> is <c>string</c>,
    ///     and the reference C++ agent accepts empty strings for these Events
    ///     (confirmed by the MTConnect.NET maintainer on PR #217, 2026-08-18).</item>
    /// </list>
    ///
    /// The convenience overload
    /// <c>AddObservation(string deviceKey, string dataItemKey, object value, DateTime timestamp)</c>
    /// exercises the canonical
    /// <c>AddObservation(string, IObservationInput, ...)</c> path that every other
    /// AddObservation overload routes through; the tests cast values to <c>object</c> so
    /// the compiler resolves that overload unambiguously against the
    /// <c>(deviceKey, dataItemKey, valueKey, value)</c> sibling.
    /// </summary>
    [TestFixture]
    [Category("AddObservationEmptyResultCoerce")]
    public class AddObservationEmptyResultCoerceTests
    {
        private const string DeviceKey = "U-COERCE";
        private const string DeviceId = "d-coerce";

        private static readonly InputValidationLevel[] _nonStrictLevels =
        {
            InputValidationLevel.Ignore,
            InputValidationLevel.Warning,
            InputValidationLevel.Remove,
        };

        private static readonly object?[] _nullEmptyWhitespaceValues =
        {
            new object?[] { null },
            new object?[] { string.Empty },
            new object?[] { "   " },
            new object?[] { "\t" },
            new object?[] { "\n" },
        };


        // -------------------------------------------------------------------- //
        // Numeric class: SAMPLE                                                //
        // Spec: MTConnect Part 2, Value Properties of Sample -                 //
        // "Sample MUST always be reported in float."                           //
        // -------------------------------------------------------------------- //

        /// <summary>Empty Result on a SAMPLE DataItem is coerced to UNAVAILABLE under every non-Strict input-validation level.</summary>
        [Test]
        [TestCaseSource(nameof(_nonStrictLevels))]
        public void Sample_EmptyResult_Coerced_To_Unavailable_Under_NonStrict_Levels(InputValidationLevel level)
        {
            const string dataItemKey = SpindleSpeedDataItem.NameId;
            using var agent = NewAgent(level, dataItem: new SpindleSpeedDataItem(DeviceId, SpindleSpeedDataItem.SubTypes.ACTUAL));

            var added = agent.AddObservation(DeviceKey, dataItemKey, (object)string.Empty, DateTime.UtcNow);

            Assert.That(added, Is.True, "empty-Result Sample observation must reach the buffer post-coerce");
            Assert.That(CurrentResult(agent, dataItemKey), Is.EqualTo(Observation.Unavailable),
                "Part 2 mandates that Sample values be numeric; empty MUST become UNAVAILABLE");
        }

        /// <summary>The null / empty / whitespace family is coerced for every SAMPLE DataItem.</summary>
        [Test]
        [TestCaseSource(nameof(_nullEmptyWhitespaceValues))]
        public void Sample_NullEmptyOrWhitespaceResult_Coerced_To_Unavailable(object? badValue)
        {
            const string dataItemKey = SpindleSpeedDataItem.NameId;
            using var agent = NewAgent(InputValidationLevel.Warning, dataItem: new SpindleSpeedDataItem(DeviceId, SpindleSpeedDataItem.SubTypes.ACTUAL));

            var added = agent.AddObservation(DeviceKey, dataItemKey, (object?)badValue!, DateTime.UtcNow);

            Assert.That(added, Is.True);
            Assert.That(CurrentResult(agent, dataItemKey), Is.EqualTo(Observation.Unavailable));
        }

        /// <summary>Strict input-validation still admits an empty Sample Result via the coerce path rather than silently dropping it.</summary>
        [Test]
        public void Sample_EmptyResult_Under_Strict_Coerced_And_Lands()
        {
            const string dataItemKey = SpindleSpeedDataItem.NameId;
            using var agent = NewAgent(InputValidationLevel.Strict, dataItem: new SpindleSpeedDataItem(DeviceId, SpindleSpeedDataItem.SubTypes.ACTUAL));

            var added = agent.AddObservation(DeviceKey, dataItemKey, (object)string.Empty, DateTime.UtcNow);

            Assert.That(added, Is.True, "Strict must coerce to UNAVAILABLE - never silently drop an empty Result");
            Assert.That(CurrentResult(agent, dataItemKey), Is.EqualTo(Observation.Unavailable));
        }


        // -------------------------------------------------------------------- //
        // Enumeration class: EVENT with controlled vocabulary                  //
        // Spec: MTConnect Part 2, Observation::result - the Result MUST be a   //
        // member of the controlled vocabulary defined for the DataItem's Type. //
        // AVAILABILITY (Availability enum: AVAILABLE, UNAVAILABLE) exercises   //
        // an EVENT Type with a small, unambiguous vocabulary.                  //
        // -------------------------------------------------------------------- //

        /// <summary>Empty Result on an Enumeration EVENT DataItem is coerced to UNAVAILABLE when the flag defaults to false.</summary>
        [Test]
        [TestCaseSource(nameof(_nullEmptyWhitespaceValues))]
        public void EnumEvent_EmptyResult_Coerced_To_Unavailable_When_Flag_False(object? badValue)
        {
            const string dataItemKey = AvailabilityDataItem.NameId;
            using var agent = NewAgent(
                InputValidationLevel.Warning,
                allowEmptyResultForEnumEvents: false,
                dataItem: new AvailabilityDataItem(DeviceId));

            var added = agent.AddObservation(DeviceKey, dataItemKey, (object?)badValue!, DateTime.UtcNow);

            Assert.That(added, Is.True);
            Assert.That(CurrentResult(agent, dataItemKey), Is.EqualTo(Observation.Unavailable));
        }

        /// <summary>Empty Result on an Enumeration EVENT DataItem is preserved verbatim when the escape-hatch flag is enabled.</summary>
        [Test]
        public void EnumEvent_EmptyResult_Preserved_When_Flag_True()
        {
            const string dataItemKey = AvailabilityDataItem.NameId;
            using var agent = NewAgent(
                InputValidationLevel.Warning,
                allowEmptyResultForEnumEvents: true,
                dataItem: new AvailabilityDataItem(DeviceId));

            var added = agent.AddObservation(DeviceKey, dataItemKey, (object)string.Empty, DateTime.UtcNow);

            Assert.That(added, Is.True);
            var currentResult = CurrentResult(agent, dataItemKey) ?? string.Empty;
            Assert.That(currentResult, Is.EqualTo(string.Empty),
                "AllowEmptyResultForEnumEvents=true preserves the empty Result for controlled-vocabulary Events");
        }

        /// <summary>A concrete vocabulary member is preserved verbatim on an Enumeration EVENT DataItem regardless of the flag.</summary>
        [Test]
        public void EnumEvent_ConcreteResult_Is_Preserved_Verbatim()
        {
            const string dataItemKey = AvailabilityDataItem.NameId;
            using var agent = NewAgent(InputValidationLevel.Warning, dataItem: new AvailabilityDataItem(DeviceId));

            var added = agent.AddObservation(DeviceKey, dataItemKey, (object)"AVAILABLE", DateTime.UtcNow);

            Assert.That(added, Is.True);
            Assert.That(CurrentResult(agent, dataItemKey), Is.EqualTo("AVAILABLE"),
                "the coerce must not substitute a sentinel for a valid controlled-vocabulary member");
        }


        // -------------------------------------------------------------------- //
        // String class: free-form EVENT (PROGRAM, MESSAGE, TOOL_ID)            //
        // Spec: MTConnect Part 2, Observation::result - the default value type //
        // for Observation::result is `string`; the standard does not forbid    //
        // the empty string for non-vocabulary Event Types. The reference C++   //
        // agent accepts empty strings for these Events.                        //
        // -------------------------------------------------------------------- //

        /// <summary>Empty Result on a PROGRAM Event DataItem is preserved verbatim: PROGRAM is a free-form String Event Type.</summary>
        [Test]
        [TestCaseSource(nameof(_nullEmptyWhitespaceValues))]
        public void StringEvent_Program_EmptyResult_Preserved(object? badValue)
        {
            const string dataItemKey = ProgramDataItem.NameId;
            using var agent = NewAgent(InputValidationLevel.Warning, dataItem: new ProgramDataItem(DeviceId, ProgramDataItem.SubTypes.ACTIVE));

            var added = agent.AddObservation(DeviceKey, dataItemKey, (object?)badValue!, DateTime.UtcNow);

            Assert.That(added, Is.True);
            // Null / whitespace values pass through unchanged; the observation's Result equals the input's string form.
            var expected = badValue?.ToString() ?? string.Empty;
            var currentResult = (CurrentResult(agent, dataItemKey) as string) ?? string.Empty;
            Assert.That(currentResult, Is.EqualTo(expected),
                "PROGRAM is a free-form String Event Type; empty and whitespace Results MUST be preserved");
        }

        /// <summary>Empty Result on a MESSAGE Event DataItem is preserved verbatim.</summary>
        [Test]
        public void StringEvent_Message_EmptyResult_Preserved()
        {
            const string dataItemKey = MessageDataItem.NameId;
            using var agent = NewAgent(InputValidationLevel.Warning, dataItem: new MessageDataItem(DeviceId));

            var added = agent.AddObservation(DeviceKey, dataItemKey, (object)string.Empty, DateTime.UtcNow);

            Assert.That(added, Is.True);
            var currentResult = (CurrentResult(agent, dataItemKey) as string) ?? string.Empty;
            Assert.That(currentResult, Is.EqualTo(string.Empty),
                "MESSAGE is a free-form String Event Type; empty Results MUST be preserved");
        }

        /// <summary>Empty Result on a TOOL_ID Event DataItem is preserved verbatim.</summary>
        [Test]
        public void StringEvent_ToolId_EmptyResult_Preserved()
        {
            const string dataItemKey = ToolIdDataItem.NameId;
            using var agent = NewAgent(InputValidationLevel.Warning, dataItem: new ToolIdDataItem(DeviceId));

            var added = agent.AddObservation(DeviceKey, dataItemKey, (object)string.Empty, DateTime.UtcNow);

            Assert.That(added, Is.True);
            var currentResult = (CurrentResult(agent, dataItemKey) as string) ?? string.Empty;
            Assert.That(currentResult, Is.EqualTo(string.Empty),
                "TOOL_ID is a free-form String Event Type; empty Results MUST be preserved");
        }

        /// <summary>A concrete String Result is passed through verbatim on a free-form Event DataItem.</summary>
        [Test]
        public void StringEvent_Program_ConcreteResult_Is_Preserved_Verbatim()
        {
            const string dataItemKey = ProgramDataItem.NameId;
            using var agent = NewAgent(InputValidationLevel.Warning, dataItem: new ProgramDataItem(DeviceId, ProgramDataItem.SubTypes.ACTIVE));

            var added = agent.AddObservation(DeviceKey, dataItemKey, (object)"PART_A.NC", DateTime.UtcNow);

            Assert.That(added, Is.True);
            Assert.That(CurrentResult(agent, dataItemKey), Is.EqualTo("PART_A.NC"),
                "PROGRAM MUST pass free-form text through verbatim");
        }


        // -------------------------------------------------------------------- //
        // Numeric-typed EVENT (SysML integer/float Result)                     //
        // -------------------------------------------------------------------- //

        /// <summary>Empty Result on a numeric-typed Event (PART_COUNT: SysML `result: integer`) is coerced.</summary>
        [Test]
        public void NumericEvent_PartCount_EmptyResult_Coerced_To_Unavailable()
        {
            const string dataItemKey = PartCountDataItem.NameId;
            using var agent = NewAgent(
                InputValidationLevel.Warning,
                dataItem: new PartCountDataItem(DeviceId, PartCountDataItem.SubTypes.ALL));

            var added = agent.AddObservation(DeviceKey, dataItemKey, (object)string.Empty, DateTime.UtcNow);

            Assert.That(added, Is.True);
            Assert.That(CurrentResult(agent, dataItemKey), Is.EqualTo(Observation.Unavailable),
                "PART_COUNT's Result type is integer per the SysML model; empty MUST become UNAVAILABLE");
        }


        // -------------------------------------------------------------------- //
        // Classifier direct tests                                              //
        // -------------------------------------------------------------------- //

        /// <summary>Direct assertion: <see cref="DataItem.GetValueClass"/> classifies representative DataItems into the three value classes.</summary>
        [Test]
        public void GetValueClass_Classifies_Representative_DataItems()
        {
            Assert.That(DataItem.GetValueClass(new SpindleSpeedDataItem(DeviceId, SpindleSpeedDataItem.SubTypes.ACTUAL)), Is.EqualTo(DataItemValueClass.Numeric),
                "SAMPLE observations are Numeric per Part 2 - Sample MUST be float");
            Assert.That(DataItem.GetValueClass(new AvailabilityDataItem(DeviceId)), Is.EqualTo(DataItemValueClass.Enumeration),
                "AVAILABILITY has a controlled vocabulary (Availability enum)");
            Assert.That(DataItem.GetValueClass(new ExecutionDataItem(DeviceId)), Is.EqualTo(DataItemValueClass.Enumeration),
                "EXECUTION has a controlled vocabulary (Execution enum)");
            Assert.That(DataItem.GetValueClass(new ProgramDataItem(DeviceId, ProgramDataItem.SubTypes.ACTIVE)), Is.EqualTo(DataItemValueClass.String),
                "PROGRAM carries free-form text");
            Assert.That(DataItem.GetValueClass(new MessageDataItem(DeviceId)), Is.EqualTo(DataItemValueClass.String),
                "MESSAGE carries free-form text");
            Assert.That(DataItem.GetValueClass(new ToolIdDataItem(DeviceId)), Is.EqualTo(DataItemValueClass.String),
                "TOOL_ID carries free-form text");
            Assert.That(DataItem.GetValueClass(new AssetChangedDataItem(DeviceId)), Is.EqualTo(DataItemValueClass.String),
                "ASSET_CHANGED carries the asset id as free-form text");
            Assert.That(DataItem.GetValueClass(new PartCountDataItem(DeviceId, PartCountDataItem.SubTypes.ALL)), Is.EqualTo(DataItemValueClass.Numeric),
                "PART_COUNT Result is integer per the SysML model");
        }


        // -------------------------------------------------------------------- //
        // Helpers                                                              //
        // -------------------------------------------------------------------- //

        private static MTConnectAgentBroker NewAgent(
            InputValidationLevel level,
            IDataItem dataItem,
            bool allowEmptyResultForEnumEvents = false)
        {
            var config = new AgentConfiguration
            {
                InputValidationLevel = level,
                AllowEmptyResultForEnumEvents = allowEmptyResultForEnumEvents,
            };
            var agent = new MTConnectAgentBroker(config);
            agent.Start();

            var device = new Device
            {
                Id = DeviceId,
                Name = DeviceId,
                Uuid = DeviceKey,
            };
            device.AddDataItem(dataItem);

            var added = agent.AddDevice(device);
            Assert.That(added, Is.Not.Null, "AddDevice must succeed for test pre-condition");
            return agent;
        }

        private static object? CurrentResult(IMTConnectAgentBroker agent, string dataItemKey)
        {
            var current = agent.GetCurrentObservations(DeviceKey, dataItemKey).SingleOrDefault();
            return current?.GetValue(ValueKeys.Result);
        }
    }
}
