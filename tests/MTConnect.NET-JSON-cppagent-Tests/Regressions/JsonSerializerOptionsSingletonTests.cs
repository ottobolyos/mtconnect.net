// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the MIT license.

using System.Reflection;
using System.Text.Json;
using NUnit.Framework;

namespace MTConnect.NET_JSON_cppagent_Tests.Regressions
{
    /// <summary>
    /// Regression pin for the DIME-connector native-heap leak (peer
    /// diagnosis dated 2026-08-21). This is the cppagent-flavoured
    /// mirror of the same guard applied to
    /// <c>MTConnect.NET-JSON/JsonFunctions.cs</c>. The two files ship
    /// independent copies of the same option-preset surface, so both
    /// must singleton their <see cref="JsonSerializerOptions"/> to keep
    /// the runtime's loader heap from accumulating LCG-emitted property
    /// accessors on every serialisation call.
    /// </summary>
    [TestFixture]
    public class JsonSerializerOptionsSingletonTests
    {
        /// <summary>Pins the behaviour expressed by the test name: default options returns the same instance on repeat access.</summary>
        [Test]
        public void DefaultOptions_returns_the_same_instance_on_repeat_access()
        {
            var a = JsonFunctions.DefaultOptions;
            var b = JsonFunctions.DefaultOptions;
            Assert.That(ReferenceEquals(a, b), Is.True,
                "JsonFunctions.DefaultOptions must be a shared singleton — a fresh JsonSerializerOptions per call leaks LCG-emitted metadata into the loader heap.");
        }

        /// <summary>Pins the behaviour expressed by the test name: indent options returns the same instance on repeat access.</summary>
        [Test]
        public void IndentOptions_returns_the_same_instance_on_repeat_access()
        {
            var a = JsonFunctions.IndentOptions;
            var b = JsonFunctions.IndentOptions;
            Assert.That(ReferenceEquals(a, b), Is.True,
                "JsonFunctions.IndentOptions must be a shared singleton — a fresh JsonSerializerOptions per call leaks LCG-emitted metadata into the loader heap.");
        }

        /// <summary>Pins the behaviour expressed by the test name: default options and indent options are distinct instances.</summary>
        [Test]
        public void DefaultOptions_and_IndentOptions_are_distinct_instances()
        {
            Assert.That(ReferenceEquals(JsonFunctions.DefaultOptions, JsonFunctions.IndentOptions), Is.False,
                "DefaultOptions (compact) and IndentOptions (pretty-printed) must be separate instances so WriteIndented differs.");
        }

        /// <summary>Pins the behaviour expressed by the test name: default options write indented is false.</summary>
        [Test]
        public void DefaultOptions_WriteIndented_is_false()
        {
            Assert.That(JsonFunctions.DefaultOptions.WriteIndented, Is.False);
        }

        /// <summary>Pins the behaviour expressed by the test name: indent options write indented is true.</summary>
        [Test]
        public void IndentOptions_WriteIndented_is_true()
        {
            Assert.That(JsonFunctions.IndentOptions.WriteIndented, Is.True);
        }

        /// <summary>Pins the behaviour expressed by the test name: json functions holds a static readonly options field.</summary>
        [Test]
        public void JsonFunctions_holds_a_static_readonly_options_field()
        {
            var fields = typeof(JsonFunctions).GetFields(BindingFlags.NonPublic | BindingFlags.Static);
            var optionsFields = System.Array.FindAll(fields, f => f.FieldType == typeof(JsonSerializerOptions) && f.IsInitOnly);
            Assert.That(optionsFields.Length, Is.GreaterThanOrEqualTo(2),
                "JsonFunctions must declare shared static readonly JsonSerializerOptions fields (compact + indented) so the instances outlive each serialisation call.");
        }
    }
}
