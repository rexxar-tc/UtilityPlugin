using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using UtilityPlugin.Utility;
using VRage.ModAPI;
using VRageMath;
using IMyCubeBlock = Sandbox.ModAPI.IMyCubeBlock;
using IMyCubeGrid = Sandbox.ModAPI.IMyCubeGrid;
using IMyInventory = Sandbox.ModAPI.IMyInventory;
using IMyShipWelder = Sandbox.ModAPI.IMyShipWelder;
using IMySlimBlock = Sandbox.ModAPI.IMySlimBlock;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using VRage.Game;
using VRage.Game.Entity;

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
            Grind
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
            GridBox = new MyOrientedBoundingBoxD( );
            ProcessBlocks.Clear( );
        }

        public bool Enabled;
        //these are set when processing a grid
        public bool HasGrid;
        public MyCubeGrid Grid;
        public MyOrientedBoundingBoxD GridBox;
        //tool, target block
        public SortedList<long, MySlimBlock> ProcessBlocks = new SortedList<long, MySlimBlock>( );
    }

    public class ProcessShipyardHandler : ProcessHandlerBase
    {
        private static List<IMyCubeBlock> grinders = new List<IMyCubeBlock>( );
        private static List<IMyCubeBlock> welders = new List<IMyCubeBlock>( );

        public static List<ShipyardItem> ShipyardsList = new List<ShipyardItem>();

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

            List<IMyEntity> SkipEntities = new List<IMyEntity>();

            HashSet<IMyEntity> entities = new HashSet<IMyEntity>( );
            Wrapper.GameAction( ( ) =>
             {
                 MyAPIGateway.Entities.GetEntities( entities );
             } );
            

            //run through our current list of shipyards and make sure they're still valid
            for ( int i = ShipyardsList.Count - 1; i >= 0; --i )
            {
                ShipyardItem item = ShipyardsList[i];

                if ( !AreToolsConnected( item.Tools ) )
                {
                    UtilityPlugin.Log.Info("remove item tools " + item.Tools.Count.ToString()  );
                    ShipyardsList.Remove( item );
                }
                if ( !entities.Contains( item.YardEntity ) )
                {
                    UtilityPlugin.Log.Info("remove item entity"  );
                    ShipyardsList.Remove( item );
                }
                if ( !item.YardEntity.Physics.IsStatic )
                {
                    UtilityPlugin.Log.Info("remove item physics"  );
                    ShipyardsList.Remove( item );
                }

                UtilityPlugin.Log.Info("evaluating shipyard"  );
                SkipEntities.Add(item.YardEntity  );
                //this should stop us recalculating a bounding box on an existing yard
                //  entities.Remove(item.YardEntity);
            }
            
            foreach ( IMyEntity entity in entities )
            {
                if ( entity == null )
                    continue;

                if ( SkipEntities.Contains( entity ) )
                    continue;

                if ( entity.Physics == null || !entity.Physics.IsStatic )
                    continue;

                if ( !(entity is IMyCubeGrid) )
                    continue;
                List<IMySlimBlock> gridBlocks = new List<IMySlimBlock>( );
                Wrapper.GameAction( ( ) =>
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
                        && ((IMyTerminalBlock)fatBlock).CustomName.ToLower( ).Contains( "shipyard" ) )
                        {
                            grinders.Add( fatBlock );
                        }

                        else if ( fatBlock is IMyShipWelder
                             && ((IMyTerminalBlock)fatBlock).CustomName.ToLower( ).Contains( "shipyard" ) )
                        {
                            welders.Add( fatBlock );
                        }
                    }
                    catch ( Exception ex )
                    {
                         UtilityPlugin.Log.Error(ex);
                    }
                }
                //make sure the shipyard blocks are all connected to the conveyor system
                if ( grinders.Count == 8 )
                {
                    UtilityPlugin.Log.Info("class construct: " + grinders.Count.ToString()  );
                    MyOrientedBoundingBoxD? testBox = IsYardValid( entity, grinders );
                    if ( testBox.HasValue )
                    {
                        ShipyardItem newItem = new ShipyardItem(
                            testBox.Value,
                            grinders.ToList(),
                            ShipyardItem.ShipyardType.Grind,
                            entity );
                        ShipyardsList.Add(newItem  );
                        UtilityPlugin.Log.Info("class constructed: " + newItem.Tools.Count.ToString() );
                    }
                }
                
                if ( welders.Count == 8 )
                {
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
                return MathUtility.CreateOrientedBoundingBox( (MyCubeGrid)entity, points );
            }
            
            return null;
        }
    }
}
