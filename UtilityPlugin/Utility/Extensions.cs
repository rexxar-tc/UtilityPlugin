using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using VRageMath;

namespace UtilityPlugin.Utility
{
    public static class Extensions
    {
        public static void Stop( this MyCubeGrid grid )
        {
            if ( grid?.Physics == null )
                return;

            if ( grid.Physics.LinearVelocity == Vector3D.Zero && grid.Physics.AngularVelocity == Vector3D.Zero )
                return;

            Wrapper.GameAction( ( ) =>
             {
                 grid.Physics.LinearVelocity = Vector3D.Zero;
                 grid.Physics.AngularVelocity = Vector3D.Zero;
             } );
        }
    }

    public static class GridSplitExtensions
    {
        public static HashSet<MySlimBlock> GetGridSplitPoints(this MyCubeGrid cubeGrid)
        {
            var splitPoints = new HashSet<MySlimBlock>();
 
            foreach (var slimBlock in cubeGrid.GetBlocks())
                if (slimBlock.IsGridSplitPoint())
                    splitPoints.Add(slimBlock);
 
            return splitPoints;
        }
 
        private class DistancePair
        {
            public MySlimBlock Target { get; private set; }
            public MySlimBlock Neighbour { get; private set; }
 
            private int m_distanceSquared;
            public int DistanceSquared
            {
                get
                {
                    // If we haven't calculated the distance yet, we're gonna have to now.
                    if (m_distanceSquared == -1)
                    {
                        m_distanceSquared = 0;
                        m_distanceSquared += (int)Math.Pow(Target.Position.X - Neighbour.Position.X, 2);
                        m_distanceSquared += (int)Math.Pow(Target.Position.Y - Neighbour.Position.Y, 2);
                        m_distanceSquared += (int)Math.Pow(Target.Position.Z - Neighbour.Position.Z, 2);
                    }
 
                    return m_distanceSquared;
                }
 
                private set
                {
                    m_distanceSquared = value;
                }
            }
 
            public DistancePair(MySlimBlock target, MySlimBlock neighbour)
            {
                Target = target;
                Neighbour = neighbour;
                DistanceSquared = -1;
            }
        }
 
        public static bool IsGridSplitPoint(this MySlimBlock slimBlock)
        {
            // Don't waste time if there's only one neighbour.
            if (slimBlock.Neighbours.Count <= 1)
                return false;
 
            // Run a quick check on all the neighbours. If one of them has no other neighbours,
            // this block is definitely a split-point, and we can go ahead and quit.
            foreach (var neighbour in slimBlock.Neighbours)
                if (neighbour.Neighbours.Count == 1)
                    return true;
 
            // Make a list of all the targets we want to search for
            var searchTargets = new List<MySlimBlock>();
 
            // Add each neighbour of the target block (except the first neighbour) to the list of targets
            for(int i = 1; i < slimBlock.Neighbours.Count; ++i)
                searchTargets.Add(slimBlock.Neighbours[i]);
 
            // Create a list to track blocks that we've already iterated through
            // We don't need to access any of these block, just check whether they match each current block,
            // so we can simplify the list by just tracking the .GetHashCode() result of each block
            var iteratedBlocks = new HashSet<int>();
           
            // Record the target block as iterated, so we don't iterate it later
            iteratedBlocks.Add(slimBlock.GetHashCode());
 
            // Create a stack for distance calculations, to minimize the amount of calculation work we need to do,
            // and to allow us to iterate the grid as a tree, with weighting towards desired targets.
            var dataStack = new Stack<List<DistancePair>>();
 
            // Iterate through the grid until we find all the targets, or have no more neighbours to traverse,
            // beginning with the target block's first neighbour
            var currentBlock = slimBlock.Neighbours[0];
            while (true)
            {
                List<DistancePair> distanceData;
 
                // If the current block exists (will be null if we just came up a step in the stack)
                // and hasn't been iterated yet, perform distance calculations and add them to the stacks
                if(currentBlock != null)
                    if (!iteratedBlocks.Contains(currentBlock.GetHashCode()))
                    {
                        // Create a list to store distances between each neighbour (6 max) and target (5 max)
                        distanceData = new List<DistancePair>(30);
                   
                        // Check for the presence of and perform calculations for each target
                        for(int i = 0; i < searchTargets.Count; ++i)
                        {
                            var target = searchTargets[i];
 
                            // Check if any of the current neighbours is the target
                            if (currentBlock.Neighbours.Contains(target))
                            {
                                // Remove the target from the list and adjust our index accordingly
                                searchTargets.RemoveAt(i--);
 
                                // If all targets have been found, we're done. The given block is not a split-point
                                if (searchTargets.Count == 0)
                                    return false;
                            }
 
                            // Otherwise, calculate and save the distance from each neighbour to the target
                            else
                                foreach (var neighbour in currentBlock.Neighbours)
                                    distanceData.Add(new DistancePair(target, neighbour));
                        }
 
                        // Sort the distance data and add it to the stack.
                        dataStack.Push(distanceData.OrderBy(data => data.DistanceSquared).ToList());
                   
                        // This block has now been iterated
                        iteratedBlocks.Add(currentBlock.GetHashCode());
                    }
 
                // Find the first target/neighbour pair where the target hasn't been found and the neighbour hasn't been iterated
                // And advance to that neighbour block for the next iteration.
                currentBlock = null;
                distanceData = dataStack.Peek();
                for(int i = 0; i < distanceData.Count; ++i)
                {
                    var distancePair = distanceData[i];
 
                    if (!searchTargets.Contains(distancePair.Target))
                        distanceData.RemoveAt(i--);
 
                    else if (iteratedBlocks.Contains(distancePair.Neighbour.GetHashCode()))
                        distanceData.RemoveAt(i--);
 
                    else
                    {
                        currentBlock = distancePair.Neighbour;
                        break;
                    }
                }
 
                // If no next block could be found, we need to step back up the stack.
                if(currentBlock == null)
                {
                    // If we've reached the bottom of the stack, we're done. We weren't able to find all the targets,
                    // so the given block is a split-point.
                    if (dataStack.Count == 1)
                        return true;
 
                    dataStack.Pop();
                }
            }
        }
    }
}