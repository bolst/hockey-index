using HockeyIndex.Api.Domain;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HockeyIndex.Api.Infrastructure.Persistence.Configurations;

internal sealed class SnakeCaseEnumConverter<TEnum>() : ValueConverter<TEnum, string>(
    value => SnakeCaseText.Of(value),
    text => SnakeCaseText.Parse<TEnum>(text))
    where TEnum : struct, Enum;
