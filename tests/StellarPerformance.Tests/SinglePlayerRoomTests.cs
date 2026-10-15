using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ships;
using Ships.Rooms;
using Ships.Rooms.Model;
using Ships.Interface.Model;
using UnityEngine;
using Xunit;

namespace StellarPerformance.Tests;

public sealed class SinglePlayerRoomTests
{
    private static ShipRooms Rooms(ushort id, int walls)
    {
        var room = (ShipRooms)RuntimeHelpers.GetUninitializedObject(typeof(ShipRooms));
        var tree = new ShipRoomsVoxelsTree(); var root = tree.GetLeaf(Vector3Int.zero);
        root.RoomId = id;
        AccessTools.Field(typeof(ShipRoomsVoxelsTreeNode), "<WallCount>k__BackingField").SetValue(root, (byte)walls);
        AccessTools.Field(typeof(ShipRooms), "<Tree>k__BackingField").SetValue(room, tree);
        return room;
    }
    [Fact]
    public void SharedQueriesFollowAuthorityWithoutSharingMutableClientGeometry()
    {
        var local = Rooms(0, 0); var authority = Rooms(12, 1);
        var client = (TrackedShipClient)RuntimeHelpers.GetUninitializedObject(typeof(TrackedShipClient));
        SinglePlayerRooms.Track(local, client, authority);
        var result = default(RoomSampleResult);
        Assert.False(SinglePlayerRooms.BeforeSample(local, Vector3.one * -.5f, ref result));
        Assert.Equal((ushort)12, result.RoomId); Assert.True(result.IsWall);
        Assert.False(SinglePlayerRooms.BeforeMutation(local));
        Assert.True(SinglePlayerRooms.BeforeMutation(authority));
        Assert.NotSame(authority.Tree, local.Tree);
        Assert.Equal(0, local.Tree.GetLeaf(Vector3Int.zero).WallCount);
        authority.Tree.GetLeaf(Vector3Int.zero).RoomId = 3;
        SinglePlayerRooms.BeforeSample(local, Vector3.one * -.5f, ref result);
        Assert.Equal((ushort)3, result.RoomId);
    }
    [Fact]
    public void OrdinaryRoomsRetainNativeQueriesAndMutationPaths()
    {
        var room = Rooms(7, 0); var result = default(RoomSampleResult);
        Assert.True(SinglePlayerRooms.BeforeMutation(room));
        Assert.True(SinglePlayerRooms.BeforeSample(room, Vector3.zero, ref result));
        SinglePlayerRooms.Restore(room);
        Assert.Equal((ushort)7, room.Tree.GetLeaf(Vector3Int.zero).RoomId);
    }
    [Fact]
    public void DormantDebugComponentDoesNotBuildTheClientRoomTree()
    {
        SinglePlayerRooms.Bind();
        var local = Rooms(0, 0); var authority = Rooms(12, 1);
        var client = (TrackedShipClient)RuntimeHelpers.GetUninitializedObject(typeof(TrackedShipClient));
        var visualizer = (Ships.Rooms.Tests.ShipRoomGridVisualizer)RuntimeHelpers.GetUninitializedObject(typeof(Ships.Rooms.Tests.ShipRoomGridVisualizer));
        SinglePlayerRooms.Track(local, client, authority);
        AccessTools.Method(typeof(SinglePlayerRooms), "BeforeVisualizer").Invoke(null, new object[] { visualizer, local });
        AccessTools.Method(typeof(SinglePlayerRooms), "BeforeVisualizerUpdate").Invoke(null, new object[] { visualizer });
        Assert.False(SinglePlayerRooms.BeforeMutation(local));
        Assert.Single(local.Tree.GetLeaves());
    }
}
