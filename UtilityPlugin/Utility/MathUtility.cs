using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI;
using VRageMath;

namespace UtilityPlugin.Utility
{
    public static class MathUtility
    {
        /// <summary>
        ///     Determines if a list of 8 Vector3I defines a rectangular prism aligned to the grid
        /// </summary>
        /// <param name="points"></param>
        /// <returns></returns>
        public static bool ArePointsOrthogonal( List<Vector3I> points )
        {
            if ( !points.Any() || points.Count != 8 )
                return false;

            //get a list of unique Z values
            List<int> zVals = points.Select( x => x.Z ).Distinct().ToList();

            //we should only have two
            if ( zVals.Count() != 2 )
                return false;

            //get a list of all points in the two Z planes
            List<Vector3I> zPlaneMax = points.FindAll( x => x.Z == zVals.Max() );
            List<Vector3I> zPlaneMin = points.FindAll( x => x.Z == zVals.Min() );

            //we should have four of each
            if ( zPlaneMin.Count() != 4 || zPlaneMax.Count() != 4 )
                return false;

            //make sure each vertex in the maxZ plane has the same X and Y as only one point in the minZ plane
            foreach ( Vector3I zMaxPoint in zPlaneMax )
            {
                var matchCount = 0;
                foreach ( Vector3I zMinPoint in zPlaneMin )
                {
                    if ( zMinPoint.X == zMaxPoint.X
                         && zMinPoint.Y == zMaxPoint.Y )
                        matchCount++;
                }
                if ( matchCount != 1 )
                    return false;
            }

            return true;
        }

        /// <summary>
        ///     Create an OBB that encloses a grid
        /// </summary>
        /// <param name="grid"></param>
        /// <returns></returns>
        public static MyOrientedBoundingBoxD? CreateOrientedBoundingBox( IMyCubeGrid grid )
        {
            Quaternion gridQuaternion = Quaternion.CreateFromForwardUp(
                Vector3.Normalize( grid.WorldMatrix.Forward ),
                Vector3.Normalize( grid.WorldMatrix.Up ) );

            Vector3D[] aabbcorner = new Vector3D[8];
            grid.PositionComp.WorldAABB.GetCorners( aabbcorner );
            var gridHalf = grid.WorldAABB.HalfExtents;
            Vector3D halfExtents = new Vector3D( gridHalf.X * .9, gridHalf.Y * .9, gridHalf.Z * .9 );

            return new MyOrientedBoundingBoxD( grid.PositionComp.WorldAABB.Center, halfExtents, gridQuaternion );
        }

        /// <summary>
        ///     Create an OBB from a list of verticies and align it to a grid
        /// </summary>
        /// <param name="grid"></param>
        /// <param name="verticies"></param>
        /// <returns></returns>
        public static MyOrientedBoundingBoxD? CreateOrientedBoundingBox( IMyCubeGrid grid, List<Vector3D> verticies )
        {
            //because I'm paranoid
            if ( grid?.Physics == null || grid.Closed )
                return null;
            if ( verticies.Count == 0 )
                return null;

            //create the quaternion to rotate the box around
            Quaternion yardQuaternion = Quaternion.CreateFromForwardUp(
                Vector3.Normalize( grid.WorldMatrix.Forward ),
                Vector3.Normalize( grid.WorldMatrix.Up ) );

            //find the center of the volume
            var yardCenter = new Vector3D();

            foreach ( Vector3D vertex in verticies )
                yardCenter = Vector3D.Add( yardCenter, vertex );

            yardCenter = Vector3D.Divide( yardCenter, verticies.Count );

            //find the dimensions of the box.

            //convert verticies to grid coordinates to find adjoining neighbors
            List<Vector3I> gridVerticies = new List<Vector3I>( verticies.Count );

            foreach ( var vertext in verticies )
                gridVerticies.Add( grid.WorldToGridInteger( vertext ) );

            Vector3D referenceVertex = verticies[0];
            var xLength = 0d;
            var yLength = 0d;
            var zLength = 0d;

            //finds the length of each axis
            for ( var i = 1; i < verticies.Count; ++i )
            {
                Vector3D thisVertex = verticies[i];
                if ( gridVerticies[0].Y == gridVerticies[i].Y
                     && gridVerticies[0].Z == gridVerticies[i].Z )
                    xLength = Math.Abs( Vector3D.Distance( referenceVertex, thisVertex ) );

                if ( gridVerticies[0].X == gridVerticies[i].X
                     && gridVerticies[0].Z == gridVerticies[i].Z )
                    yLength = Math.Abs( Vector3D.Distance( referenceVertex, thisVertex ) );

                if ( gridVerticies[0].X == gridVerticies[i].X
                     && gridVerticies[0].Y == gridVerticies[i].Y )
                    zLength = Math.Abs( Vector3D.Distance( referenceVertex, thisVertex ) );
            }

            var halfExtents = new Vector3D( xLength / 2, yLength / 2, zLength / 2 );

            //FINALLY we can make the bounding box
            return new MyOrientedBoundingBoxD( yardCenter, halfExtents, yardQuaternion );
        }
    }
}