// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Player.Api.Infrastructure.JsonConverters;

/// <summary>
/// JSON converter that accepts both boolean and string representations of boolean values
/// Allows "true"/"false" strings to be deserialized as booleans for backwards compatibility
/// </summary>
public class StringToBooleanConverter : JsonConverter<bool?>
{
    public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
                return true;
            case JsonTokenType.False:
                return false;
            case JsonTokenType.String:
                var stringValue = reader.GetString();
                if (string.IsNullOrEmpty(stringValue))
                    return null;
                if (bool.TryParse(stringValue, out var boolValue))
                    return boolValue;
                throw new JsonException($"Unable to convert \"{stringValue}\" to boolean");
            case JsonTokenType.Null:
                return null;
            default:
                throw new JsonException($"Unexpected token type: {reader.TokenType}");
        }
    }

    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteBooleanValue(value.Value);
        else
            writer.WriteNullValue();
    }
}
