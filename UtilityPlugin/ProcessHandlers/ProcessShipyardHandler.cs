using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using UtilityPlugin.Utility;
using VRage;
using VRage.ModAPI;
using VRageMath;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using IMyShipWelder = Sandbox.ModAPI.IMyShipWelder;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace UtilityPlugin.ProcessHandlers
{
    public class ShipyardItem
    {
        public ShipyardItem( MyOrientedBoundingBoxD box, List<IMyCubeBlock> tools, ShipyardType yardType, IMyEntity yardEntity )
        {
            ShipyardBox = box;
            Tools = tools;
            YardType = yardType;
            YardEntity = yardEntity;
        }

        public enum ShipyardType
        {
            Weld,
            Grind,
            Disabled
        }

        public MyOrientedBoundingBoxD ShipyardBox;
        public List<IMyCubeBlock> Tools;
        public ShipyardType YardType;
        public IMyEntity YardEntity;

        public void Clear( )
        {
            //clear out all the old grid data but leave shipyard info
            //not strictly necessary, but might as well clean up after ourselves
            Enabled = false;
            HasGrid = false;
            Grid = null;
            ProcessBlocks.Clear( );
            YardGrids.Clear();
            ClearLines();
            GridProjector = null;
        }

        public bool Enabled;
        //these are set when processing a grid
        public bool HasGrid;
        public MyCubeGrid Grid;
        //tool, target block
        public SortedList<long, MySlimBlock> ProcessBlocks = new SortedList<long, MySlimBlock>( );
        public List<MyCubeGrid> YardGrids = new List<MyCubeGrid>();
        public MyProjectorBase GridProjector;
        

        public void ClearLines()
        {
            foreach (var tool in Tools)
            {
                Communication.MessageStruct message = new Communication.MessageStruct()
                {
                    toolId = tool.EntityId,
                    gridId = 0,
                    blockPos = new SerializableVector3I(0, 0, 0),
                    packedColor = 0,
                    pulse = false
                };
                Communication.SendLine(message);
            }
        }
    }

    public class ProcessShipyardHandler : ProcessHandlerBase
    {
        private static List<IMyCubeBlock> grinders = new List<IMyCubeBlock>( );
        private static List<IMyCubeBlock> welders = new List<IMyCubeBlock>( );

        public static HashSet<ShipyardItem> ShipyardsList = new HashSet<ShipyardItem>();

        public override int GetUpdateResolution( )
        {
            return 10000;
        }

        public override void Handle( )
        {
            //clear the welder and grinder lists
            if ( grinders.Any( ) )
                grinders.Clear( );
            if ( welders.Any( ) )
                welders.Clear( );

            List<IMyEntity> skipEntities = new List<IMyEntity>();

            HashSet<IMyEntity> entities = new HashSet<IMyEntity>( );
            Wrapper.GameAction( ( ) =>
             {
                 MyAPIGateway.Entities.GetEntities( entities );
             } );
            

            //run through our current list of shipyards and make sure they're still valid
            lock ( ShipyardsList )
            {
                HashSet<ShipyardItem> itemsToRemove = new HashSet<ShipyardItem>();
                foreach(var item in ShipyardsList )
                {

                    if ( !AreToolsConnected( item.Tools ) )
                    {
                        UtilityPlugin.Log.Info( "remove item tools " + item.Tools.Count );
                        item.Clear();
                        itemsToRemove.Add( item );
                        continue;
                    }
                    if ( !entities.Contains( item.YardEntity ) )
                    {
                        UtilityPlugin.Log.Info( "remove item entity" );
                        item.Clear();
                        itemsToRemove.Add( item );
                        continue;
                    }
                    if ( !item.YardEntity.Physics.IsStatic )
                    {
                        UtilityPlugin.Log.Info( "remove item physics" );
                        itemsToRemove.Add( item );
                        item.Clear();
                        continue;
                    }

                    //skipEntities.Add( item.YardEntity );
                    //this should stop us recalculating a bounding box on an existing yard
                    //  entities.Remove(item.YardEntity);
                }

                foreach ( var item in itemsToRemove )
                {
                    ShipyardsList.Remove( item );
                }

                foreach ( IMyEntity entity in entities )
                {
                    if ( entity == null || entity.Closed )
                        continue;

                    if ( skipEntities.Contains( entity ) )
                        continue;

                    if ( entity.Physics == null || !entity.Physics.IsStatic )
                        continue;

                    if ( !(entity is IMyCubeGrid) )
                        continue;

                    List<IMySlimBlock> gridBlocks = new List<IMySlimBlock>();
                    Wrapper.GameAction( () =>
                    {
                        ((IMyCubeGrid)entity).GetBlocks( gridBlocks );
                    } );

                    foreach ( IMySlimBlock slimBlock in gridBlocks )
                    {
                        if ( slimBlock == null )
                            continue;

                        if ( slimBlock.FatBlock == null )
                            continue;

                        var fatBlock = slimBlock.FatBlock;
                        //TODO: Probably better to check subtype of the custom block when we have it
                        try
                        {
                            if ( fatBlock is IMyShipGrinder
                                 && ((IMyTerminalBlock)fatBlock).CustomName.ToLower().Contains( "shipyard" ) )
                            {
                                grinders.Add( fatBlock );
                            }

                            else if ( fatBlock is IMyShipWelder
                                      && ((IMyTerminalBlock)fatBlock).CustomName.ToLower().Contains( "shipyard" ) )
                            {
                                welders.Add( fatBlock );
                            }
                        }
                        catch ( Exception ex )
                        {
                            UtilityPlugin.Log.Error( ex );
                        }
                    }
                    //make sure the shipyard blocks are all connected to the conveyor system
                    if ( grinders.Count == 8 )
                    {
                        if ( ShipyardsList.Any( x => x.YardEntity.EntityId == entity.EntityId && x.YardType==ShipyardItem.ShipyardType.Grind) )
                            continue;

                        MyOrientedBoundingBoxD? testBox = IsYardValid( entity, grinders );
                        if ( testBox.HasValue )
                        {
                            ShipyardsList.Add( new ShipyardItem(
                                                   testBox.Value,
                                                   grinders.ToList(),
                                                   ShipyardItem.ShipyardType.Grind,
                                                   entity ) );
                        }
                    }

                    if ( welders.Count == 8 )
                    {
                        if (ShipyardsList.Any(x => x.YardEntity.EntityId == entity.EntityId && x.YardType == ShipyardItem.ShipyardType.Weld))
                            continue;

                        MyOrientedBoundingBoxD? testBox = IsYardValid( entity, welders );
                        if ( testBox.HasValue )
                        {
                            ShipyardsList.Add( new ShipyardItem(
                                                   testBox.Value,
                                                   welders.ToList(),
                                                   ShipyardItem.ShipyardType.Weld,
                                                   entity ) );
                        }
                    }
                }
            }
            base.Handle( );
        }

        //this makes sure all tools are connected to the same conveyor system
        private bool AreToolsConnected( List<IMyCubeBlock> tools )
        {
            if ( tools.Count != 8 )
                return false;
            bool found = true;
            Wrapper.GameAction( () =>
            {
                for ( int i = 1; i < tools.Count; ++i )
                {
                    IMyInventory toolInventory = (IMyInventory) ((IMyShipToolBase) tools[0]).GetInventory( 0 );
                    IMyInventory compareInventory = (IMyInventory) ((IMyShipToolBase) tools[i]).GetInventory( 0 );
                    if ( compareInventory == null || toolInventory == null )
                        continue;
                    if ( !toolInventory.IsConnectedTo( compareInventory ) )
                       found = false;
                }
            } );
            return found;
        }

        private MyOrientedBoundingBoxD? IsYardValid( IMyEntity entity, List<IMyCubeBlock> tools )
        {
            DateTime startTime = DateTime.Now;
            List<Vector3D> points = new List<Vector3D>( );
            List<Vector3I> gridPoints = new List<Vector3I>( );
            foreach ( IMyCubeBlock tool in tools )
            {
                gridPoints.Add( tool.Position );
                points.Add( tool.PositionComp.GetPosition( ) );
            }
            
            if ( !AreToolsConnected( tools ) )
                return null;
            
            if ( MathUtility.ArePointsOrthogonal( gridPoints ) )
            {
                UtilityPlugin.Log.Info( "APO Time: " + (DateTime.Now - startTime).Milliseconds );
                startTime = DateTime.Now;
                MyOrientedBoundingBoxD? returnBox = MathUtility.CreateOrientedBoundingBox( (MyCubeGrid)entity, points );
                UtilityPlugin.Log.Info( "OBB Time: " + (DateTime.Now - startTime).Milliseconds );
                return returnBox;
            }
            
            return null;
        }
    }
}
