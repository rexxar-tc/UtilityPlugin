using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems;
using Sandbox.Game.GameSystems.Conveyors;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using SEModAPIExtensions.API;
using UtilityPlugin.Utility;
using VRage.Game;
using VRage.ModAPI;
using VRageMath;
using IMyCubeBlock = Sandbox.ModAPI.IMyCubeBlock;
using IMyCubeGrid = Sandbox.ModAPI.IMyCubeGrid;
using IMyInventory = Sandbox.ModAPI.IMyInventory;
using IMyShipWelder = Sandbox.ModAPI.IMyShipWelder;
using IMySlimBlock = Sandbox.ModAPI.IMySlimBlock;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace UtilityPlugin.ProcessHandlers
{
    public class ProcessWeldHandler : ProcessHandlerBase
    {
        public override int GetUpdateResolution( )
        {
            return 1000;
        }

        public override void Handle( )
        {
            if ( !ProcessShipyardHandler.ShipyardsList.Any( ) )
                return;

            foreach ( ShipyardItem item in ProcessShipyardHandler.ShipyardsList )
            {
                // if (!item.Enabled)
                //      continue;

                if ( item.YardType != ShipyardItem.ShipyardType.Weld )
                    continue;

                //we finished the grid and the entity was deleted
                if ( item.Grid == null && !item.SplitGrids.Any( ) )
                    item.Clear( );
                //if we have any split grids in the queue, load up the next one
                else if ( item.Grid == null && item.SplitGrids.Any( ) )
                    item.Grid = item.SplitGrids.First( );

                UtilityPlugin.Log.Info( "2" );
                if ( !item.HasGrid )
                {
                    //List<IMyEntity> entities = new List<IMyEntity>();
                    HashSet<IMyEntity> entities = new HashSet<IMyEntity>( );
                    //we can't get entities in a oriented bounding box, so create a sphere around the whole thing
                    //this is just to find entities in the general area, we check later against the box
                    BoundingSphereD entitySphere = new BoundingSphereD( item.ShipyardBox.Center, item.ShipyardBox.HalfExtent.Max( ) );

                    Wrapper.GameAction( ( ) =>
                     {
                        //entities = MyAPIGateway.Entities.GetEntitiesInSphere(ref entitySphere);
                        MyAPIGateway.Entities.GetEntities( entities );
                     } );
                    UtilityPlugin.Log.Info( "3" );
                    if ( !entities.Any( ) )
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
                        UtilityPlugin.Log.Info( "4" );
                        MyOrientedBoundingBoxD? testBox = MathUtility.CreateOrientedBoundingBox( grid );
                        if ( !testBox.HasValue )
                            continue;
                        MyOrientedBoundingBoxD gridBox = testBox.Value;
                        UtilityPlugin.Log.Info( "5" );
                        UtilityPlugin.Log.Info( item.ShipyardBox.Center.ToString );
                        UtilityPlugin.Log.Info( gridBox.Center.ToString );
                        UtilityPlugin.Log.Info( item.ShipyardBox.Contains( ref gridBox ).ToString );
                        if (
                            item.ShipyardBox.Contains( ref gridBox ) == ContainmentType.Disjoint )
                        {
                            //don't allow ships to be welded if they're moving
                            if ( grid.Physics.LinearVelocity == Vector3D.Zero
                             && grid.Physics.AngularVelocity == Vector3D.Zero )
                            {
                                UtilityPlugin.Log.Info( "20" );
                                item.HasGrid = true;
                                item.Grid = grid;
                                item.GridBox = gridBox;
                            }
                        }
                    }
                }
                UtilityPlugin.Log.Info( "6" );
                if ( item.HasGrid )
                    StepWeld( item );
            }
            base.Handle( );
        }

        private static bool StepWeld( ShipyardItem shipyardItem )
        {
            Dictionary<string, int> missingComponents = new Dictionary<string, int>( );
            UtilityPlugin.Log.Info( "7" );
            Random random = new Random( );
            float grindAmount = Server.Instance.Config.GrinderSpeedMultiplier * PluginSettings.Instance.GrindMultiplier;
            //shorten this to grid for convenience
            MyCubeGrid grid = shipyardItem.Grid;
            if ( grid == null )
                return false;

            //this is awful but I'll fix it later. probably
            HashSet<MySlimBlock> weldBlocks = new HashSet<MySlimBlock>();
            foreach (MySlimBlock block in grid.CubeBlocks)
            {
                if (!block.IsFullIntegrity)
                    weldBlocks.Add(block);
            }

            foreach (IMyCubeBlock welder in shipyardItem.Tools)
            {
                if (!shipyardItem.ProcessBlocks.ContainsKey(welder.EntityId))
                {
                    MySlimBlock nextBlock = null;
                    UtilityPlugin.Log.Info( "19" );
                    int tryCount = 0;


                    while ( tryCount < 20 )
                    {
                        UtilityPlugin.Log.Info( "9" );
                        //limit the number of tries so we don't get stuck in a loop forever
                        tryCount++;
                        UtilityPlugin.Log.Info( "30" );
                        //pick a random block. we don't really care if two welders hit the same block, so don't check

                        if ( weldBlocks.Count > 1 )
                            nextBlock = weldBlocks.ElementAt( random.Next( 0, weldBlocks.Count - 1 ) );
                        else
                            nextBlock = weldBlocks.FirstElement( );

                        if ( nextBlock == null )
                            continue;

                        if (nextBlock.IsFullIntegrity)
                            continue;

                        if ( shipyardItem.ProcessBlocks.ContainsValue( nextBlock ) )
                            continue;

                        break;
                    }

                    //we weren't able to find a suitable block somehow, so skip this welder for now
                    if ( nextBlock == null )
                        continue;
                    UtilityPlugin.Log.Info( "10" );
                    //we found a block to pair with our welder, add it to the dictionary and carry on with destruction
                    shipyardItem.ProcessBlocks.Add( welder.EntityId, nextBlock );
                }
            }
            Wrapper.GameAction(() =>
            {
                foreach (IMyCubeBlock welderBlock in shipyardItem.Tools)
                {
                    UtilityPlugin.Log.Info("12");
                    IMyShipWelder welder = (IMyShipWelder) welderBlock;
                    MyInventory welderInventory = (MyInventory) welder.GetInventory(0);
                    MySlimBlock block;
                    shipyardItem.ProcessBlocks.TryGetValue(welderBlock.EntityId, out block);
                    if (block == null)
                        continue;

                    if (block.IsFullIntegrity)
                    {
                        shipyardItem.ProcessBlocks.Remove(welderBlock.EntityId);
                        continue;
                    }

                    block.GetMissingComponents(missingComponents);

                    foreach (var component in missingComponents)
                    {
                        var componentId = new MyDefinitionId(typeof (MyObjectBuilder_Component), component.Key);
                        int amount = Math.Max(component.Value - (int) welderInventory.GetItemAmount(componentId), 0);
                        if (amount == 0)
                            continue;

                        if (welder.UseConveyorSystem)
                            MyGridConveyorSystem.ItemPullRequest((IMyConveyorEndpointBlock) welder, welderInventory,
                                welder.OwnerId, componentId, component.Value);
                    }

                    float weldAmount = PluginSettings.Instance.WeldMultiplier*
                                       Server.Instance.Config.WelderSpeedMultiplier;

                    if (block.CanContinueBuild((MyInventory) welder.GetInventory(0)))
                    {
                        block.MoveItemsToConstructionStockpile((MyInventory) welder.GetInventory(0));
                        block.IncreaseMountLevel(weldAmount, 0, null, 1f, true);

                    }

                }
            });

            //THREADING!!!

            return false;
        }

    }
}
