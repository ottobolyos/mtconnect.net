// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace MTConnect.NET_JSON_Tests.Regressions
{
    /// <summary>
    /// Regression pin for the DIME-connector native-heap leak (peer
    /// diagnosis dated 2026-08-21). A fresh <see cref="JsonSerializerOptions"/>
    /// instance owns its own serialisation-metadata cache; building that
    /// cache emits reflection-based property accessors for the entire type
    /// graph, and the emitted code accumulates in the runtime's loader
    /// heaps where the GC cannot reclaim it. Allocating one on every
    /// serialisation call therefore leaked ~3.3 MB/h RSS in production
    /// (tempco-001, tim-001).
    ///
    /// The <see cref="JsonFunctions.DefaultOptions"/> and
    /// <see cref="JsonFunctions.IndentOptions"/> properties must therefore
    /// return the same instance on every access, and the
    /// <c>Convert</c>/<c>ConvertBytes</c>/<c>ConvertStream</c> hot paths
    /// must reuse those instances when no per-call converter is supplied.
    /// </summary>
    [TestFixture]
    public class JsonSerializerOptionsSingletonTests
    {
        // Small POCO used by the Convert/ConvertBytes/ConvertStream overload
        // smoke tests and the thread-safety pin. Kept tiny on purpose so
        // the metadata cache warms in a single JIT pass and each thread's
        // work is bounded, but with a nested type so the reachable-graph
        // JIT emit still runs on cold DefaultOptions instances.
        internal class SamplePayload
        {
            public string Name { get; set; } = "sample";
            public int Count { get; set; } = 42;
            public SampleChild Child { get; set; } = new SampleChild();
        }

        internal class SampleChild
        {
            public string Label { get; set; } = "child";
            public double Value { get; set; } = 3.14;
        }

        private sealed class NoopConverter : JsonConverter<SamplePayload>
        {
            public override SamplePayload Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
                => new SamplePayload();

            public override void Write(Utf8JsonWriter writer, SamplePayload value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteString("Name", value.Name);
                writer.WriteNumber("Count", value.Count);
                writer.WriteEndObject();
            }
        }

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
            // Structural guard: at least one static readonly field of type
            // JsonSerializerOptions must exist on JsonFunctions so the
            // shared instance survives across serialisation calls.
            var fields = typeof(JsonFunctions).GetFields(BindingFlags.NonPublic | BindingFlags.Static);
            var optionsFields = System.Array.FindAll(fields, f => f.FieldType == typeof(JsonSerializerOptions) && f.IsInitOnly);
            Assert.That(optionsFields.Length, Is.GreaterThanOrEqualTo(2),
                "JsonFunctions must declare shared static readonly JsonSerializerOptions fields (compact + indented) so the instances outlive each serialisation call.");
        }

        /// <summary>
        /// Thread-safety pin: 100 concurrent Convert calls against the shared
        /// DefaultOptions must all succeed, must not throw, and must produce
        /// byte-identical output. JsonSerializerOptions is documented as
        /// thread-safe for read after its first (de)serialization call, so
        /// concurrent Serialize calls sharing one instance is legal by
        /// contract — this test pins that contract against any future
        /// refactor that would put a mutation on the hot path.
        /// </summary>
        [Test]
        public void Convert_is_thread_safe_across_100_concurrent_callers()
        {
            const int threadCount = 100;
            var payload = new SamplePayload();
            var outputs = new ConcurrentBag<string>();
            var exceptions = new ConcurrentBag<System.Exception>();
            var startGate = new ManualResetEventSlim(false);

            var threads = new Thread[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                threads[i] = new Thread(() =>
                {
                    try
                    {
                        startGate.Wait();
                        var s = JsonFunctions.Convert(payload);
                        outputs.Add(s);
                    }
                    catch (System.Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                });
                threads[i].Start();
            }

            // Warm the singleton once on the driver thread so the first-use
            // metadata build is not itself concurrent with the race window.
            // The point of the pin is steady-state hot-path concurrency, not
            // first-touch racing (STJ handles that internally).
            _ = JsonFunctions.Convert(payload);

            startGate.Set();
            foreach (var t in threads) t.Join();

            Assert.That(exceptions, Is.Empty,
                "Concurrent Convert calls against the shared DefaultOptions must not throw.");
            Assert.That(outputs.Count, Is.EqualTo(threadCount));

            var canonical = JsonFunctions.Convert(payload);
            foreach (var s in outputs)
            {
                Assert.That(s, Is.EqualTo(canonical),
                    "Every concurrent Convert output must be byte-identical to the single-threaded canonical output.");
            }
        }

        /// <summary>
        /// Cold-path pin: when a caller supplies a per-call converter, the
        /// implementation must NOT append that converter to the shared
        /// DefaultOptions.Converters collection. Doing so would (a) permanently
        /// pollute the singleton and (b) throw InvalidOperationException
        /// because JsonSerializerOptions freezes its converter list after
        /// first use. The pin observes the singleton before and after a
        /// converter-supplied Convert call and asserts the count is unchanged.
        /// </summary>
        [Test]
        public void Convert_with_custom_converter_does_not_mutate_DefaultOptions_converters()
        {
            // Warm the singleton so its converters list is frozen — if the
            // implementation ever tried to append to it, the test would
            // observe an exception path rather than silent pollution.
            _ = JsonFunctions.Convert(new SamplePayload());

            var beforeCount = JsonFunctions.DefaultOptions.Converters.Count;
            var beforeIndentCount = JsonFunctions.IndentOptions.Converters.Count;

            var converter = new NoopConverter();
            var result = JsonFunctions.Convert(new SamplePayload(), converter);
            var resultIndented = JsonFunctions.Convert(new SamplePayload(), converter, indented: true);

            Assert.That(result, Is.Not.Null,
                "Convert with a custom converter must succeed via the cold-path fresh-options branch.");
            Assert.That(resultIndented, Is.Not.Null,
                "Convert(indented:true) with a custom converter must succeed via the cold-path fresh-options branch.");

            Assert.That(JsonFunctions.DefaultOptions.Converters.Count, Is.EqualTo(beforeCount),
                "The cold-path branch must build a fresh JsonSerializerOptions — a caller-supplied converter must never leak into DefaultOptions.Converters.");
            Assert.That(JsonFunctions.IndentOptions.Converters.Count, Is.EqualTo(beforeIndentCount),
                "The cold-path branch must build a fresh JsonSerializerOptions — a caller-supplied converter must never leak into IndentOptions.Converters.");
        }

        /// <summary>
        /// Cold-path pin: the custom-converter Convert output must reflect
        /// the caller's converter (proving the fresh options object used
        /// it), while a following converter-less Convert on the same input
        /// must NOT reflect the converter (proving the singleton was untouched).
        /// </summary>
        [Test]
        public void Convert_with_custom_converter_uses_converter_without_affecting_singleton_output()
        {
            var payload = new SamplePayload();
            var canonicalWithoutConverter = JsonFunctions.Convert(payload);

            var converter = new NoopConverter();
            var withConverter = JsonFunctions.Convert(payload, converter);

            // The custom converter drops the Child property, so its output
            // must not equal the canonical singleton-emitted output.
            Assert.That(withConverter, Is.Not.EqualTo(canonicalWithoutConverter),
                "The cold-path fresh options must apply the caller's converter — otherwise the singleton was reused, defeating the branch.");
            Assert.That(withConverter, Does.Not.Contain("Child"),
                "NoopConverter drops the Child field; the cold-path output should reflect that.");

            // Following singleton-path call must be untouched.
            var afterCanonical = JsonFunctions.Convert(payload);
            Assert.That(afterCanonical, Is.EqualTo(canonicalWithoutConverter),
                "After a converter-supplied call, the singleton-path output must be byte-identical to before.");
            Assert.That(afterCanonical, Does.Contain("Child"),
                "The singleton must still emit the Child field — the cold-path converter must not have polluted it.");
        }

        /// <summary>
        /// Overload smoke pin: Convert/ConvertBytes/ConvertStream must all
        /// produce the same JSON for the same input under the compact
        /// preset, and the indented variants must be internally consistent.
        /// This guards against a future refactor that accidentally routes
        /// one overload through a divergent options instance.
        /// </summary>
        [Test]
        public void Convert_ConvertBytes_ConvertStream_produce_identical_output_for_compact()
        {
            var payload = new SamplePayload();

            var s = JsonFunctions.Convert(payload);
            var bytes = JsonFunctions.ConvertBytes(payload);
            var stream = JsonFunctions.ConvertStream(payload);

            Assert.That(s, Is.Not.Null);
            Assert.That(bytes, Is.Not.Null);
            Assert.That(stream, Is.Not.Null);

            var bytesString = Encoding.UTF8.GetString(bytes!);
            Assert.That(bytesString, Is.EqualTo(s),
                "ConvertBytes(UTF-8) must decode to the same string ConvertBytes emits.");

            stream!.Position = 0;
            using var reader = new System.IO.StreamReader(stream);
            var streamString = reader.ReadToEnd();
            Assert.That(streamString, Is.EqualTo(s),
                "ConvertStream must contain the same JSON Convert returns.");
        }

        /// <summary>Overload smoke pin — indented variant of the same guard.</summary>
        [Test]
        public void Convert_ConvertBytes_ConvertStream_produce_identical_output_for_indented()
        {
            var payload = new SamplePayload();

            var s = JsonFunctions.Convert(payload, indented: true);
            var bytes = JsonFunctions.ConvertBytes(payload, indented: true);
            var stream = JsonFunctions.ConvertStream(payload, indented: true);

            Assert.That(s, Is.Not.Null);
            Assert.That(bytes, Is.Not.Null);
            Assert.That(stream, Is.Not.Null);

            var bytesString = Encoding.UTF8.GetString(bytes!);
            Assert.That(bytesString, Is.EqualTo(s));

            stream!.Position = 0;
            using var reader = new System.IO.StreamReader(stream);
            var streamString = reader.ReadToEnd();
            Assert.That(streamString, Is.EqualTo(s));

            // Cross-check indentation is present (crude, but sufficient
            // to pin that IndentOptions actually took effect on all
            // three overloads).
            Assert.That(s, Does.Contain("\n"),
                "Indented Convert output must contain newlines.");
            Assert.That(bytesString, Does.Contain("\n"),
                "Indented ConvertBytes output must contain newlines.");
            Assert.That(streamString, Does.Contain("\n"),
                "Indented ConvertStream output must contain newlines.");
        }

        /// <summary>
        /// Null-input pin: every overload must swallow a null input and
        /// return null / null / null — this is documented behaviour and
        /// the singleton refactor must preserve it.
        /// </summary>
        [Test]
        public void Convert_overloads_return_null_for_null_input()
        {
            Assert.That(JsonFunctions.Convert(null), Is.Null);
            Assert.That(JsonFunctions.ConvertBytes(null), Is.Null);
            Assert.That(JsonFunctions.ConvertStream(null), Is.Null);
        }

        /// <summary>
        /// Async-throughput pin: 100 parallel Convert calls via
        /// Parallel.For to complement the Thread-based pin above. The
        /// ThreadPool scheduling differs from raw Thread scheduling, and
        /// System.Text.Json's internal metadata-lock behaviour has
        /// historically been sensitive to the difference; both paths are
        /// pinned so future STJ upgrades are covered.
        /// </summary>
        [Test]
        public void Convert_is_thread_safe_under_ParallelFor()
        {
            _ = JsonFunctions.Convert(new SamplePayload()); // warm

            var canonical = JsonFunctions.Convert(new SamplePayload());
            var results = new ConcurrentBag<string>();

            Parallel.For(0, 100, _ =>
            {
                var s = JsonFunctions.Convert(new SamplePayload());
                results.Add(s);
            });

            Assert.That(results.Count, Is.EqualTo(100));
            foreach (var r in results)
            {
                Assert.That(r, Is.EqualTo(canonical));
            }
        }
    }
}
