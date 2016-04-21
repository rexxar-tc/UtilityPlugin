using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using SEModAPIExtensions.API;
using UtilityPlugin.Utility;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace UtilityPlugin.ProcessHandlers
{
    public class ProcessGrindHandler : ProcessHandlerBase
    {
        private static DisconnectHelper disconnect = new DisconnectHelper();

        public override int GetUpdateResolution()
        {
            return 500;
        }

        public override void Handle()
        {
            lock ( ProcessShipyardHandler.ShipyardsList )
            {
                foreach ( ShipyardItem item in ProcessShipyardHandler.ShipyardsList.Where( item => item.YardType == ShipyardItem.ShipyardType.Grind ) )
                {
                    //check if the grid we're looking at is done grinding and ready to be deleted
                    if ( item.HasGrid )
                    {
                        if ( item.Grid?.Physics == null || item.Grid.Closed )
                        {
                            //item.HasGrid = false;
                            item.Clear();
                            continue;

                            //check if there's any other grids in our list to process
                            if ( item.YardGrids.Count < 1 )
                            {
                                item.Clear();
                                continue;
                            }

                            item.Grid = item.YardGrids[0];
                            item.YardGrids.RemoveAt( 0 );
                            //just in case this grid is also closed for some reason
                            if ( item.Grid?.Physics == null || item.Grid.Closed )
                                continue;
                        }

                        //check if the target ship has left the shipyard
                        var testBox = MathUtility.CreateOrientedBoundingBox( item.Grid );
                        if ( testBox == null )
                        {
                            UtilityPlugin.Log.Info( "grid left yard" );
                            item.Clear();
                            continue;
                        }
                        var gridBox = testBox.Value;
                        if ( item.ShipyardBox.Contains( ref gridBox ) != ContainmentType.Contains )
                        {
                            UtilityPlugin.Log.Info( "grid left yard" );
                            item.Clear();
                            continue;
                        }

                        StepGrind( item );
                        continue;
                    }

                    item.Grid.Stop();
                    foreach ( MyCubeGrid yardGrid in item.YardGrids )
                        yardGrid.Stop();

                    var allEntities = new HashSet<MyEntity>();
                    var grids = new HashSet<MyCubeGrid>();

                    //we can't get entities in a oriented bounding box, so create a sphere around the whole thing
                    //this is just to find entities in the general area, we check later against the box
                    var entitySphere = new BoundingSphereD( item.ShipyardBox.Center,
                                                            item.ShipyardBox.HalfExtent.AbsMax() );

                    Wrapper.GameAction( () => allEntities = MyEntities.GetEntities() );

                    foreach ( MyEntity entity in allEntities.Where( x => x is MyCubeGrid ) )
                    {
                        if ( entitySphere.Contains( entity.PositionComp.GetPosition() ) == ContainmentType.Contains )
                            grids.Add( entity as MyCubeGrid );
                    }

                    if ( grids.Count < 1 )
                        continue;

                    foreach ( MyCubeGrid grid in grids )
                    {
                        if ( grid?.Physics == null || grid.Closed )
                            continue;

                        //no grinding stations
                        if ( grid.Physics.IsStatic )
                            continue;

                        //create a bounding box around the ship
                        MyOrientedBoundingBoxD? testBox = MathUtility.CreateOrientedBoundingBox( grid );

                        if ( !testBox.HasValue )
                            continue;

                        MyOrientedBoundingBoxD gridBox = testBox.Value;

                        //check if the ship bounding box is completely inside the yard box
                        if ( item.ShipyardBox.Contains( ref gridBox ) == ContainmentType.Contains )
                        {
                            if ( !item.HasGrid )
                            {
                                item.HasGrid = true;
                                item.Grid = grid;
                            }
                            else
                                item.YardGrids.Add( grid );
                        }
                    }
                }
            }

            base.Handle();
        }
        
        private static void StepGrind( ShipyardItem shipyardItem )
        {
            var random = new Random();
            float grindAmount = Server.Instance.Config.GrinderSpeedMultiplier * PluginSettings.Instance.GrindMultiplier;
            HashSet<long> grindersToRemove = new HashSet<long>();
            //shorten this to grid for convenience
            MyCubeGrid grid = shipyardItem.Grid;

            if ( grid?.Physics == null || grid.Closed )
                return;

            if ( grid.BlocksCount < 1 )
                return;

            //do a raycast to see if the grinder can see the block we're assigning to it
            foreach ( IMyCubeBlock listGrinder in shipyardItem.Tools )
            {
                MySlimBlock nextBlock;
                if ( !shipyardItem.ProcessBlocks.TryGetValue( listGrinder.EntityId, out nextBlock ) )
                {
                    var tryCount = 0;

                    //TODO: optimize the try count instead of picking an arbitrary value
                    //what the hell does that mean?
                    while ( tryCount < 30 )
                    {
                        if ( grid?.Physics == null )
                            return;

                        if ( grid.Physics.LinearVelocity != Vector3D.Zero || grid.Physics.AngularVelocity != Vector3D.Zero )
                            grid.Stop();

                        //limit the number of tries so we don't get stuck in a loop forever
                        tryCount++;

                        //pick a random block. we don't really care if two grinders hit the same block, so don't check
                        if ( grid.BlocksCount > 30 )
                            nextBlock = grid.CubeBlocks.ElementAt( random.Next( 0, grid.BlocksCount - 1 ) );

                        //if we have less than 30 blocks total, just iterate through them, it's faster than going at random
                        else
                            nextBlock = grid.CubeBlocks.ElementAt( Math.Min( tryCount, grid.BlocksCount - 1 ) );
                        if ( nextBlock == null )
                            continue;

                        if ( shipyardItem.ProcessBlocks.ContainsValue( nextBlock ) )
                            continue;

                        //this raycast should give us the grid location of the first block it hits 
                        //we don't really care if it hits our random block, just grab whatever the grinder sees first
                        Vector3I? blockResult = grid.RayCastBlocks( listGrinder.GetPosition(),
                                                                    grid.GridIntegerToWorld( nextBlock.Position ) );
                        if ( !blockResult.HasValue )
                            continue;
                        
                        //TODO: remove this when my PR is merged
                        //check if removing this block will split the grid
                        if ( disconnect.TryDisconnect(grid.GetCubeBlock(blockResult.Value)) )
                        {
                            //UtilityPlugin.Log.Info( "detected split" );
                            continue;
                        }

                        nextBlock = grid.GetCubeBlock( blockResult.Value );
                        
                        break;
                    }

                    //we weren't able to find a suitable block somehow, so skip this grinder for now
                    if ( nextBlock == null )
                        continue;

                    //we found a block to pair with our grinder, add it to the dictionary and carry on with destruction
                    shipyardItem.ProcessBlocks.Add( listGrinder.EntityId, nextBlock );
                }
            }
            var tmpItemList = new List<MyPhysicalInventoryItem>();

            foreach ( IMyCubeBlock grinderBlock in shipyardItem.Tools )
            {
                var grinder = (IMyShipGrinder)grinderBlock;

                var grinderInventory = (MyInventory)grinder.GetInventory( 0 );
                MySlimBlock block;

                if ( !shipyardItem.ProcessBlocks.TryGetValue( grinderBlock.EntityId, out block ) )
                    continue;

                if ( block?.CubeGrid?.Physics == null )
                    continue;

                if ( disconnect.TryDisconnect( block ) )
                {
                    //UtilityPlugin.Log.Info( "detected split at grind" );
                    shipyardItem.ProcessBlocks.Remove( grinderBlock.EntityId );
                    continue;
                }

                Wrapper.GameAction( () =>
                {
                    var damageInfo = new MyDamageInformation( false, grindAmount, MyDamageType.Grind,
                                                              grinder.EntityId );
                    if ( block.UseDamageSystem )
                        MyDamageSystem.Static.RaiseBeforeDamageApplied( block, ref damageInfo );

                    block.DecreaseMountLevel( damageInfo.Amount, grinderInventory );
                    block.MoveItemsFromConstructionStockpile( grinderInventory );
                    if ( block.UseDamageSystem )
                        MyDamageSystem.Static.RaiseAfterDamageApplied( block, damageInfo );

                    if ( block.IsFullyDismounted )
                    {
                        if ( block.FatBlock != null && block.FatBlock.HasInventory )
                        {
                            for ( var i = 0; i < block.FatBlock.InventoryCount; ++i )
                            {
                                MyInventory blockInventory = block.FatBlock.GetInventory( i );
                                if ( blockInventory == null )
                                    continue;

                                if ( blockInventory.Empty() )
                                    continue;

                                tmpItemList.Clear();
                                tmpItemList.AddList( blockInventory.GetItems() );

                                foreach ( MyPhysicalInventoryItem item in tmpItemList )
                                    MyInventory.Transfer( blockInventory, grinderInventory, item.ItemId );
                            }
                        }

                        if ( block.UseDamageSystem )
                            MyDamageSystem.Static.RaiseDestroyed( block, damageInfo );

                        block.SpawnConstructionStockpile();
                        block.CubeGrid.RazeBlock( block.Min );
                        grindersToRemove.Add( grinderBlock.EntityId );
                    }
                } );
                foreach ( var tool in shipyardItem.Tools )
                {
                    MySlimBlock targetBlock;
                    Communication.MessageStruct message = new Communication.MessageStruct()
                    {
                        toolId = tool.EntityId,
                        gridId = 0,
                        blockPos = new SerializableVector3I( 0, 0, 0 ),
                        packedColor = 0,
                        pulse = false
                    };
                    if ( !shipyardItem.ProcessBlocks.TryGetValue( tool.EntityId, out targetBlock ) )
                    {
                        Communication.SendLine( message );
                        continue;
                    }
                    message.gridId = targetBlock.CubeGrid.EntityId;
                    message.blockPos = targetBlock.Position;
                    message.packedColor = Color.OrangeRed.PackedValue;
                    Communication.SendLine( message );
                }

                foreach ( long removeId in grindersToRemove )
                {
                    shipyardItem.ProcessBlocks.Remove( removeId );
                }
            }
        }
    }
}
