// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MTConnect
{
    /// <summary>
    /// JSON serialization helpers shared by the MTConnect JSON surrogate
    /// types. Provides the default and indented <see cref="JsonSerializerOptions"/>
    /// presets, ISO 8601 timestamp formatting, and convenience methods for
    /// serializing to a string, byte array, or stream.
    /// </summary>
    public static class JsonFunctions
    {
        // A JsonSerializerOptions instance owns its own serialization
        // metadata cache, and building that cache emits reflection-based
        // property accessors (LCG DynamicMethods) for every property in
        // the reachable type graph. Allocating a fresh instance per call
        // therefore re-emits those accessors on every serialization, and
        // the emitted code accumulates in the runtime's loader heaps
        // where the GC cannot reclaim it — this is the mechanism behind
        // the ~3.3 MB/h RSS climb observed on DIME production hosts in
        // 2026-08. The instances below are created once and reused;
        // JsonSerializerOptions is thread-safe for read after its first
        // (de)serialization, and nothing in this file mutates them after
        // construction.
        private static readonly JsonSerializerOptions _defaultOptions = CreateOptions(false);
        private static readonly JsonSerializerOptions _indentOptions = CreateOptions(true);

        static JsonFunctions()
        {
#if NET8_0_OR_GREATER
            // Warm the default reflection resolver + serialization
            // metadata cache at assembly load rather than on the first
            // production /current or /sample request. Serializing a
            // typed null pays the resolver bootstrap cost + fixes the
            // options' internal state, so the first user-facing
            // Serialize call skips that setup entirely.
            //
            // Order matters: the warm-up must run BEFORE MakeReadOnly,
            // because MakeReadOnly(populateMissingResolver: false)
            // freezes the options WITHOUT choosing a TypeInfoResolver;
            // a subsequent Serialize on a resolver-less, frozen
            // options would throw NotSupportedException. Running
            // Serialize first lets STJ auto-populate the resolver via
            // its normal lazy path, after which MakeReadOnly(false)
            // is a pure lock with no side effect on serialization.
            JsonSerializer.Serialize<object>(null, _defaultOptions);
            JsonSerializer.Serialize<object>(null, _indentOptions);

            // Freeze both singletons so callers cannot mutate the
            // shared instance (adding a Converter, flipping
            // WriteIndented, etc.). Attempted mutation throws
            // InvalidOperationException — the fail-fast is preferable
            // to silent cross-caller pollution, and the cold-path
            // Convert branch stays open because it builds a fresh
            // (non-read-only) options object per call.
            _defaultOptions.MakeReadOnly(populateMissingResolver: false);
            _indentOptions.MakeReadOnly(populateMissingResolver: false);
#endif
        }

        private static JsonSerializerOptions CreateOptions(bool indented)
        {
            return new JsonSerializerOptions
            {
                WriteIndented = indented,
#if NET5_0_OR_GREATER
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
#endif
                PropertyNameCaseInsensitive = true,
                MaxDepth = 1000
            };
        }

        private static JsonSerializerOptions GetOptions(JsonConverter converter, bool indented)
        {
            // Hot path: no per-call converter, hand back the shared
            // instance and let System.Text.Json reuse its metadata cache
            // instead of re-emitting property accessors for the whole
            // type graph on every call.
            if (converter == null) return indented ? _indentOptions : _defaultOptions;

            // Cold path: a caller-supplied converter cannot be added to
            // a shared instance once it has been used, so build a
            // private one. Callers that mint many one-off converters
            // will still pay the LCG cost — this fallback preserves the
            // public API contract for external consumers.
            var options = CreateOptions(indented);
            options.Converters.Add(converter);
            return options;
        }

        /// <summary>
        /// The default <see cref="JsonSerializerOptions"/> used by MTConnect
        /// JSON serialization: compact output, default-valued properties
        /// omitted on write (on net5+), numbers read from strings (on net5+),
        /// case-insensitive property names, and a depth limit of 1000.
        /// </summary>
        /// <remarks>
        /// This is a process-wide shared singleton; do NOT mutate the
        /// returned instance's <see cref="JsonSerializerOptions.Converters"/>
        /// collection or any writable property. The returned object is
        /// marked <c>MakeReadOnly()</c> under <c>net8.0</c> and later;
        /// attempted mutation throws <see cref="System.InvalidOperationException"/>.
        /// On older TFMs (netstandard2.0, net4.6.1–net4.8, net6.0, net7.0)
        /// the instance is not statically frozen but callers must still
        /// treat it as immutable — mutating it silently corrupts every
        /// other in-process serializer that shares the singleton.
        /// </remarks>
        public static JsonSerializerOptions DefaultOptions => _defaultOptions;

        /// <summary>
        /// The indented variant of <see cref="DefaultOptions"/>, used when the
        /// formatter's <c>indentOutput</c> option is set.
        /// </summary>
        /// <remarks>
        /// This is a process-wide shared singleton; do NOT mutate the
        /// returned instance's <see cref="JsonSerializerOptions.Converters"/>
        /// collection or any writable property. The returned object is
        /// marked <c>MakeReadOnly()</c> under <c>net8.0</c> and later;
        /// attempted mutation throws <see cref="System.InvalidOperationException"/>.
        /// On older TFMs (netstandard2.0, net4.6.1–net4.8, net6.0, net7.0)
        /// the instance is not statically frozen but callers must still
        /// treat it as immutable — mutating it silently corrupts every
        /// other in-process serializer that shares the singleton.
        /// </remarks>
        public static JsonSerializerOptions IndentOptions => _indentOptions;


        /// <summary>
        /// Formats <paramref name="timestamp"/> as a round-trip ISO 8601
        /// string (the <c>o</c> format).
        /// </summary>
        public static string GetTimestamp(DateTime timestamp)
        {
            return timestamp.ToString("o");
        }

        /// <summary>
        /// Formats <paramref name="timestamp"/> as a round-trip ISO 8601
        /// string, normalizing to UTC when the offset is zero.
        /// </summary>
        public static string GetTimestamp(DateTimeOffset timestamp)
        {
            if (timestamp.Offset != TimeSpan.Zero)
            {
                return timestamp.ToString("o");
            }
            else
            {
                return timestamp.UtcDateTime.ToString("o");
            }
        }

        /// <summary>
        /// Serializes <paramref name="obj"/> to a JSON string using the MTConnect
        /// default options, optionally indented and optionally extended with a
        /// custom converter. Returns null on any serialization error.
        /// </summary>
        public static string Convert(object obj, JsonConverter converter = null, bool indented = false)
        {
            if (obj != null)
            {
                try
                {
                    var options = GetOptions(converter, indented);

                    return JsonSerializer.Serialize(obj, options);
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Serializes <paramref name="obj"/> to UTF-8 encoded JSON bytes using
        /// the MTConnect default options, optionally indented and optionally
        /// extended with a custom converter. Returns null on any serialization
        /// error.
        /// </summary>
        public static byte[] ConvertBytes(object obj, JsonConverter converter = null, bool indented = false)
        {
            if (obj != null)
            {
                try
                {
                    var options = GetOptions(converter, indented);

                    return JsonSerializer.SerializeToUtf8Bytes(obj, options);
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Serializes <paramref name="obj"/> to a JSON stream using the
        /// MTConnect default options, optionally indented and optionally
        /// extended with a custom converter. Returns null on any serialization
        /// error.
        /// </summary>
        public static Stream ConvertStream(object obj, JsonConverter converter = null, bool indented = false)
        {
            if (obj != null)
            {
                try
                {
                    var options = GetOptions(converter, indented);

                    var outputStream = new MemoryStream();
                    JsonSerializer.Serialize(outputStream, obj, options);
                    return outputStream;
                }
                catch { }
            }

            return null;
        }
    }
}
