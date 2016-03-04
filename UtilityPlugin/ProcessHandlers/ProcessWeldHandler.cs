using System;
using System.Collections.Generic;
using System.Linq;
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
using IMyShipWelder = Sandbox.ModAPI.IMyShipWelder;

namespace UtilityPlugin.ProcessHandlers
{
    public class ProcessWeldHandler : ProcessHandlerBase
    {
        public override int GetUpdateResolution()
        {
            return 1000;
        }

        public override void Handle()
        {
            if ( !ProcessShipyardHandler.ShipyardsList.Any() )
                return;

            foreach ( var item in ProcessShipyardHandler.ShipyardsList )
            {
                //if ( !item.Enabled )
                //    continue;

                if ( item.YardType != ShipyardItem.ShipyardType.Weld )
                    continue;

                if ( !item.HasGrid )
                {
                    var entities = new List<IMyEntity>();
                    //var entities = new HashSet<IMyEntity>();
                    //we can't get entities in a oriented bounding box, so create a sphere around the whole thing
                    //this is just to find entities in the general area, we check later against the box
                    var entitySphere = new BoundingSphereD( item.ShipyardBox.Center,
                                                            item.ShipyardBox.HalfExtent.AbsMax() );

                    Wrapper.GameAction( () =>
                    {
                        entities = MyAPIGateway.Entities.GetEntitiesInSphere( ref entitySphere );
                        //MyAPIGateway.Entities.GetEntities( entities );
                    } );

                    if ( !entities.Any() )
                        continue;

                    foreach ( var entity in entities )
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

                        var testBox = MathUtility.CreateOrientedBoundingBox( grid );
                        if ( !testBox.HasValue )
                            continue;
                        var gridBox = testBox.Value;

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
                {
                    if ( !StepWeld( item ) )
                    {
                        //welding is done, do something about it
                    }
                }
            }
            base.Handle();
        }

        private static bool StepWeld( ShipyardItem shipyardItem )
        {
            var missingComponents = new Dictionary<string, int>();
            var random = new Random();
            var weldAmount = Server.Instance.Config.GrinderSpeedMultiplier * PluginSettings.Instance.GrindMultiplier;
            //shorten this to grid for convenience
            var grid = shipyardItem.Grid;
            if ( grid == null )
                return false;

            var weldBlocks = new HashSet<MySlimBlock>();
            foreach ( var block in grid.CubeBlocks )
            {
                if ( !block.IsFullIntegrity )
                    weldBlocks.Add( block );
            }

            //if we have no blocks to weld, return false so we know we're done
            if ( weldBlocks.Count == 0 )
                return false;

            foreach ( var welder in shipyardItem.Tools )
            {
                if ( !shipyardItem.ProcessBlocks.ContainsKey( welder.EntityId ) )
                {
                    MySlimBlock nextBlock = null;
                    var tryCount = 0;


                    while ( tryCount < 20 )
                    {
                        //limit the number of tries so we don't get stuck in a loop forever
                        tryCount++;

                        //pick a random block. we don't really care if two welders hit the same block, so don't check
                        if ( weldBlocks.Count > 1 )
                            nextBlock = weldBlocks.ElementAt( random.Next( 0, weldBlocks.Count - 1 ) );
                        else
                            nextBlock = weldBlocks.FirstElement();

                        if ( nextBlock == null )
                            continue;

                        if ( nextBlock.IsFullIntegrity )
                            continue;

                        break;
                    }

                    //we weren't able to find a suitable block somehow, so skip this welder for now
                    if ( nextBlock == null )
                        continue;

                    //we found a block to pair with our welder, add it to the dictionary and carry on with destruction
                    shipyardItem.ProcessBlocks.Add( welder.EntityId, nextBlock );
                }
            }

            if ( shipyardItem.ProcessBlocks.Count < 1 )
            {
                //No more blocks to weld
                return false;
            }

            Wrapper.GameAction( () =>
            {
                foreach ( var welderBlock in shipyardItem.Tools )
                {
                    var welder = (IMyShipWelder)welderBlock;
                    var welderInventory = (MyInventory)welder.GetInventory( 0 );
                    MySlimBlock block;

                    shipyardItem.ProcessBlocks.TryGetValue( welderBlock.EntityId, out block );
                    if ( block == null )
                        continue;

                    if ( block.IsFullIntegrity )
                    {
                        shipyardItem.ProcessBlocks.Remove( welder.EntityId );
                        continue;
                    }

                    block.GetMissingComponents( missingComponents );

                    foreach ( var component in missingComponents )
                    {
                        var componentId = new MyDefinitionId( typeof (MyObjectBuilder_Component), component.Key );
                        var amount = Math.Max( component.Value - (int)welderInventory.GetItemAmount( componentId ), 0 );
                        if ( amount == 0 )
                            continue;

                        if ( welder.UseConveyorSystem )
                        {
                            MyGridConveyorSystem.ItemPullRequest( (IMyConveyorEndpointBlock)welder, welderInventory,
                                                                  welder.OwnerId, componentId, component.Value );
                        }
                    }

                    if ( block.CanContinueBuild( (MyInventory)welder.GetInventory( 0 ) ) )
                    {
                        block.MoveItemsToConstructionStockpile( (MyInventory)welder.GetInventory( 0 ) );
                        block.IncreaseMountLevel( weldAmount, 0, null, 1f, true );
                    }
                    else
                        shipyardItem.ProcessBlocks.Remove( welder.EntityId );
                }
            } );

            return true;
        }
    }
}