// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Buffers;
using System.Text;
using System.Text.Json;
using Player.Api.Features.Applications;
using Player.Api.Infrastructure.JsonConverters;

namespace Player.Api.Tests.Infrastructure.JsonConverters;

/// <summary>
/// Applied to <c>Embeddable</c> and <c>LoadInBackground</c> on the create-application command so older clients
/// that quote their booleans keep working. Anything it rejects is a 400 for the caller, so the accepted set is
/// part of that endpoint's contract.
/// </summary>
public class StringToBooleanConverterTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void A_json_boolean_reads_as_itself(string json, bool expected)
    {
        Assert.Equal(expected, Read(json));
    }

    /// <summary>
    /// The reason the converter exists. Casing and surrounding whitespace are whatever <c>bool.TryParse</c>
    /// accepts, so the .NET-style "True" a client may have echoed back from an older response is fine.
    /// </summary>
    [Theory]
    [InlineData("\"true\"", true)]
    [InlineData("\"false\"", false)]
    [InlineData("\"True\"", true)]
    [InlineData("\"FALSE\"", false)]
    [InlineData("\" true \"", true)]
    public void A_quoted_boolean_reads_as_a_boolean(string json, bool expected)
    {
        Assert.Equal(expected, Read(json));
    }

    /// <summary>
    /// Null and empty string both mean "not specified" rather than false — the property is nullable and the
    /// handler treats absent as its own case.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    public void Nothing_supplied_reads_as_null(string json)
    {
        Assert.Null(Read(json));
    }

    /// <summary>
    /// No coercion of 1/0/yes — a client sending one of those is told so rather than silently getting false.
    /// </summary>
    [Theory]
    [InlineData("\"yes\"")]
    [InlineData("\"1\"")]
    [InlineData("\"tru\"")]
    public void An_unparseable_string_is_rejected(string json)
    {
        var exception = Assert.Throws<JsonException>(() => { Read(json); });

        Assert.Contains("Unable to convert", exception.Message);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void A_non_string_non_boolean_token_is_rejected(string json)
    {
        var exception = Assert.Throws<JsonException>(() => { Read(json); });

        Assert.Contains("Unexpected token type", exception.Message);
    }

    /// <summary>
    /// Writing is plain, so the round trip normalizes a quoted input to a real JSON boolean — a value read back
    /// from the API is never a string.
    /// </summary>
    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    [InlineData(null, "null")]
    public void Writing_always_produces_a_json_boolean(bool? value, string expected)
    {
        Assert.Equal(expected, Write(value));
    }

    /// <summary>Pins that the converter is actually attached to the request it was written for.</summary>
    [Fact]
    public void The_create_application_command_accepts_quoted_booleans()
    {
        var command = JsonSerializer.Deserialize<Create.Command>(
            """{"embeddable":"true","loadInBackground":"false"}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.True(command.Embeddable);
        Assert.False(command.LoadInBackground);
    }

    /// <summary>
    /// Driven directly rather than through <see cref="JsonSerializer"/>: a converter for a nullable type does
    /// not handle null by default, so the serializer would answer the null cases without ever calling it.
    /// </summary>
    private static bool? Read(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        return new StringToBooleanConverter().Read(ref reader, typeof(bool?), JsonSerializerOptions.Default);
    }

    private static string Write(bool? value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);

        new StringToBooleanConverter().Write(writer, value, JsonSerializerOptions.Default);
        writer.Flush();

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
