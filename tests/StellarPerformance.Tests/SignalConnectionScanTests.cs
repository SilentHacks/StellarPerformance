using System;
using System.Collections.Generic;
using System.Linq;
using Ships.Cables.Model;
using Ships.Interface.Model.Parts.Common;
using Xunit;

namespace StellarPerformance.Tests;

[Collection("Diagnostic counters")]
public sealed class SignalConnectionScanTests
{
    private static SocketConnection Connection(int id) => new SocketConnection
    {
        SocketA = new EntityPartSocket(1, (ushort)(id % 20), (byte)(id % 3)),
        SocketB = new EntityPartSocket(2, (ushort)((id + 1) % 20), (byte)((id + 1) % 3)),
        CableId = (uint)id
    };
    [Fact]
    public void UnionMatchesNativeOrderThroughNativeAndDirectSetMutations()
    {
        SignalConnectionScan.Clear();
        try
        {
            var group = new ConnectionGroup(1); var random = new Random(211026);
            for (var i = 0; i < 3000; i++)
            {
                var connection = Connection(random.Next(80));
                if (i % 3 == 0) group.RemoveConnection(connection); else group.AddConnection(connection);
                if (i % 23 == 0 && group.Sockets.Count > 0)
                {
                    var key = group.Sockets.Keys.First();
                    group.Sockets[key] = new HashSet<SocketConnection>(group.Sockets[key].Reverse());
                }
                if (i % 31 == 0 && group.Sockets.Count > 0)
                {
                    var set = group.Sockets.Values.First();
                    if (set.Count > 0) { set.Remove(set.First()); set.Add(Connection(100 + i)); }
                }
                Assert.Equal(group.AllConnections.ToArray(), SignalConnectionScan.Read(group).ToArray());
                Assert.Same(SignalConnectionScan.Read(group), SignalConnectionScan.Read(group));
            }
            group.Sockets.Clear(); Assert.Empty(SignalConnectionScan.Read(group));
            group.AddConnection(Connection(99)); Assert.Equal(group.AllConnections.ToArray(), SignalConnectionScan.Read(group).ToArray());
        }
        finally { SignalConnectionScan.Clear(); }
    }
    [Fact]
    public void IndependentGroupsAndOversizedInputsRetainNativeMembership()
    {
        SignalConnectionScan.Clear();
        try
        {
            for (var n = 0; n < 1100; n++)
            {
                var group = new ConnectionGroup((uint)n); group.AddConnection(Connection(n));
                Assert.Equal(group.AllConnections.ToArray(), SignalConnectionScan.Read(group).ToArray());
            }
            var large = new ConnectionGroup(9999);
            for (var i = 0; i < 4200; i++) large.Sockets[new EntityPartSocket(3, (ushort)i, 0)] = new HashSet<SocketConnection> { Connection(i) };
            Assert.Equal(large.AllConnections.ToArray(), SignalConnectionScan.Read(large).ToArray());
        }
        finally { SignalConnectionScan.Clear(); }
    }
}
