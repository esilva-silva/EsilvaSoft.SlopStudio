using EsilvaSoft.SlopStudio.Application.Agents;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class AgentToolInvocationQuotaTests
{
    [Test]
    public void SessionLimitReleasesACompletedLease()
    {
        var quota = new AgentToolInvocationQuota();
        var session = Guid.NewGuid();
        using var first = quota.TryEnter(session, Guid.NewGuid(), Guid.NewGuid());
        using var second = quota.TryEnter(session, Guid.NewGuid(), Guid.NewGuid());
        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Not.Null);
        Assert.That(quota.TryEnter(session, Guid.NewGuid(), Guid.NewGuid()), Is.Null);

        first!.Dispose();
        using var replacement = quota.TryEnter(session, Guid.NewGuid(), Guid.NewGuid());
        Assert.That(replacement, Is.Not.Null);
    }

    [Test]
    public void ConnectionAndGlobalLimitsApplyAcrossSessions()
    {
        var quota = new AgentToolInvocationQuota();
        var sharedConnection = Guid.NewGuid();
        var leases = Enumerable.Range(0, 4).Select(_ =>
            quota.TryEnter(Guid.NewGuid(), Guid.NewGuid(), sharedConnection)).ToArray();
        try
        {
            Assert.That(leases, Has.All.Not.Null);
            Assert.That(quota.TryEnter(Guid.NewGuid(), Guid.NewGuid(), sharedConnection), Is.Null);

            var otherConnections = Enumerable.Range(0, 4).Select(_ =>
                quota.TryEnter(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())).ToArray();
            try
            {
                Assert.That(otherConnections, Has.All.Not.Null);
                Assert.That(quota.TryEnter(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), Is.Null);
                leases[0]!.Dispose();
                using var replacement = quota.TryEnter(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
                Assert.That(replacement, Is.Not.Null);
            }
            finally
            {
                foreach (var lease in otherConnections) lease?.Dispose();
            }
        }
        finally
        {
            foreach (var lease in leases) lease?.Dispose();
        }
    }

    [Test]
    public void TwentyCallsPerTurnStayConsumedAfterLeaseRelease()
    {
        var quota = new AgentToolInvocationQuota();
        var session = Guid.NewGuid();
        var turn = Guid.NewGuid();
        for (var i = 0; i < 20; i++)
        {
            using var lease = quota.TryEnter(session, turn, null);
            Assert.That(lease, Is.Not.Null, $"Call {i + 1} should be admitted.");
        }
        Assert.That(quota.TryEnter(session, turn, null), Is.Null);
        using var nextTurn = quota.TryEnter(session, Guid.NewGuid(), null);
        Assert.That(nextTurn, Is.Not.Null);
    }

    [Test]
    public void CancelledSourceKeepsItsSlotUntilItsTaskCompletes()
    {
        var quota = new AgentToolInvocationQuota();
        var session = Guid.NewGuid();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledCall = quota.TryEnter(session, Guid.NewGuid(), null);
        Assert.That(cancelledCall, Is.Not.Null);
        cancelledCall!.HoldUntil(pending.Task);
        cancelledCall.Dispose();

        using var second = quota.TryEnter(session, Guid.NewGuid(), null);
        Assert.That(second, Is.Not.Null);
        Assert.That(quota.TryEnter(session, Guid.NewGuid(), null), Is.Null);
        pending.SetResult();
        Assert.That(SpinWait.SpinUntil(() =>
        {
            using var recovered = quota.TryEnter(session, Guid.NewGuid(), null);
            return recovered is not null;
        }, TimeSpan.FromSeconds(2)), Is.True);
    }
}
