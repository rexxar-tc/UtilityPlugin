using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems;
using Sandbox.Game.GameSystems.Conveyors;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using SEModAPIExtensions.API;
using UtilityPlugin.Utility;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using IMyShipWelder = Sandbox.ModAPI.IMyShipWelder;

namespace UtilityPlugin.ProcessHandlers
{
    public class ProcessWeldHandler : ProcessHandlerBase
    {
        public override int GetUpdateResolution()
        {
            return 500;
        }

        public override void Handle()
        {

            lock ( ProcessShipyardHandler.ShipyardsList )
            {
                foreach ( ShipyardItem item in ProcessShipyardHandler.ShipyardsList.Where( item => item.YardType == ShipyardItem.ShipyardType.Weld ) )
                {

                    if ( item.HasGrid )
                    {
                        if ( item.Grid?.Physics == null || item.Grid.Closed )
                        {
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
                        
                        if ( !StepWeld( item ) )
                        {
                            item.Clear();
                            continue;
                        }
                    }
                    
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
                    
                    foreach ( MyCubeGrid grid in grids )
                    {
                        if ( grid?.Physics == null || grid.Closed )
                            continue;

                        if ( grid.Physics.IsStatic )
                            continue;

                        if ( item.HasGrid && item.Grid == grid )
                        {
                            //TODO: alert the player?
                            //there's multiple grids inside the shipyard before grind start
                            //item.HasGrid = false;
                            //break;
                        }
                        
                        MyOrientedBoundingBoxD? testBox = MathUtility.CreateOrientedBoundingBox( grid );
                        if ( !testBox.HasValue )
                            continue;
                        MyOrientedBoundingBoxD gridBox = testBox.Value;

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

        private static bool StepWeld( ShipyardItem shipyardItem )
        {
         HashSet<long> stalledWelders = new HashSet<long>();
        var missingComponents = new Dictionary<string, int>();
            var random = new Random();
            float weldAmount = Server.Instance.Config.WelderSpeedMultiplier * PluginSettings.Instance.WeldMultiplier;
            float boneAmount = weldAmount * .1f;
            //shorten this to grid for convenience
            MyCubeGrid grid = shipyardItem.Grid;
            if ( grid?.Physics == null || grid.Closed )
                return false;

            var weldBlocks = new HashSet<MySlimBlock>();
            foreach ( MySlimBlock block in grid.CubeBlocks.Where(block => !block.IsFullIntegrity || (block.HasDeformation || block.MaxDeformation > 0.0001f)) )
                weldBlocks.Add( block );

            //if we have no blocks to weld, return false so we know we're done
            if ( weldBlocks.Count == 0 )
            {
                if ( shipyardItem.GridProjector == null )
                {
                    foreach ( var block in shipyardItem.Grid.GetFatBlocks() )
                    {
                        MyProjectorBase projector = block as MyProjectorBase;

                        if ( projector?.Clipboard.PreviewGrids.Count > 0 )
                        {
                            /*
                            bool canBuild = false;
                            Wrapper.GameAction( () => canBuild = projector.Clipboard.PreviewGrids[0].CubeBlocks.All(x => projector.CanBuild(x, true) != MyProjectorBase.BuildCheckResult.OK));
                            if ( !canBuild)
                                continue;
                            */
                            shipyardItem.GridProjector = projector;
                            break;
                        }
                    }
                }
            }
            if ( weldBlocks.Count < 8 && shipyardItem.GridProjector?.Clipboard.PreviewGrids.Count > 0 )
            {
                int neededBlocks = 8 - weldBlocks.Count;
                //shipyardItem.ProcessBlocks.Clear();
                for ( int b = 0; b < neededBlocks; b++ )
                {
                    var welder = shipyardItem.Tools[b];
                    var projector = shipyardItem.GridProjector;
                    var projectedGrid = projector.Clipboard.PreviewGrids[0];
                    MySlimBlock buildBlock = null;

                    Wrapper.GameAction( () =>
                    {
                        //buildBlock = projectedGrid.CubeBlocks.First( x => projector.CanBuild( x, true ) == MyProjectorBase.BuildCheckResult.OK );
                        foreach ( var block in projectedGrid.CubeBlocks )
                        {
                            if ( projector.CanBuild( block, true ) == MyProjectorBase.BuildCheckResult.OK )
                            {
                                buildBlock = block;
                                break;
                            }
                        }
                        if ( buildBlock == null )
                        {
                            return;
                        }
                        var welderInventory = (MyInventory)((IMyShipWelder)welder).GetInventory( 0 );
                        var components = buildBlock.BlockDefinition.Components;
                        if ( components == null || components.Length == 0 )
                            return;
                        var componentId = components[0].Definition.Id;
                        MyGridConveyorSystem.ItemPullRequest( (IMyConveyorEndpointBlock)welder, welderInventory, welder.OwnerId, componentId, 1 );


                        if ( welderInventory.ContainItems( 1, buildBlock.BlockDefinition.Components[0].Definition.Id ) )
                        {
                            projector.Build( buildBlock, welder.OwnerId, welder.EntityId );
                        }
                    } );
                    /*
                        Communication.MessageStruct message = new Communication.MessageStruct()
                        {
                            toolId=welder.EntityId,
                            gridId=buildBlock.CubeGrid.EntityId,
                            blockPos=buildBlock.Position,
                            packedColor = Color.LightGreen.PackedValue,
                            pulse=false
                        };
                        Communication.SendLine( message );
                        */
                    //if (buildBlock!=null)
                    //weldBlocks.Add( buildBlock );
                }
            }
            if ( shipyardItem.GridProjector?.Clipboard.PreviewGrids.Count == 0 )
            {
                shipyardItem.GridProjector = null;
                return false;
            }

            foreach ( IMyCubeBlock welder in shipyardItem.Tools )
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

                        if ( !nextBlock.IsFullIntegrity || (nextBlock.HasDeformation || nextBlock.MaxDeformation > 0.0001f) )
                        {
                            break;
                        }
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
                foreach ( IMyCubeBlock welderBlock in shipyardItem.Tools )
                {
                    var welder = (IMyShipWelder)welderBlock;
                    var welderInventory = (MyInventory)welder.GetInventory( 0 );
                    MySlimBlock block;

                    shipyardItem.ProcessBlocks.TryGetValue( welderBlock.EntityId, out block );
                    if ( block?.CubeGrid?.Physics == null )
                        continue;

                    if (!(!block.IsFullIntegrity || (block.HasDeformation || block.MaxDeformation > 0.0001f)) )
                    {
                        shipyardItem.ProcessBlocks.Remove( welder.EntityId );
                        continue;
                    }
                    
                    block.GetMissingComponents( missingComponents );

                    foreach ( KeyValuePair<string, int> component in missingComponents )
                    {
                        var componentId = new MyDefinitionId( typeof (MyObjectBuilder_Component), component.Key );
                        int amount = Math.Max( component.Value - (int)welderInventory.GetItemAmount( componentId ), 0 );
                        if ( amount == 0 )
                            continue;

                        if ( welder.UseConveyorSystem )
                        {
                            MyGridConveyorSystem.ItemPullRequest( (IMyConveyorEndpointBlock)welder, welderInventory,
                                                                  welder.OwnerId, componentId, component.Value );
                        }
                    }
                    
                        block.MoveItemsToConstructionStockpile( (MyInventory)welder.GetInventory( 0 ) );
                        block.IncreaseMountLevel( weldAmount, 0, null, boneAmount, true );

                    if ( !block.CanContinueBuild( (MyInventory)welder.GetInventory( 0 ) ) && !block.IsFullIntegrity )
                        stalledWelders.Add( welder.EntityId );
                }
            } );

            foreach (var tool in shipyardItem.Tools)
            {
                MySlimBlock targetBlock;
                Communication.MessageStruct message = new Communication.MessageStruct()
                {
                    toolId = tool.EntityId,
                    gridId = 0,
                    blockPos = new SerializableVector3I(),
                    packedColor = 0
                };
                if (!shipyardItem.ProcessBlocks.TryGetValue(tool.EntityId, out targetBlock))
                {
                    Communication.SendLine(message);
                    continue;
                }

                message.gridId = targetBlock.CubeGrid.EntityId;
                message.blockPos = targetBlock.Position;

                if ( stalledWelders.Contains( tool.EntityId ) )
                {
                    message.packedColor = Color.Purple.PackedValue;
                    message.pulse = true;
                }
                else
                {
                    message.packedColor = Color.DarkCyan.PackedValue;
                    message.pulse = false;
                }

                Communication.SendLine(message);
            }
            
            return true;
        }
    }
}