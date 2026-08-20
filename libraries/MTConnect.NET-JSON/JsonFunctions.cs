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
        public static JsonSerializerOptions DefaultOptions => _defaultOptions;

        /// <summary>
        /// The indented variant of <see cref="DefaultOptions"/>, used when the
        /// formatter's <c>indentOutput</c> option is set.
        /// </summary>
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
