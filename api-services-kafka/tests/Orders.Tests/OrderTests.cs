using Contracts;
using Orders.Api.Domain;

namespace Orders.Tests;

public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static Order NewOrder() =>
        Order.Create([new OrderLine(Guid.NewGuid(), 1)], Now);

    [Fact]
    public void Create_starts_pending()
    {
        var order = NewOrder();

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Single(order.Items);
        Assert.Null(order.RejectionReason);
    }

    [Fact]
    public void Confirm_from_pending_returns_true()
    {
        var order = NewOrder();

        Assert.True(order.Confirm(Now));
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void Confirm_after_rejected_returns_false_and_keeps_rejected()
    {
        var order = NewOrder();
        order.Reject("no stock", Now);

        Assert.False(order.Confirm(Now));
        Assert.Equal(OrderStatus.Rejected, order.Status);
    }

    [Fact]
    public void Reject_cuts_a_reason_that_is_longer_than_the_column()
    {
        var order = NewOrder();

        Assert.True(order.Reject(new string('r', 2000), Now));

        Assert.Equal(Order.MaxReasonLength, order.RejectionReason!.Length);
    }

    [Fact]
    public void Reject_stores_reason()
    {
        var order = NewOrder();

        Assert.True(order.Reject("no stock", Now));
        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal("no stock", order.RejectionReason);
    }
}
