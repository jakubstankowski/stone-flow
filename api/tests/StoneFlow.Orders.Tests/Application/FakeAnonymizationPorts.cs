using Microsoft.Extensions.Time.Testing;
using StoneFlow.Orders.Application.Abstractions;
using StoneFlow.Orders.Application.AnonymizeCustomer;
using StoneFlow.Orders.Domain.Orders;
using StoneFlow.Orders.Domain.Payments;
using StoneFlow.Shared.Application.Audit;
using StoneFlow.Shared.Application.Persistence;

namespace StoneFlow.Orders.Tests.Application;

/// <summary>Fałszywe porty anonimizacji (SF-22) ze wspólnym dziennikiem wywołań.</summary>
internal sealed class AnonymizationFakes
{
    public static readonly byte[] Version = [0, 0, 0, 0, 0, 0, 0, 1];
    public static readonly byte[] NewVersion = [0, 0, 0, 0, 0, 0, 0, 2];

    public AnonymizationFakes(DateTimeOffset now)
    {
        Time = new FakeTimeProvider(now);
        UnitOfWork = new FakeUnitOfWork(Calls);
        Lock = new FakeOrderLock(this);
        Store = new FakeOrderStore(this);
        EntryStore = new FakeAnonymizationStore(this);
        Activity = new FakeAuditActivity(this);
        Redaction = new FakeAuditRedaction(this);
        AuditLog = new FakeAuditLog(this);
        Settings = new FakeSettings();
    }

    public List<string> Calls { get; } = [];

    public FakeTimeProvider Time { get; }

    public FakeUnitOfWork UnitOfWork { get; }

    public FakeOrderLock Lock { get; }

    public FakeOrderStore Store { get; }

    public FakeAnonymizationStore EntryStore { get; }

    public FakeAuditActivity Activity { get; }

    public FakeAuditRedaction Redaction { get; }

    public FakeAuditLog AuditLog { get; }

    public FakeSettings Settings { get; }

    public Order? Order { get; set; }

    public AnonymizationRetention Retention => new(Settings, EntryStore, Activity, Time);

    public AnonymizeCustomerHandler Handler() =>
        new(UnitOfWork, AuditLog, Redaction, Lock, Store, EntryStore, Retention, Time);

    internal sealed class FakeSettings : IAnonymizationSettings
    {
        public int? RetentionMonths { get; set; }
    }

    internal sealed class FakeUnitOfWork(List<string> calls) : IUnitOfWork
    {
        public bool? Committed { get; private set; }

        public Func<object, bool>? CommitWhen { get; private set; }

        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, Func<T, bool> commitWhen, CancellationToken cancellationToken)
        {
            calls.Add("begin");
            CommitWhen = result => commitWhen((T)result);
            var result = await work(cancellationToken);
            Committed = commitWhen(result);
            return result;
        }
    }

    internal sealed class FakeOrderLock(AnonymizationFakes fakes) : IOrderLock
    {
        public Task<LockedOrder?> LockAsync(Guid orderId, CancellationToken cancellationToken)
        {
            fakes.Calls.Add("lock");
            return Task.FromResult(fakes.Order is { } order && order.Id == orderId
                ? new LockedOrder(order.Number, order.Value, order.IsCancelled, 0, 0, 0, 0)
                : null);
        }
    }

    internal sealed class FakeOrderStore(AnonymizationFakes fakes) : IOrderStore
    {
        public StoreSaveStatus SaveStatus { get; set; } = StoreSaveStatus.Saved;

        public Task<StoredOrder?> LoadAsync(Guid id, CancellationToken cancellationToken)
        {
            fakes.Calls.Add("load");
            return Task.FromResult(fakes.Order is { } order && order.Id == id ? new StoredOrder(order, Version) : null);
        }

        public Task<StoreSaveResult> AddAsync(Order order, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<StoreSaveResult> SaveAsync(Order order, byte[] expectedVersion, CancellationToken cancellationToken)
        {
            fakes.Calls.Add("save");
            return Task.FromResult(SaveStatus == StoreSaveStatus.Saved
                ? new StoreSaveResult(StoreSaveStatus.Saved, NewVersion)
                : new StoreSaveResult(SaveStatus));
        }
    }

    internal sealed class FakeAnonymizationStore(AnonymizationFakes fakes) : IOrderAnonymizationStore
    {
        public EntryRetentionFacts Facts { get; set; } = new(null, null);

        public List<Refund> Refunds { get; } = [];

        public List<SalesCorrection> Corrections { get; } = [];

        public Task<EntryRetentionFacts> GetRetentionFactsAsync(Guid orderId, CancellationToken cancellationToken)
        {
            fakes.Calls.Add("facts");
            return Task.FromResult(Facts);
        }

        public Task<ReasonEntries> LoadReasonEntriesAsync(Guid orderId, CancellationToken cancellationToken)
        {
            fakes.Calls.Add("entries");
            return Task.FromResult(new ReasonEntries(Refunds, Corrections));
        }
    }

    internal sealed class FakeAuditActivity(AnonymizationFakes fakes) : IAuditActivity
    {
        public DateTime? LastRecordedAtUtc { get; set; }

        public Task<DateTime?> LastRecordedAtUtcAsync(AuditObjectReference root, CancellationToken cancellationToken)
        {
            fakes.Calls.Add("activity");
            return Task.FromResult(LastRecordedAtUtc);
        }
    }

    internal sealed class FakeAuditRedaction(AnonymizationFakes fakes) : IAuditRedaction
    {
        public List<AuditRedaction> Redactions { get; } = [];

        public Task<int> RedactAsync(AuditRedaction redaction, CancellationToken cancellationToken)
        {
            fakes.Calls.Add("redact");
            Redactions.Add(redaction);
            return Task.FromResult(0);
        }
    }

    internal sealed class FakeAuditLog(AnonymizationFakes fakes) : IAuditLog
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
        {
            fakes.Calls.Add("audit");
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}
