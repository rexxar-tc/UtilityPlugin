using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using SEModAPIExtensions.API;
using UtilityPlugin.Utility;
using VRage.Game.Entity;
using VRage.Library.Collections;
using VRage.ModAPI;
using VRageMath;

namespace UtilityPlugin.ProcessHandlers
{
    public class ProcessWeldHandler : ProcessHandlerBase
    {

        private static List<BoundingBoxD> shipyards = new List<BoundingBoxD>( );
        public override int GetUpdateResolution( )
        {
            return 1000;
        }

        public override void Handle( )
        {
            UpdateShipyards( );
            if (!shipyards.Any())
                return;

            foreach ( BoundingBoxD shipyard in shipyards )
            {
                
                List<IMyEntity> entities = new List<IMyEntity>( );
                
                BoundingBoxD thisShipyard = new BoundingBoxD( shipyard.Min, shipyard.Max );
                
                Wrapper.GameAction( ( ) =>
                 {
                     entities = MyAPIGateway.Entities.GetElementsInBox( ref thisShipyard );
                 } );
                if ( !entities.Any( ) )
                    continue;

                foreach ( IMyEntity entity in entities )
                {
                    var grid = entity as MyCubeGrid;
                    if (grid == null)
                        continue;

                    if (shipyard.Contains(new BoundingBoxD(grid.GridIntegerToWorld(grid.Min), grid.GridIntegerToWorld(grid.Max))) == ContainmentType.Contains)
                    {
                        //don't allow ships to be welded if they're moving
                       if (grid.Physics.LinearVelocity == Vector3D.Zero
                           && grid.Physics.AngularVelocity == Vector3D.Zero
                           && !grid.Physics.IsStatic)
                       {
                            //Communication.SendPublicInformation("Welding ship");
                            //we're allowed to weld this ship
                            if (!StepWeld(grid))
                            {
                                //Communication.SendPublicInformation("Ship is finished");
                                //welding is done, notify the player
                            }
                        }
                    }
                }
            }
            base.Handle();
        }

        private void UpdateShipyards( )
        {
            //clear the shipyards so we don't have to bother checking for yards to remove
            if(shipyards.Any())
            shipyards.Clear();

            HashSet<IMyEntity> entities = new HashSet<IMyEntity>( );
            Wrapper.GameAction( ( ) =>
             {
                 MyAPIGateway.Entities.GetEntities( entities, x => x is IMyCubeGrid );
             } );

            foreach (IMyEntity entity in entities)
            {
                List<IMyCubeBlock> welders = new List<IMyCubeBlock>();

                if (entity.Physics == null || !entity.Physics.IsStatic)
                    continue;

                if (!(entity is IMyCubeGrid))
                    return;

                List<IMySlimBlock> gridBlocks = new List<IMySlimBlock>();
                ((IMyCubeGrid) entity).GetBlocks(gridBlocks);

                foreach (IMySlimBlock slimBlock in gridBlocks)
                {
                    if (slimBlock == null)
                        continue;
                    
                    if (slimBlock.FatBlock == null)
                        continue;
                    
                    if (!(slimBlock.FatBlock is IMyShipWelder))
                        continue;
                    
                    if (((IMyTerminalBlock)slimBlock.FatBlock).CustomName.ToLower().Contains("shipyard"))
                    {
                            welders.Add(slimBlock.FatBlock);
                    }
                }

                if (welders.Any() && welders.Count != 8)
                    continue;
                
                List<Vector3D> points = new List<Vector3D>();
                List<Vector3I> gridPoints = new List<Vector3I>();
                foreach (IMyCubeBlock welder in welders)
                {
                    gridPoints.Add(welder.Position);
                    points.Add(welder.PositionComp.GetPosition());
                }

                if (MathUtility.ArePointsOrthogonal(gridPoints))
                {
                    shipyards.Add(BoundingBoxD.CreateFromPoints(points));
                    //Communication.SendPublicInformation("Found shipyard");
                }
            }
        }

        private static bool StepWeld( MyCubeGrid grid )
        {
            int blockCount = 0;
            foreach ( MySlimBlock block in grid.CubeBlocks )
            {
                if (block == null)
                    continue;
                
                block.FillConstructionStockpile();
                if ( !block.IsFullIntegrity || block.HasDeformation )
                {
                    Wrapper.GameAction(() =>
                    {
                        block.IncreaseMountLevel(1f, 0, null, 1f, true);
                    });
                        blockCount++;
                }
                if (blockCount == 8)
                    return true;
            }
            if (blockCount > 0)
                return true;

            return false;
        }

    }
}
