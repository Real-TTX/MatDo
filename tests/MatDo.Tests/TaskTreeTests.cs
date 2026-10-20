using MatDo.Services;

namespace MatDo.Tests;

public sealed class TaskTreeTests
{
    private static TaskRow Row(long id, long? parent = null)
        => new(id, $"T{id}", null, 4, null, null, null, null, false, null, 1, "Inbox", "#808080", true, null, parent, null, (int)id, DateTime.UtcNow, [], 0, 0, 0);

    [Fact]
    public void Each_task_is_followed_by_its_sub_tasks_to_any_depth_in_the_order_they_came()
    {
        // 1 ─ 3 ─ 5      2
        //   └ 4
        var tree = TaskService.Tree([Row(1), Row(2), Row(3, 1), Row(4, 1), Row(5, 3)]);
        Assert.Equal([1L, 3, 5, 4, 2], tree.Select(r => r.Id));
        Assert.Equal([0, 1, 2, 1, 0], tree.Select(r => r.Depth));
        Assert.Equal([2, 1, 0, 0, 0], tree.Select(r => r.Children));
    }

    [Fact]
    public void A_sub_task_whose_task_is_not_in_the_list_stands_on_its_own()
    {
        var tree = TaskService.Tree([Row(7, 99), Row(8)]);
        Assert.Equal([7L, 8], tree.Select(r => r.Id));
        Assert.All(tree, r => Assert.Equal(0, r.Depth));
    }
}
