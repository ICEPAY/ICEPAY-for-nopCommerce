using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.SQLite;
using LinqToDB.Mapping;
using Moq;
using Nop.Data;
using Nop.Plugin.Payments.ICEPAY.Domain;
using Nop.Plugin.Payments.ICEPAY.Services;
using NUnit.Framework;

namespace ICEPAY.Tests;

public class DatabaseLockTests
{
    [Test]
    public async Task Database_lock_rejects_competitors_and_releases_after_failure()
    {
        var mapping = new MappingSchema();
        new FluentMappingBuilder(mapping).Entity<ICEPAYOrderLock>().HasPrimaryKey(x => x.Id).HasIdentity(x => x.Id).Build();
        using var db = new DataConnection(new DataOptions().UseSQLite("Data Source=:memory:", SQLiteProvider.Microsoft).UseMappingSchema(mapping));
        var table = db.CreateTable<ICEPAYOrderLock>();
        db.Insert(new ICEPAYOrderLock { Id = 1, OrderId = 42 });
        var repository = new Mock<IRepository<ICEPAYOrderLock>>();
        repository.Setup(x => x.Table).Returns(table);
        var store = new ICEPAYStore(Mock.Of<IRepository<ICEPAYPaymentAttempt>>(), Mock.Of<IRepository<ICEPAYRefundRequest>>(), repository.Object);
        await store.WithOrderLockAsync(42, async () =>
        {
            Assert.ThrowsAsync<InvalidOperationException>(() => store.WithOrderLockAsync(42, () => Task.FromResult(1)));
            await Task.Yield();
            return 1;
        });
        Assert.ThrowsAsync<ArgumentException>(() => store.WithOrderLockAsync<int>(42, () => throw new ArgumentException("Failed")));
        Assert.That(await store.WithOrderLockAsync(42, () => Task.FromResult(7)), Is.EqualTo(7));
        Assert.That((await table.SingleAsync()).Owner, Is.Empty);
    }

    [Test]
    public async Task Touching_reconciliation_time_does_not_overwrite_a_concurrent_payment_update()
    {
        var mapping = new MappingSchema();
        new FluentMappingBuilder(mapping).Entity<ICEPAYPaymentAttempt>().HasPrimaryKey(x => x.Id).HasIdentity(x => x.Id).Build();
        using var db = new DataConnection(new DataOptions().UseSQLite("Data Source=:memory:", SQLiteProvider.Microsoft).UseMappingSchema(mapping));
        var table = db.CreateTable<ICEPAYPaymentAttempt>();
        var id = db.InsertWithInt32Identity(new ICEPAYPaymentAttempt { OrderId = 42, Applied = true, ReviewRequired = true });
        var repository = new Mock<IRepository<ICEPAYPaymentAttempt>>();
        repository.Setup(x => x.Table).Returns(table);
        var store = new ICEPAYStore(repository.Object, Mock.Of<IRepository<ICEPAYRefundRequest>>(), Mock.Of<IRepository<ICEPAYOrderLock>>());
        await store.TouchAsync(id);
        var attempt = await table.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(attempt.Applied, Is.True);
            Assert.That(attempt.ReviewRequired, Is.True);
            Assert.That(attempt.LastCheckedOnUtc, Is.GreaterThan(DateTime.UnixEpoch));
        });
    }
}
