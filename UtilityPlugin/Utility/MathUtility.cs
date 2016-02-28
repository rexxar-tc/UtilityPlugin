using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using VRageMath;

namespace UtilityPlugin.Utility
{
    public static class MathUtility
    {

        /// <summary>
        /// Determines if a list of 8 Vector3I defines a rectangular prism aligned to the grid
        /// </summary>
        /// <param name="points"></param>
        /// <returns></returns>
        public static bool ArePointsOrthogonal( List<Vector3I> points )
        {
            if ( !points.Any( ) || points.Count != 8 )
                return false;

            //get a list of unique Z values
            List<int> zVals = points.Select( x => x.Z ).Distinct( ).ToList( );

            //we should only have two
            if ( zVals.Count( ) != 2 )
                return false;

            //get a list of all points in the two Z planes
            List<Vector3I> zPlaneMax = points.FindAll( x => x.Z == zVals.Max( ) );
            List<Vector3I> zPlaneMin = points.FindAll( x => x.Z == zVals.Min( ) );

            //we should have four of each
            if ( zPlaneMin.Count( ) != 4 || zPlaneMax.Count( ) != 4 )
                return false;

            //make sure each vertex in the maxZ plane has the same X and Y as only one point in the minZ plane
            foreach ( Vector3I zMaxPoint in zPlaneMax )
            {
                int matchCount = 0;
                foreach ( Vector3I zMinPoint in zPlaneMin )
                {
                    if ( zMinPoint.X == zMaxPoint.X
                      && zMinPoint.Y == zMaxPoint.Y )
                    {
                        matchCount++;
                    }
                }
                if ( matchCount != 1 )
                    return false;
            }

            return true;
        }

        public static MyOrientedBoundingBoxD? CreateOrientedBoundingBox(IMyCubeGrid grid)
        {
            List<Vector3D> verticies = new List<Vector3D>();
            verticies.Add(grid.GridIntegerToWorld(grid.Min));
            verticies.Add(grid.GridIntegerToWorld(grid.Max));

            return CreateOrientedBoundingBox(grid, verticies);
        }

        public static MyOrientedBoundingBoxD? CreateOrientedBoundingBox( IMyCubeGrid grid, List<Vector3D> verticies )
        {
            //because I'm paranoid
            if ( grid == null )
                return null;
            if ( grid.Physics == null )
                return null;

            //create the quaternion to rotate the box around
            Quaternion yardQuaternion = Quaternion.CreateFromForwardUp(
                Vector3.Normalize( grid.WorldMatrix.Forward ),
                Vector3.Normalize( grid.WorldMatrix.Up ) );

            //find opposing verticies
            Vector3D maxVertex = new Vector3D( );
            Vector3D minVertex = new Vector3D( );

            foreach ( Vector3D vertex in verticies )
            {
                if ( vertex.LengthSquared( ) > maxVertex.LengthSquared( ) )
                    maxVertex = vertex;
                if ( vertex.LengthSquared( ) < minVertex.LengthSquared( ) )
                    minVertex = vertex;
            }

            //find the midpoint which is the center of the volume
            Vector3D yardCenter = new Vector3D(
                (maxVertex.X + minVertex.X) / 2,
                (maxVertex.Y + minVertex.Y) / 2,
                (maxVertex.Z + minVertex.Z) / 2 );

            //find the dimensions of the box. 
            //we're converting to the grid coordinates because the math is easier
            List<Vector3I> gridVerticies = new List<Vector3I>( );

            foreach ( Vector3D vertex in verticies )
            {
                gridVerticies.Add( grid.WorldToGridInteger( vertex ) );
            }

            Vector3I referenceVertex = gridVerticies[0];
            int xLength = 0;
            int yLength = 0;
            int zLength = 0;

            //finds the length of each axis
            for ( int i = 1; i < gridVerticies.Count; ++i )
            {
                Vector3I thisVertex = gridVerticies[i];
                if ( referenceVertex.Y == thisVertex.Y
                  && referenceVertex.Z == thisVertex.Z )
                    xLength = Math.Abs( referenceVertex.X - thisVertex.X );

                if ( referenceVertex.X == thisVertex.X
                  && referenceVertex.Z == thisVertex.Z )
                    yLength = Math.Abs( referenceVertex.Y - thisVertex.Y );

                if ( referenceVertex.X == thisVertex.X
                  && referenceVertex.Y == thisVertex.Y )
                    zLength = Math.Abs( referenceVertex.Z - thisVertex.Z );
            }
            
            Vector3D halfExtents = new Vector3D( xLength / 2, yLength / 2, zLength / 2 );

            //FINALLY we can make the bounding box
            return new MyOrientedBoundingBoxD( yardCenter, halfExtents, yardQuaternion );

        }
    }
}
