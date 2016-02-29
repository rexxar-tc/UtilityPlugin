using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using SEModAPIExtensions.API;
using UtilityPlugin.Utility;
using VRage.Game;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRageMath;
using IMyCubeBlock = Sandbox.ModAPI.IMyCubeBlock;
using IMyCubeGrid = Sandbox.ModAPI.IMyCubeGrid;
using IMyInventory = Sandbox.ModAPI.IMyInventory;
using IMySlimBlock = Sandbox.ModAPI.IMySlimBlock;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace UtilityPlugin.ProcessHandlers
{
    public class ProcessGrindHandler : ProcessHandlerBase
    {
        public static MyDisconnectHelper disconnect = new MyDisconnectHelper();

        public override int GetUpdateResolution()
        {
            return 1000;
        }

        public override void Handle()
        {
            UtilityPlugin.Log.Info("grind update"  );
            if ( !ProcessShipyardHandler.ShipyardsList.Any() )
                return;

            foreach ( ShipyardItem item in ProcessShipyardHandler.ShipyardsList )
            {
                //if (!item.Enabled)
                //    continue;

                if ( item.YardType != ShipyardItem.ShipyardType.Grind )
                    continue;

                //we finished the grid and the entity was deleted
                if ( item.Grid == null && !item.SplitGrids.Any() )
                    item.Clear();
                //if we have any split grids in the queue, load up the next one
                else if ( item.Grid == null && item.SplitGrids.Any() )
                    item.Grid = item.SplitGrids.First();

                if ( !item.HasGrid )
                {
                    List<IMyEntity> entities = new List<IMyEntity>();
                    //HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
                    //we can't get entities in a oriented bounding box, so create a sphere around the whole thing
                    //this is just to find entities in the general area, we check later against the box
                    BoundingSphereD entitySphere = new BoundingSphereD( item.ShipyardBox.Center,
                                                                        item.ShipyardBox.HalfExtent.AbsMax() );

                    Wrapper.GameAction(
                        () => { entities = MyAPIGateway.Entities.GetEntitiesInSphere( ref entitySphere ); } );
                    if ( !entities.Any() )
                        continue;


                    foreach ( IMyEntity entity in entities )
                    {
                        var grid = entity as MyCubeGrid;
                        if ( grid == null )
                            continue;

                        if ( grid.Physics.IsStatic )
                            continue;

                        if ( item.HasGrid && item.Grid == grid )
                        {
                            //TODO: alert the player
                            //there's multiple grids inside the shipyard before grind start
                            //item.HasGrid = false;
                            //break;
                        }
                        MyOrientedBoundingBoxD? testBox = MathUtility.CreateOrientedBoundingBox( grid );
                        if ( !testBox.HasValue )
                            continue;
                        MyOrientedBoundingBoxD gridBox = testBox.Value;
                        UtilityPlugin.Log.Info( item.ShipyardBox.Center.ToString );
                        UtilityPlugin.Log.Info( gridBox.Center.ToString );
                        UtilityPlugin.Log.Info( item.ShipyardBox.Contains( ref gridBox ).ToString );
                        if (
                            item.ShipyardBox.Contains( ref gridBox ) == ContainmentType.Contains )
                        {
                            //don't allow ships to be welded if they're moving
                            if ( grid.Physics.LinearVelocity == Vector3D.Zero
                                 && grid.Physics.AngularVelocity == Vector3D.Zero )
                            {
                                item.HasGrid = true;
                                item.Grid = grid;
                                item.GridBox = gridBox;
                            }
                        }
                    }
                }
                if ( item.HasGrid )
                    StepGrind( item );
            }
            base.Handle();
        }

        private static bool StepGrind( ShipyardItem shipyardItem )
        {
            UtilityPlugin.Log.Info("step grind"  );
            Random random = new Random();
            float grindAmount = Server.Instance.Config.GrinderSpeedMultiplier*PluginSettings.Instance.GrindMultiplier;
            //shorten this to grid for convenience
            MyCubeGrid grid = shipyardItem.Grid;
            if ( grid == null )
                return false;

            //do a raycast to see if the grinder can see the block we're assigning to it
            foreach ( IMyCubeBlock listGrinder in shipyardItem.Tools )
            {
                    MySlimBlock nextBlock;
                if ( !shipyardItem.ProcessBlocks.TryGetValue( listGrinder.EntityId,out nextBlock) )
                {
                    int tryCount = 0;
                    while ( tryCount < 10 )
                    {
                        //limit the number of tries so we don't get stuck in a loop forever
                        tryCount++;

                        //pick a random block. we don't really care if two grinders hit the same block, so don't check

                        if ( grid.BlocksCount > 1 )
                            nextBlock = grid.CubeBlocks.ElementAt( random.Next( 0, grid.BlocksCount - 1 ) );
                        else
                            nextBlock = grid.CubeBlocks.FirstElement();

                        if ( nextBlock == null )
                            continue;

                        if ( shipyardItem.ProcessBlocks.ContainsValue( nextBlock ) )
                            continue;

                        UtilityPlugin.Log.Info("start" );
                        DateTime nowDateTime = DateTime.Now;
                        if ( disconnect.Disconnect( grid, nextBlock, true ) )
                        {
                            UtilityPlugin.Log.Info("Elapsed: " + (DateTime.Now - nowDateTime).ToString()  );
                            continue;
                        }
                        UtilityPlugin.Log.Info( "Elapsed: " + (DateTime.Now - nowDateTime).ToString( ) );

                        //this raycast should give us the grid location of the first block it hits 
                        //we don't really care if it hits our random block, just grab whatever the grinder sees first
                        Vector3I? blockResult = grid.RayCastBlocks( listGrinder.GetPosition(),
                                                                    grid.GridIntegerToWorld( nextBlock.Position ) );

                        if ( !blockResult.HasValue )
                            continue;

                        nextBlock = grid.GetCubeBlock( blockResult.Value );
                        break;
                    }


                    //we weren't able to find a suitable block somehow, so skip this grinder for now
                    if ( nextBlock == null )
                        continue;

                    //TODO: figure out why the hell I can't use SortedList.SetByIndex()
                    //we found a block to pair with our grinder, add it to the dictionary and carry on with destruction
                    shipyardItem.ProcessBlocks.Remove( listGrinder.EntityId );
                    shipyardItem.ProcessBlocks.Add( listGrinder.EntityId, nextBlock );
                }
            }
            List<MyPhysicalInventoryItem> tmpItemList = new List<MyPhysicalInventoryItem>();

            for ( int t = shipyardItem.Tools.Count - 1; t > 0; t-- )
                //foreach ( IMyCubeBlock grinderBlock in shipyardItem.Tools )
            {
                IMyCubeBlock grinderBlock = shipyardItem.Tools[t];
                IMyShipGrinder grinder = (IMyShipGrinder) grinderBlock;

                MyInventory grinderInventory = (MyInventory) grinder.GetInventory( 0 );
                MySlimBlock block;

                if(!shipyardItem.ProcessBlocks.TryGetValue( grinderBlock.EntityId, out block ))
                    continue;

              //  if ( disconnect.Disconnect( block.CubeGrid, block, true ) )
              //  {
              //      shipyardItem.Tools.RemoveAt( t );
              //      continue;
              //  }

                Wrapper.GameAction( () =>
                {
                    MyDamageInformation damageInfo = new MyDamageInformation( false, grindAmount, MyDamageType.Grind,
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
                            for ( int i = 0; i < block.FatBlock.InventoryCount; ++i )
                            {
                                var blockInventory = block.FatBlock.GetInventory( i );
                                if ( blockInventory == null )
                                    continue;

                                if ( blockInventory.Empty() )
                                    continue;

                                tmpItemList.Clear();
                                tmpItemList.AddList( blockInventory.GetItems() );

                                foreach ( var item in tmpItemList )
                                    MyInventory.Transfer( blockInventory, grinderInventory, item.ItemId );
                            }
                        }
                        if ( block.UseDamageSystem )
                            MyDamageSystem.Static.RaiseDestroyed( block, damageInfo );

                        block.SpawnConstructionStockpile();
                        block.CubeGrid.RazeBlock( block.Min );
                    }
                } );
            }
            return true;
        }

        //this should check if removing a block splits the grid

        private static HashSet<MySlimBlock> blocksList = new HashSet<MySlimBlock>();

        public static bool CheckGridSplit( MySlimBlock testBlock )
        {
            UtilityPlugin.Log.Info( "CheckSplit" );
            MyCubeGrid grid = testBlock.CubeGrid;

            if ( testBlock.Neighbours.Count <= 0 )
                return false;

            MySlimBlock firstBlock = testBlock.Neighbours[0];

            FindUniqueNeighbors( firstBlock, testBlock );

            UtilityPlugin.Log.Info( "finish check" );

            UtilityPlugin.Log.Info( $"{blocksList.Count} : {grid.BlocksCount}" );
            if ( blocksList.Count + 1 < grid.BlocksCount )
            {
                blocksList.Clear();
                UtilityPlugin.Log.Error( "true" );
                return true;
            }
            blocksList.Clear();
            UtilityPlugin.Log.Info( "false" );
            return false;
        }

        private static void FindUniqueNeighbors( MySlimBlock testBlock, MySlimBlock targetBlock )
        {
            foreach ( MySlimBlock testNeighbor in testBlock.Neighbours )
            {
                if ( testNeighbor == targetBlock )
                    continue;

                if ( !blocksList.Contains( testNeighbor ) )
                {
                    blocksList.Add( testNeighbor );
                    FindUniqueNeighbors( testNeighbor, targetBlock );
                }
            }
        }
    }
}
