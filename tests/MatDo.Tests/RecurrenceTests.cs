using MatDo.Services;

namespace MatDo.Tests;

public sealed class RecurrenceTests
{
    [Theory]
    [InlineData("d:1", "2026-10-19", "2026-10-19", "2026-10-20")]
    [InlineData("d:3", "2026-10-19", "2026-10-19", "2026-10-22")]
    [InlineData("d:1", "2026-10-10", "2026-10-19", "2026-10-20")] // overdue: into the future
    [InlineData("wd", "2026-10-23", "2026-10-23", "2026-10-26")] // Friday -> Monday
    [InlineData("w:1", "2026-10-19", "2026-10-19", "2026-10-26")]
    [InlineData("w:1:3", "2026-10-19", "2026-10-19", "2026-10-21")] // Monday -> Wednesday
    [InlineData("w:1:1", "2026-10-19", "2026-10-19", "2026-10-26")]
    [InlineData("w:2:1", "2026-10-19", "2026-10-19", "2026-11-02")]
    [InlineData("m:1", "2026-01-31", "2026-01-31", "2026-02-28")]
    [InlineData("m:1", "2026-01-31", "2026-02-28", "2026-03-31")] // the 31st comes back
    [InlineData("m:1:15", "2026-10-19", "2026-10-19", "2026-11-15")]
    [InlineData("m:1:31", "2026-10-31", "2026-10-31", "2026-11-30")]
    [InlineData("y:1", "2026-02-28", "2026-02-28", "2027-02-28")]
    public void Next(string rule, string due, string today, string expected)
        => Assert.Equal(DateOnly.Parse(expected), Recurrence.Next(rule, DateOnly.Parse(due), DateOnly.Parse(today)));

    [Theory]
    [InlineData("d:1", true)]
    [InlineData("wd", true)]
    [InlineData("x:1", false)]
    [InlineData("d:0", false)]
    [InlineData("", false)]
    public void Valid(string rule, bool valid) => Assert.Equal(valid, Recurrence.IsValid(rule));

    [Fact]
    public void Describe() => Assert.Equal("Jeden Montag", Recurrence.Describe("w:1:1"));
}
