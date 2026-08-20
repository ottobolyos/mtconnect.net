// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the MIT license.

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MTConnect
{
    /// <summary>
    /// Helpers for configuring the cppagent-compatible JSON serializer
    /// (option sets and one-shot convert wrappers around
    /// <see cref="JsonSerializer"/>). The shared option sets switch off
    /// indentation by default, ignore properties with default values,
    /// allow numbers to be read from strings (a common cppagent edge),
    /// and keep property lookup case-insensitive to tolerate equipment
    /// payload variation.
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
        // 2026-08. The cppagent-format assembly ships 25 Streams.Json
        // classes (2.5× the plain-JSON count), so the LCG cost per
        // serialisation is proportionally larger. The instances below
        // are created once and reused; JsonSerializerOptions is
        // thread-safe for read after its first (de)serialization, and
        // nothing in this file mutates them after construction.
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
        /// Default serializer options used when no <c>indentOutput</c>
        /// option is requested. Produces compact JSON, omits properties
        /// at their default value, allows numbers to be read from
        /// strings, and ignores property-name casing.
        /// </summary>
        public static JsonSerializerOptions DefaultOptions => _defaultOptions;

        /// <summary>
        /// Pretty-printed serializer options used when the
        /// <c>indentOutput</c> formatter option is enabled; otherwise
        /// identical to <see cref="DefaultOptions"/>.
        /// </summary>
        public static JsonSerializerOptions IndentOptions => _indentOptions;


        /// <summary>
        /// Serializes <paramref name="obj"/> to a JSON string using the
        /// cppagent option defaults, optionally with an extra converter
        /// and pretty-printing. Returns <c>null</c> on any
        /// serialization failure or when the input is null.
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
        /// Serializes <paramref name="obj"/> to a UTF-8 byte array
        /// using the cppagent option defaults, optionally with an extra
        /// converter and pretty-printing. Returns <c>null</c> on any
        /// serialization failure or when the input is null.
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
        /// Serializes <paramref name="obj"/> into a fresh
        /// <see cref="MemoryStream"/> using the cppagent option
        /// defaults, optionally with an extra converter and
        /// pretty-printing. Returns <c>null</c> on any serialization
        /// failure or when the input is null.
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
