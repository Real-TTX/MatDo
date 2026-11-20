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
    [InlineData("w:1:1,4", "2026-10-19", "2026-10-19", "2026-10-22")] // Monday -> Thursday
    [InlineData("w:1:1,4", "2026-10-22", "2026-10-22", "2026-10-26")] // Thursday -> Monday
    [InlineData("w:2:1,4", "2026-10-22", "2026-10-22", "2026-11-02")] // every other week: the Monday after the next
    [InlineData("w:2:3", "2026-10-19", "2026-10-19", "2026-10-21")] // a due date off the rule: the first Wednesday, then every other
    [InlineData("m:1:L", "2026-10-31", "2026-10-31", "2026-11-30")]
    [InlineData("m:1:L", "2027-01-31", "2027-01-31", "2027-02-28")]
    [InlineData("m:1:Lx5", "2026-10-30", "2026-10-30", "2026-11-27")]
    [InlineData("m:1:1x1", "2026-11-02", "2026-11-02", "2026-12-07")]
    [InlineData("m:1:3x3", "2026-10-21", "2026-10-21", "2026-11-18")]
    public void Next(string rule, string due, string today, string expected)
        => Assert.Equal(DateOnly.Parse(expected), Recurrence.Next(rule, DateOnly.Parse(due), DateOnly.Parse(today)));

    [Theory]
    [InlineData("d:1", true)]
    [InlineData("wd", true)]
    [InlineData("x:1", false)]
    [InlineData("d:0", false)]
    [InlineData("", false)]
    [InlineData("w:1:1,4", true)]
    [InlineData("w:1:1,8", false)]
    [InlineData("m:1:L", true)]
    [InlineData("m:1:Lx5", true)]
    [InlineData("m:1:5x1", false)]
    [InlineData("m:3;c;u=2026-12-31", true)]
    [InlineData("d:1;z", false)]
    public void Valid(string rule, bool valid) => Assert.Equal(valid, Recurrence.IsValid(rule));

    [Theory]
    [InlineData("wd")]
    [InlineData("w:2:1,3,5")]
    [InlineData("m:1:L;c")]
    [InlineData("m:1:Lx5;u=2027-01-31")]
    [InlineData("m:3;c;u=2026-12-31")]
    public void A_rule_reads_back_to_its_text(string rule) => Assert.Equal(rule, Recurrence.Parse(rule)!.ToString());

    [Theory]
    [InlineData("m:3;c", "2026-01-10", "2026-01-20", "2026-04-20")] // from the check-off, not from the due date
    [InlineData("m:3", "2026-01-10", "2026-01-20", "2026-04-10")]
    [InlineData("w:1:1;u=2026-10-26", "2026-10-19", "2026-10-19", "2026-10-26")]
    [InlineData("w:1:1;u=2026-10-25", "2026-10-19", "2026-10-19", null)] // past the end: done for good
    public void NextDue(string rule, string due, string today, string? expected)
        => Assert.Equal(expected is null ? null : DateOnly.Parse(expected), Recurrence.NextDue(rule, DateOnly.Parse(due), DateOnly.Parse(today)));

    [Fact]
    public void Describe()
    {
        string De(string rule) => MatDo.Localization.Languages.In("de", () => Recurrence.Describe(rule));
        string En(string rule) => MatDo.Localization.Languages.In("en", () => Recurrence.Describe(rule));
        Assert.Equal("Jeden Montag", De("w:1:1"));
        Assert.Equal("Every Monday", En("w:1:1"));
        Assert.Equal("Jeden Montag, Mittwoch und Freitag", De("w:1:1,3,5"));
        Assert.Equal("Every Monday and Thursday", En("w:1:1,4"));
        Assert.Equal("Jeden letzten Freitag im Monat", De("m:1:Lx5"));
        Assert.Equal("Every first Monday of the month", En("m:1:1x1"));
        Assert.Equal("Jeden Monat am letzten Tag", De("m:1:L"));
        Assert.Equal("Alle 3 Monate ab Erledigung bis 31. Dez. 2026", De("m:3;c;u=2026-12-31"));
    }
}
