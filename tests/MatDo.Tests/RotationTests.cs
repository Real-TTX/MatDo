using MatDo.Services;

namespace MatDo.Tests;

public sealed class RotationTests
{
    [Fact]
    public void Hands_over_to_the_next_person_in_order()
    {
        long[] pool = [10, 20, 30];
        Assert.Equal(20, TaskService.NextAssignee(pool, 10));
        Assert.Equal(30, TaskService.NextAssignee(pool, 20));
    }

    [Fact]
    public void Wraps_round_after_the_last_person()
    {
        long[] pool = [10, 20, 30];
        Assert.Equal(10, TaskService.NextAssignee(pool, 30));
    }

    [Fact]
    public void Starts_at_the_first_person_when_nobody_is_assigned_yet()
    {
        long[] pool = [10, 20, 30];
        Assert.Equal(10, TaskService.NextAssignee(pool, null));
    }

    [Fact]
    public void Starts_at_the_first_person_when_the_current_one_left_the_pool()
    {
        long[] pool = [10, 20, 30];
        Assert.Equal(10, TaskService.NextAssignee(pool, 99));
    }

    [Fact]
    public void A_single_person_keeps_the_task()
    {
        long[] pool = [10];
        Assert.Equal(10, TaskService.NextAssignee(pool, 10));
    }

    [Fact]
    public void An_empty_pool_leaves_the_assignee_as_it_is()
    {
        Assert.Equal(42, TaskService.NextAssignee([], 42));
        Assert.Null(TaskService.NextAssignee([], null));
    }
}
