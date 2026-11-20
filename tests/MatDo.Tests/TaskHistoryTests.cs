using MatDo.Services;

namespace MatDo.Tests;

public sealed class TaskHistoryTests
{
    private static readonly DateOnly Sat = new(2026, 11, 21);
    private static readonly DateOnly NextSat = new(2026, 11, 28);

    [Fact]
    public void A_repeating_task_can_be_taken_back_while_it_stands_where_the_check_off_put_it()
        => Assert.True(TaskService.CanUndo(isCompleted: false, dueDate: NextSat, nextDue: NextSat));

    [Fact]
    public void A_repeating_task_that_was_moved_since_cannot_be_taken_back()
        => Assert.False(TaskService.CanUndo(isCompleted: false, dueDate: NextSat.AddDays(1), nextDue: NextSat));

    [Fact]
    public void A_completed_task_can_be_taken_back_by_the_check_off_that_completed_it()
        => Assert.True(TaskService.CanUndo(isCompleted: true, dueDate: Sat, nextDue: null));

    [Fact]
    public void A_completed_task_is_not_taken_back_by_an_older_check_off_of_its_repeating_days()
        => Assert.False(TaskService.CanUndo(isCompleted: true, dueDate: Sat, nextDue: NextSat));

    [Fact]
    public void An_open_task_whose_check_off_completed_it_has_nothing_to_take_back()
        => Assert.False(TaskService.CanUndo(isCompleted: false, dueDate: Sat, nextDue: null));
}
