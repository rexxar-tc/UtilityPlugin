using System;
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
        public override int GetUpdateResolution( )
        {
            return 1000;
        }

        public override void Handle( )
        {
            if ( !ProcessShipyardHandler.ShipyardsList.Any( ) )
                return;
            UtilityPlugin.Log.Info( "1" );
            foreach ( ShipyardItem item in ProcessShipyardHandler.ShipyardsList)
            {
               // if (!item.Enabled)
              //      continue;

                if (item.YardType != ShipyardItem.ShipyardType.Grind)
                    continue;

                //we finished the grid and the entity was deleted
                if ( item.Grid == null && !item.SplitGrids.Any( ))
                    item.Clear();
                //if we have any split grids in the queue, load up the next one
                else if (item.Grid == null && item.SplitGrids.Any())
                    item.Grid = item.SplitGrids.First();

                UtilityPlugin.Log.Info( "2" );
                if (!item.HasGrid)
                {
                    //List<IMyEntity> entities = new List<IMyEntity>();
                    HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
                    //we can't get entities in a oriented bounding box, so create a sphere around the whole thing
                    //this is just to find entities in the general area, we check later against the box
                    BoundingSphereD entitySphere = new BoundingSphereD(item.ShipyardBox.Center,item.ShipyardBox.HalfExtent.Max());

                    Wrapper.GameAction(() =>
                    {
                        //entities = MyAPIGateway.Entities.GetEntitiesInSphere(ref entitySphere);
                        MyAPIGateway.Entities.GetEntities(entities);
                    });
                    UtilityPlugin.Log.Info( "3" );
                    if (!entities.Any())
                        continue;


                    foreach (IMyEntity entity in entities)
                    {
                        var grid = entity as MyCubeGrid;
                        if (grid == null)
                            continue;

                        if (grid.Physics.IsStatic)
                            continue;
                        
                        if (item.HasGrid && item.Grid == grid)
                        {
                            //TODO: alert the player
                            //there's multiple grids inside the shipyard before grind start
                            //item.HasGrid = false;
                            //break;
                        }
                        UtilityPlugin.Log.Info( "4" );
                        MyOrientedBoundingBoxD? testBox = MathUtility.CreateOrientedBoundingBox(grid);
                        if (!testBox.HasValue)
                            continue;
                        MyOrientedBoundingBoxD gridBox = testBox.Value;
                        UtilityPlugin.Log.Info( "5" );
                        UtilityPlugin.Log.Info(item.ShipyardBox.Center.ToString);
                        UtilityPlugin.Log.Info(gridBox.Center.ToString);
                        UtilityPlugin.Log.Info(item.ShipyardBox.Contains(ref gridBox).ToString);
                        if (
                            item.ShipyardBox.Contains(ref gridBox) == ContainmentType.Disjoint)
                        {
                            //don't allow ships to be welded if they're moving
                            if (grid.Physics.LinearVelocity == Vector3D.Zero
                             && grid.Physics.AngularVelocity == Vector3D.Zero)
                            {
                                UtilityPlugin.Log.Info("20");
                                item.HasGrid = true;
                                item.Grid = grid;
                                item.GridBox = gridBox;
                            }
                        }
                    }
                }
                UtilityPlugin.Log.Info( "6" );
                if(item.HasGrid)
                StepGrind(item);
            }
            base.Handle( );
        }
        
        private static bool StepGrind( ShipyardItem shipyardItem  )
        {
            UtilityPlugin.Log.Info( "7" );
            Random random = new Random();
            float grindAmount = Server.Instance.Config.GrinderSpeedMultiplier*PluginSettings.Instance.GrindMultiplier;
            //shorten this to grid for convenience
            MyCubeGrid grid = shipyardItem.Grid;
            if (grid == null)
                return false;

            //do a raycast to see if the grinder can see the block we're assigning to it
            foreach (IMyCubeBlock listGrinder in shipyardItem.Tools)
            {
                UtilityPlugin.Log.Info( "8" );
                if (!shipyardItem.ProcessBlocks.ContainsKey(listGrinder.EntityId))
                {
                    MySlimBlock nextBlock = null;
                    UtilityPlugin.Log.Info( "19" );
                    int tryCount = 0;
                    while (tryCount < 10)
                    {
                        UtilityPlugin.Log.Info("9");
                        //limit the number of tries so we don't get stuck in a loop forever
                        tryCount++;
                        UtilityPlugin.Log.Info( "30" );
                        //pick a random block. we don't really care if two grinders hit the same block, so don't check
                       
                        if (grid.BlocksCount > 1)
                            nextBlock = grid.CubeBlocks.ElementAt(random.Next(0, grid.BlocksCount - 1));
                        else
                            nextBlock = grid.CubeBlocks.FirstElement();

                        if(nextBlock==null)
                            continue;

                        if (shipyardItem.ProcessBlocks.ContainsValue(nextBlock))
                            continue;

                        UtilityPlugin.Log.Info( "31" );
                        //this raycast should give us the grid location of the first block it hits 
                        //we don't really care if it hits our random block, just grab whatever the grinder sees first
                        Vector3I? blockResult = grid.RayCastBlocks(listGrinder.GetPosition(),
                            grid.GridIntegerToWorld(nextBlock.Position));
                        UtilityPlugin.Log.Info( "33" );
                        if (!blockResult.HasValue)
                            continue;

                        nextBlock = grid.GetCubeBlock(blockResult.Value);
                        break;
                    }


                    //we weren't able to find a suitable block somehow, so skip this grinder for now
                    if (nextBlock == null)
                        continue;
                    UtilityPlugin.Log.Info("10");
                    //we found a block to pair with our grinder, add it to the dictionary and carry on with destruction
                    shipyardItem.ProcessBlocks.Add(listGrinder.EntityId, nextBlock);
                }
            }
            UtilityPlugin.Log.Info( "11" );
            List<MyPhysicalInventoryItem> tmpItemList = new List<MyPhysicalInventoryItem>( );
            foreach ( IMyCubeBlock grinderBlock in shipyardItem.Tools )
            {
                UtilityPlugin.Log.Info( "12" );
                IMyShipGrinder grinder = (IMyShipGrinder)grinderBlock;
                MyInventory grinderInventory = (MyInventory)grinder.GetInventory( 0 );
                MySlimBlock block;
                shipyardItem.ProcessBlocks.TryGetValue(grinderBlock.EntityId, out block);
                UtilityPlugin.Log.Info( "13" );
                Wrapper.GameAction( ( ) =>
                 {
                     
                     MyDamageInformation damageInfo = new MyDamageInformation( false, grindAmount, MyDamageType.Grind,
                         grinder.EntityId );
                     if ( block.UseDamageSystem )
                         MyDamageSystem.Static.RaiseBeforeDamageApplied( block, ref damageInfo );
                     
                     block.DecreaseMountLevel( damageInfo.Amount, grinderInventory );
                     block.MoveItemsFromConstructionStockpile( grinderInventory );
                     UtilityPlugin.Log.Info( "14" );
                     if ( block.UseDamageSystem )
                         MyDamageSystem.Static.RaiseAfterDamageApplied( block, damageInfo );
                     
                     if ( block.IsFullyDismounted )
                     {
                         if ( block.FatBlock != null && block.FatBlock.HasInventory )
                         {
                             for ( int i = 0; i < block.FatBlock.InventoryCount; ++i )
                             {
                                 UtilityPlugin.Log.Info( "15" );
                                 var blockInventory = block.FatBlock.GetInventory( i ) as MyInventory;
                                 if ( blockInventory == null )
                                     continue;
                                 
                                 if ( blockInventory.Empty( ) )
                                     continue;

                                 tmpItemList.Clear( );
                                 tmpItemList.AddList( blockInventory.GetItems( ) );
                                 
                                 foreach ( var item in tmpItemList )
                                 {
                                     MyInventory.Transfer( blockInventory, grinderInventory, item.ItemId );
                                 }
                             }
                         }
                         UtilityPlugin.Log.Info( "16" );
                         if ( block.UseDamageSystem )
                             MyDamageSystem.Static.RaiseDestroyed( block, damageInfo );
                         
                         block.SpawnConstructionStockpile( );
                         block.CubeGrid.RazeBlock( block.Min );
                         UtilityPlugin.Log.Info( "17" );
                         //remove the list entry for this grinder so we can assign it a new block in the next loop
                         shipyardItem.ProcessBlocks.Remove(grinderBlock.EntityId);
                     }
                 } );
            }
            UtilityPlugin.Log.Info( "18" );
            return true;
        }

    }
}
