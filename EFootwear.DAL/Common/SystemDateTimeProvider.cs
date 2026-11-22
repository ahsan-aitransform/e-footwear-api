using EFootwear.BLL.Abstractions;

namespace EFootwear.DAL.Common;

/// <summary>The real clock. Tests replace <see cref="IDateTimeProvider"/> with a fixed one.</summary>
public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
