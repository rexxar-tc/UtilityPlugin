using System.Collections.Generic;
using System.Linq;
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
    }
}
