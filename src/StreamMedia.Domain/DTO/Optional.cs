using System.Text.Json;
using System.Text.Json.Serialization;

namespace StreamMedia.Domain.DTO;

/// <summary>
/// Distingue "el campo no vino" (<c>IsSet == false</c>) de "vino null" (<c>IsSet == true</c>, <c>Value == null</c>).
/// </summary>
[JsonConverter(typeof(OptionalConverterFactory))]
public readonly record struct Optional<T>(bool IsSet, T? Value);

public sealed class OptionalConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(OptionalConverter<>).MakeGenericType(type.GetGenericArguments()[0]))!;

    private sealed class OptionalConverter<T> : JsonConverter<Optional<T>>
    {
        // Solo se invoca si la propiedad vino en el JSON; si falta queda default (IsSet = false).
        public override Optional<T> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            new(true, JsonSerializer.Deserialize<T>(ref reader, options));

        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.Value, options);
    }
}