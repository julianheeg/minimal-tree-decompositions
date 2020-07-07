﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Tamaki_Tree_Decomp.Data_Structures;
using static Tamaki_Tree_Decomp.Data_Structures.Graph;

namespace Tamaki_Tree_Decomp
{
    /// <summary>
    /// a class for finding safe separators and handling the reconstruction of partial tree decompositions
    /// </summary>
    public class SafeSeparator
    {
        Graph graph;

        BitSet separator;
        public int separatorSize;

        List<ReindecationMapping> reconstructionIndexationMappings; // mapping from reduced vertex id to original vertex id, by component

        private readonly bool verbose;

        public static bool separate = true; // this can be set to false in order to easily disable
                                            // the search for safe separators for debugging reasons
        public static bool size3Separate = true;

        public SafeSeparator(Graph graph, bool verbose = true)
        {
            this.graph = graph;

            this.verbose = verbose;
        }

        /// <summary>
        /// tries to separate the graph using safe separators. If successful the minK parameter is set to the maximum of minK and the separator size
        /// </summary>
        /// <param name="separatedGraphs">a list of the separated graphs, if a separator exists, else null</param>
        /// <param name="minK">the minimum tree width parameter. If a separator is found that is greater than minK, it is set to the separator size</param>
        /// <returns>true iff a separation has been performed</returns>
        public bool Separate(out List<Graph> separatedGraphs, ref int minK)
        {
            if (!separate)
            {
                graph = null;   // remove reference to the graph so that its ressources can be freed
                separatedGraphs = null;
                reconstructionIndexationMappings = null;
                return false;
            }

            if (FindSize1Separator() || FindSize2Separator() || HeuristicDecomposition() || FindSize3Separator())
            {
                if (verbose)
                {
                    Console.WriteLine("graph {0} contains a safe separator: {1}", graph.graphID, separator.ToString());
                }
                List<int> separatorVertices = separator.Elements();
                separatorSize = separatorVertices.Count;
                graph.MakeIntoClique(separatorVertices);

                separatedGraphs = graph.Separate(separator, out reconstructionIndexationMappings);

                if (minK < separatorSize)   // TODO: correct? not mink < separator size -1 for heuristic decomposition?
                {
                    minK = separatorSize;
                }

                // remove reference to the graph so that its ressources can be freed
                graph = null;

                return true;
            }
            /*
            else if (HeuristicDecomposition())
            {
                if (verbose)
                {
                    Console.WriteLine("clique minor found: {0}", separator.ToString());
                    PrintSeparation();
                }
                separatedGraphs = subGraphs;
                if (minK < separatorSize - 1)
                {
                    minK = separatorSize - 1;   // TODO: correct?
                }
                return true;
            }
            */

            reconstructionIndexationMappings = null;
            separatedGraphs = null;
            return false;
        }

        #region exact safe separator search

        // TODO: check if graph is connected, otherwise split immediately

        /// <summary>
        /// lists all articulation points of the graph
        /// This method can also be used to test for separators of size n by passing a set of n-1 vertices as
        /// ignoredVertices. and combining the result with the ignoredVertices.
        /// Code adapated from https://slideplayer.com/slide/12811727/
        /// </summary>
        /// <param name="ignoredVertices">a list of vertices to treat as non-existent</param>
        /// <returns>an iterator over all articulation points</returns>
        public IEnumerable<int> ArticulationPoints(List<int> ignoredVertices = null)
        {
            if (graph.vertexCount == 0)
            {
                yield break;
            }

            ignoredVertices = ignoredVertices ?? new List<int>();   // create an empty list if the given one is null in order to prevent NullReferenceExceptions

            int start = 0;
            while (ignoredVertices.Contains(start))
            {
                start++;
            }
            int[] count = new int[graph.vertexCount];                     // TODO: make member variable
            int[] reachBack = new int[graph.vertexCount];                 // TODO: make member variable
            Queue<int>[] children = new Queue<int>[graph.vertexCount];    // TODO: make member variable
            for (int i = 0; i < graph.vertexCount; i++)
            {
                count[i] = int.MaxValue;
                reachBack[i] = int.MaxValue;
                children[i] = new Queue<int>();
            }
            count[start] = 0;
            int numSubtrees = 0;
            
            for (int i = 0; i < graph.adjacencyList[start].Count; i++)
            {
                int rootNeighbor = graph.adjacencyList[start][i];
                if (count[rootNeighbor] == int.MaxValue && !ignoredVertices.Contains(rootNeighbor))
                {
                    Stack<(int, int, int)> stack = new Stack<(int, int, int)>();
                    stack.Push((rootNeighbor, 1, start));
                    while (stack.Count > 0)
                    {
                        (int node, int timer, int fromNode) = stack.Peek();
                        if (count[node] == int.MaxValue)
                        {
                            count[node] = timer;
                            reachBack[node] = timer;

                            List<int> neighbors = graph.adjacencyList[node];
                            for (int j = 0; j < neighbors.Count; j++)
                            {
                                int neighbor = neighbors[j];
                                if (neighbor != fromNode && !ignoredVertices.Contains(neighbor))
                                {
                                    children[node].Enqueue(neighbor);
                                }
                            }
                        }
                        else if (children[node].Count > 0)
                        {
                            int child = children[node].Dequeue();
                            if (count[child] < int.MaxValue)
                            {
                                if (reachBack[node] > count[child])
                                {
                                    reachBack[node] = count[child];
                                }
                            }
                            else
                            {
                                stack.Push((child, timer + 1, node));
                            }
                        }
                        else
                        {
                            if (node != rootNeighbor)
                            {
                                if (reachBack[node] >= count[fromNode])
                                {
                                    yield return fromNode;
                                }
                                if (reachBack[fromNode] > reachBack[node])
                                {
                                    reachBack[fromNode] = reachBack[node];
                                }
                            }
                            stack.Pop();
                        }
                    }

                    numSubtrees++;
                }
            }
            if (numSubtrees > 1)
            {
                yield return start;
            }
        }

        /// <summary>
        /// Tests if the graph can be separated with a separator of size 1. If so, the graph is split and the
        /// resulting subgraphs and the reconstruction mappings are saved in the corresponding member variables.
        /// </summary>
        /// <returns>true iff a size 1 separator exists</returns>
        private bool FindSize1Separator()
        {
            foreach (int a in ArticulationPoints())
            {
                separator = new BitSet(graph.vertexCount);
                separator[a] = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Tests if the graph can be separated with a safe separator of size 2. If so, the graph is split and the
        /// resulting subgraphs and the reconstruction mappings are saved in the corresponding member variables
        /// </summary>
        /// <returns>true iff a size 2 separator exists</returns>
        public bool FindSize2Separator()
        {
            List<int> ignoredVertices = new List<int>(1) { -1 };

            // loop over every pair of vertices
            for (int u = 0; u < graph.vertexCount; u++)
            {
                ignoredVertices[0] = u;
                foreach (int a in ArticulationPoints(ignoredVertices))
                {
                    separator = new BitSet(graph.vertexCount);
                    separator[u] = true;
                    separator[a] = true;
                    return true;
                }
            }
            return false;
        }

        [ThreadStatic]
        public static Stopwatch size3SeparationStopwatch;
        [ThreadStatic]
        public static int size3separators = 0;

        /// <summary>
        /// Tests if the graph can be separated with a safe separator of size 3. If so, the graph is split and the
        /// resulting subgraphs and the reconstruction mappings are saved in the corresponding member variables
        /// </summary>
        /// <returns>true iff a size 2 separator exists</returns>
        public bool FindSize3Separator()
        {
            if (!size3Separate)
            {
                return false;
            }
            if (size3SeparationStopwatch == null)
            {
                size3SeparationStopwatch = new Stopwatch();
            }
            size3SeparationStopwatch.Start();
            List<int> ignoredVertices = new List<int>(2) { -1, -1 };

            // loop over every pair of vertices
            for (int u = 0; u < graph.vertexCount; u++)
            {
                ignoredVertices[0] = u;
                for (int v = u + 1; v < graph.vertexCount; v++)
                {
                    ignoredVertices[1] = v;
                    foreach (int a in ArticulationPoints(ignoredVertices))
                    {
                        // TODO: needs only have at least two vertices. Don't use the Components and neighbors function
                        BitSet candidateSeparator = new BitSet(graph.vertexCount);
                        candidateSeparator[u] = true;
                        candidateSeparator[v] = true;
                        candidateSeparator[a] = true;

                        bool safe = true;

                        foreach ((BitSet component, BitSet neighbors) in graph.ComponentsAndNeighbors(candidateSeparator))
                        {
                            Debug.Assert(component.Count() > 0);
                            if (component.Count() == 1)
                            {
                                safe = false;
                                break;
                            }
                        }

                        if (safe)
                        {
                            separator = candidateSeparator;
                            size3SeparationStopwatch.Stop();
                            size3separators++;
                            return true;
                        }
                    }
                }
            }
            size3SeparationStopwatch.Stop();
            return false;
        }

        [ThreadStatic]
        public static Stopwatch cliqueSeparatorStopwatch = new Stopwatch();
        [ThreadStatic]
        public static int cliqueSeparators = 0;

        public bool FindCliqueSeparator(int ignoredVertex)
        {
            cliqueSeparatorStopwatch.Start();

            MCS_M_Plus(out int[] alpha, out Graph H, out BitSet X);

            //Atoms(alpha, H, X, out List<Graph> atoms, out List<BitSet> minSeps, out List<BitSet> cliqueSeps);

            cliqueSeparatorStopwatch.Stop();
            throw new NotImplementedException();
        }

        /*
        private void Atoms(int[] alpha, Graph H, BitSet X, out List<Graph> atoms, out List<BitSet> S_H, out List<BitSet> S_c)
        {
            CopyGraph(out List<int>[] G_adjacencyList, out BitSet[] G_neighbors);
            CopyGraph(out List<int>[] H_adjacencyList, out BitSet[] H_neighbors, out BitSet[] H_neighborsWith);   // TODO: do we need to copy at all? Isn't the graph thrown away afterwards?

            atoms = new List<Graph>();
            S_H = new List<BitSet>();
            S_c = new List<BitSet>();

            for (int i = 0; i < vertexCount; i++)
            {
                int x = alpha[i];
                if (X[x])
                {
                    S_H.Add(H_neighbors[x]);    // TODO: copy?
                    List<int> S = H_adjacencyList[x];

                    // test if S is clique
                    // (intersect the neighbors of x with the neighbors' neighbors and test if that is equal to x' neighbors)
                    BitSet intersection = new BitSet(H_neighborsWith[x]);
                    for (int j = 0; j < S.Count; j++)
                    {
                        intersection.IntersectWith(H_neighborsWith[S[j]]);  // TODO: possibly test for early exit
                    }
                    if (intersection.Equals(H_neighborsWith[x]))
                    {
                        S_c.Add(H_neighbors[x]);
                        BitSet C = null;    // TODO: connected component of G'-S containing x
                        atoms.Add(null);    // TODO: add G'(S union C)
                        // TODO: removed vertices in G' <- removed vertices in G' union C
                    }
                }

                // remove x from H'
                // TODO: use removed vertices Bitset instead
                for (int j = 0; j < H_adjacencyList[x].Count; j++)
                {
                    int neighbor = H_adjacencyList[x][j];
                    H_adjacencyList[neighbor].Remove(x);
                }
                H_adjacencyList[x].Clear();
            }

            atoms.Add(null); // G'

            throw new NotImplementedException();
        }
        */

        /// <summary>
        /// An Introduction to Clique Minimal Separator Decomposition
        /// Anne Berry, Romain Pogorelcnik, Geneviève Simonet
        /// </summary>
        /// <param name="alpha"></param>
        /// <param name="H"></param>
        /// <param name="x"></param>
        private void MCS_M_Plus(out int[] alpha, out Graph H, out BitSet X)
        {
            // init
            alpha = new int[graph.vertexCount];
            HashSet<(int, int)> F = new HashSet<(int, int)>();
            Graph G_prime = new Graph(graph);
            bool[] reached = new bool[graph.vertexCount];
            BitSet[] reach = new BitSet[graph.vertexCount];   // TODO: HashSet?
            int[] labels = new int[graph.vertexCount];
            for (int i = 0; i < graph.vertexCount; i++)
            {
                labels[i] = 0;
            }
            int s = -1;
            X = new BitSet(graph.vertexCount);

            for (int i = graph.vertexCount - 1; i >= 0; i--)
            {
                int x = -1; // TODO: use priority queue?
                int maxLabel = -1;
                for (int j = 0; j < graph.vertexCount; j++)
                {
                    if (maxLabel < labels[j])
                    {
                        maxLabel = labels[j];
                        x = j;
                    }
                }
                List<int> Y = G_prime.adjacencyList[x];

                if (labels[x] <= s)
                {
                    X[x] = true;
                }

                s = labels[x];

                Array.Clear(reached, 0, graph.vertexCount);
                reached[x] = true;

                for (int j = 0; j < graph.vertexCount; j++)   // for j=0 to n-1   <---- n-1 kann evtl auch die Größe des verbleibenden Graphen sein
                {
                    reach[j] = new BitSet(graph.vertexCount);
                }

                int y;
                for (int j = 0; j < Y.Count; j++)
                {
                    y = Y[j];
                    reached[y] = true;
                    reach[labels[y]][y] = true;
                }

                for (int j = 0; j < graph.vertexCount; j++)
                {
                    while (!reach[j].IsEmpty())
                    {
                        y = reach[j].First();
                        reach[j][y] = false;

                        int z;
                        for (int k = 0; k < G_prime.adjacencyList[y].Count; k++)
                        {
                            z = G_prime.adjacencyList[y][k];

                            if (!reached[z])
                            {
                                reached[z] = true;
                                if (labels[z] > j)
                                {
                                    Y.Add(z);
                                    reach[labels[z]][z] = true;
                                }
                                else
                                {
                                    reach[j][z] = true;
                                }
                            }
                        }
                    }
                }

                for (int j = 0; j < Y.Count; j++)
                {
                    y = Y[j];
                    if (!F.Contains((y, x)))
                    {
                        F.Add((x, y));
                    }
                    labels[y]++;
                }

                alpha[i] = x;

                // remove x from G'
                G_prime.Remove(x);
            }

            // build H = (V, E+F)
            H = new Graph(graph);
            foreach((int u, int v) in F)
            {
                H.Insert(u, v);
            }
        }

        #endregion

        #region heuristic safe separator search

        /// <summary>
        /// Tries to find a safe separator using heuristics. If so, the graph is split and the resulting
        /// subgraphs and the reconstruction mappings are saved in the corresponding member variables.
        /// </summary>
        /// <returns>true iff a safe separator has been found</returns>
        public bool HeuristicDecomposition()
        {
            // consider all candidate separators
            foreach (BitSet candidateSeparator in CandidateSeparators())
            {
                if (IsSafeSeparator_Heuristic(candidateSeparator))
                {
                    separator = candidateSeparator;
                    return true;
                }

            }
            return false;
        }

        private static readonly int MAX_MISSINGS = 100;
        private static readonly int MAX_STEPS = 1000000;

        /// <summary>
        /// tests heuristically if a candidate separator is a safe separator. If this method returns true then the separator is guaranteed to be safe. False negatives are possible, however.
        /// </summary>
        /// <param name="candidateSeparator">the candidate separator to test</param>
        /// <returns>true iff the used heuristic gives a guarantee that the candidate is a safe separator</returns>
        public bool IsSafeSeparator_Heuristic(BitSet candidateSeparator)  // TODO: make private
        {
            bool isFirstComponent = true;

            // try to find a contraction of each component where the candidate separator is a labelled minor
            foreach ((BitSet, BitSet) C_NC in graph.ComponentsAndNeighbors(candidateSeparator))
            {
                BitSet component = C_NC.Item1;

                // test two things:
                //   1. test if only one component exists. In that case we don't have a safe separator (or a separator at all, for that matter)
                //      the test ist done by testing if the union of the candidate and the component is equal to the entire vertex set
                //   2. test if the number of missing edges is larger than the max missings parameter. In that case we give up on this candidate
                // both things need to be tested only once, obviously, therefore we test it only when we examine the first component.
                if (isFirstComponent)
                {
                    isFirstComponent = false;

                    // test if there is only one component associated with the separator
                    BitSet allTest = new BitSet(candidateSeparator);
                    allTest.UnionWith(component);
                    if (allTest.Equals(graph.notRemovedVertices))
                    {
                        return false;
                    }
                    
                    // count missing edges
                    int missingEdges = 0;
                    foreach (int v in candidateSeparator.Elements())
                    {
                        BitSet separator = new BitSet(candidateSeparator);
                        separator.ExceptWith(graph.neighborSetsWithout[v]);
                        missingEdges += (int)separator.Count() - 1;
                    }
                    missingEdges /= 2;

                    if (missingEdges > MAX_MISSINGS)
                    {
                        return false;
                    }
                }

                // search for the candidate separator as a clique minor on the remaining graph without the component
                BitSet sep = C_NC.Item2;
                BitSet rest = new BitSet(graph.notRemovedVertices);
                rest.ExceptWith(sep);
                rest.ExceptWith(component);
                if (!FindCliqueMinor(sep, rest))
                {
                    return false;
                }                
            }

            return true;
        }

        #region Tamaki's logic

        private class Edge
        {
            public readonly int left1;
            public readonly int left2;
            public bool unAugmentable;

            internal Edge(int left1, int left2)
            {
                this.left1 = left1;
                this.left2 = left2;
            }

            /// <summary>
            /// tries to find a pair of right nodes that can cover this edge and that are still available 
            /// </summary>
            /// <param name="rightNodes">the list of right nodes</param>
            /// <param name="available">the set of available nodes</param>
            /// <param name="graph">the underlying graph</param>
            /// <param name="coveringPair">a covering pair of right nodes if one exists, else null</param>
            /// <returns>true, iff a covering pair could be found</returns>
            internal bool FindCoveringPair(List<RightNode> rightNodes, BitSet available, Graph graph, out (RightNode, RightNode) coveringPair)
            {
                foreach (RightNode right1 in rightNodes)
                {
                    if (right1.neighborSet[left1] && !right1.neighborSet[left2])
                    {
                        foreach (RightNode right2 in rightNodes)
                        {
                            if (right2.neighborSet[left2] && !right2.neighborSet[left1])
                            {
                                // ----- lines 508 to 519 -----

                                BitSet vs1 = right1.neighborSet;
                                BitSet vs2 = right2.neighborSet;
                                BitSet vs = new BitSet(vs1);

                                while (true)
                                {
                                    BitSet ns = graph.Neighbors(vs);
                                    if (ns.Intersects(vs2))
                                    {
                                        coveringPair = (right1, right2);
                                        return true;
                                    }
                                    ns.IntersectWith(available);
                                    if (ns.IsEmpty())
                                    {
                                        break;
                                    }
                                    vs.UnionWith(ns);
                                }
                            }
                        }
                    }
                }
                coveringPair = (null, null);
                return false;
            }

            internal bool IsFinallyCovered(List<RightNode> rightNodes)
            {
                foreach (RightNode rightNode in rightNodes)
                {
                    if (rightNode.FinallyCovers(this))
                    {
                        return true;
                    }
                }
                return false;
            }

            public override string ToString()
            {
                return String.Format("({0},{1}), unaugmentable = {2}", left1 + 1, left2 + 1, unAugmentable);
            }
        }

        private class RightNode
        {
            internal BitSet vertexSet;
            internal BitSet neighborSet;
            internal int assignedTo = -1;

            public RightNode(int vertex, BitSet neighbors, int vertexCount)
            {
                vertexSet = new BitSet(vertexCount);
                vertexSet[vertex] = true;
                neighborSet = neighbors;    // TODO: copy?
            }

            public RightNode(BitSet vertexSet, BitSet neighbors)
            {
                this.vertexSet = vertexSet;
                neighborSet = neighbors;
            }

            internal bool PotentiallyCovers(Edge edge)
            {
                return assignedTo == -1 && neighborSet[edge.left1] && neighborSet[edge.left2];
            }

            internal bool FinallyCovers(Edge edge)
            {
                return assignedTo == edge.left1 && neighborSet[edge.left2] || assignedTo == edge.left2 && neighborSet[edge.left1];
            }

            public override string ToString()
            {
                return String.Format("vertices: {{{0}}}, neighbors: {{{1}}}, assigned to: {2}", vertexSet.ToString(), neighborSet.ToString(), assignedTo + 1);
            }
        }

        /// <summary>
        /// tries to contract edges withing a subgraph consisting of a separator and a vertex set in such a way that the separator becomes
        /// a clique minor. This method works heuristically, so false negatives are possible.
        /// </summary>
        /// <param name="separator">the separator</param>
        /// <param name="rest">the vertex set</param>
        /// <returns>true, iff the graph could be contracted to a a clique on the separator</returns>
        private bool FindCliqueMinor(BitSet separator, BitSet rest)
        {
            BitSet available = new BitSet(rest);
            List<int> leftNodes = separator.Elements();
            int separatorSize = leftNodes.Count;

            // build a list of the missing edges
            List<Edge> missingEdges = new List<Edge>();
            for (int i = 0; i < leftNodes.Count; i++)
            {
                int left1 = leftNodes[i];
                for (int j = i + 1; j < leftNodes.Count; j++)
                {
                    int left2 = leftNodes[j];
                    if (!graph.neighborSetsWithout[left1][left2])
                    {
                        missingEdges.Add(new Edge(left1, left2));
                    }
                }
            }

            // exit early if there is no missing edge
            if (missingEdges.Count == 0)
            {
                return true;
            }

            // TAMAKI: missingEdges -> set index


            // ----- lines 241 to 263 -----

            List<RightNode> rightNodes = new List<RightNode>();
            BitSet neighborSet = graph.Neighbors(separator);
            neighborSet.IntersectWith(rest);
            List<int> neighbors = neighborSet.Elements();

            for (int i = 0; i < neighbors.Count; i++)
            {
                int v = neighbors[i];
                if (graph.adjacencyList[v].Count == 1)
                {
                    continue;
                }
                bool useless = true;
                for (int j = 0; j < missingEdges.Count; j++)
                {
                    Edge missingEdge = missingEdges[j];
                    if (graph.neighborSetsWithout[v][missingEdge.left1] || graph.neighborSetsWithout[v][missingEdge.left2])
                    {
                        useless = false;
                        break;
                    }
                }

                if (useless)
                {
                    continue;
                }

                RightNode rn = new RightNode(v, graph.neighborSetsWithout[v], graph.vertexCount);
                rightNodes.Add(rn);
                available[v] = false;
            }


            int steps = 0;

            // ----- lines 265 to 281 -----

            while (FindZeroCoveredEdge(missingEdges, rightNodes, out Edge zeroCoveredEdge))
            {
                if (zeroCoveredEdge.FindCoveringPair(rightNodes, available, graph, out (RightNode, RightNode) coveringPair))
                {
                    MergeRightNodes(coveringPair, rightNodes, available, graph);
                }
                else
                {
                    return false;
                }

                steps++;
                if (steps >= MAX_STEPS)
                {
                    return false;
                }
            }

            // ----- lines 283 to 302 -----

            bool moving = true;
            while (rightNodes.Count > separatorSize / 2 && moving)
            {
                steps++;
                if (steps > MAX_STEPS)
                {
                    return false;
                }

                moving = false;
                if (FindLeastCoveredEdge(missingEdges, rightNodes, out Edge leastCoveredEdge))
                {
                    if (leastCoveredEdge.FindCoveringPair(rightNodes, available, graph, out (RightNode, RightNode) coveringPair))
                    {
                        MergeRightNodes(coveringPair, rightNodes, available, graph);
                        moving = true;
                    }
                    else
                    {
                        leastCoveredEdge.unAugmentable = true;
                    }
                }
            }

            // filter out right nodes that cover no missing edge potentially
            rightNodes =
                    rightNodes.FindAll(
                        rightNode => missingEdges.Exists(
                            edge => rightNode.PotentiallyCovers(edge)
                        )
                    );

            // ----- perform final contractions (lines 340 to 400) -----

            while (missingEdges.Count > 0)
            {
                // ----- find best covering pair (lines 347 to 383) -----

                int bestPair_left = -1;
                RightNode bestPair_right = null;
                int maxMinCover = 0;
                int maxFc = 0;

                foreach (int leftNode in leftNodes)
                {
                    foreach (RightNode rightNode in rightNodes)
                    {
                        if (rightNode.assignedTo != -1 || !rightNode.neighborSet[leftNode])
                        {
                            continue;
                        }
                        steps++;
                        if (steps > MAX_STEPS)
                        {
                            return false;
                        }
                        rightNode.assignedTo = leftNode;
                        int minCover = MinCover(missingEdges, rightNodes, graph.vertexCount);
                        int fc = 0;
                        foreach (Edge edge in missingEdges)
                        {
                            if (edge.IsFinallyCovered(rightNodes))
                            {
                                fc++;
                            }
                        }
                        rightNode.assignedTo = -1;
                        if (bestPair_left == -1 && bestPair_right == null || minCover > maxMinCover)
                        {
                            maxMinCover = minCover;
                            bestPair_left = leftNode;
                            bestPair_right = rightNode;
                            maxFc = fc;
                        }
                        else if (minCover == maxMinCover && fc > maxFc)
                        {
                            bestPair_left = leftNode;
                            bestPair_right = rightNode;
                            maxFc = fc;
                        }
                    }
                }
                if (maxMinCover == 0)
                {
                    return false;
                }

                // finally assign best pair
                bestPair_right.assignedTo = bestPair_left;

                // update missing edges list
                missingEdges.RemoveAll(edge => edge.IsFinallyCovered(rightNodes));
            }

            return true;
        }

        /// <summary>
        /// finds the augmentable missing edge, which is 'potentially covered' by the least amount of right nodes
        /// </summary>
        /// <param name="missingEdges">the list of missing edges</param>
        /// <param name="rightNodes">the list of right nodes</param>
        /// <param name="leastCoveredEdge">the least covered edge, if there is at least one such edge, else null</param>
        /// <returns>true, iff such an edge exists</returns>
        private bool FindLeastCoveredEdge(List<Edge> missingEdges, List<RightNode> rightNodes, out Edge leastCoveredEdge)
        {
            int minCover = 0;
            leastCoveredEdge = null;
            foreach (Edge edge in missingEdges)
            {
                if (edge.unAugmentable)
                {
                    continue;
                }
                int nCover = 0;
                foreach (RightNode rightNode in rightNodes)
                {
                    if (rightNode.PotentiallyCovers(edge))
                    {
                        nCover++;
                    }
                }
                if (leastCoveredEdge == null || nCover < minCover)
                {
                    minCover = nCover;
                    leastCoveredEdge = edge;
                }
            }
            return leastCoveredEdge != null;
        }

        /// <summary>
        /// determines the amount of right nodes that 'potentially cover' the least 'potentially covered' missing edge
        /// </summary>
        /// <param name="missingEdges">the list of missing edges</param>
        /// <param name="rightNodes">the list of right nodes</param>
        /// <param name="vertexCount">the graph's vertex count</param>
        /// <returns></returns>
        private int MinCover(List<Edge> missingEdges, List<RightNode> rightNodes, int vertexCount)
        {
            int minCover = vertexCount;
            foreach (Edge edge in missingEdges)
            {
                if (edge.IsFinallyCovered(rightNodes))
                {
                    continue;
                }
                int nCover = 0;
                foreach (RightNode rightNode in rightNodes)
                {
                    if (rightNode.PotentiallyCovers(edge))
                    {
                        nCover++;
                    }
                }
                if (nCover < minCover)
                {
                    minCover = nCover;
                }
            }
            return minCover;
        }

        /// <summary>
        /// merges the two right nodes of a covering pair
        /// </summary>
        /// <param name="coveringPair">the covering pair</param>
        /// <param name="rightNodes">the list of right nodes</param>
        /// <param name="available">the list of available nodes</param>
        /// <param name="graph">the underlying graph</param>
        private void MergeRightNodes((RightNode, RightNode) coveringPair, List<RightNode> rightNodes, BitSet available, Graph graph)
        {
            // ----- lines 523 to 558 -----

            RightNode rn1 = coveringPair.Item1;
            RightNode rn2 = coveringPair.Item2;

            BitSet vs1 = rn1.vertexSet;
            BitSet vs2 = rn2.vertexSet;

            List<BitSet> layerList = new List<BitSet>();

            BitSet vs = new BitSet(vs1);
            int counter = 0;
            while (true)
            {
                // TODO: remove debug stuff
                counter++;
                if (counter > 50)
                {
                    throw new Exception("Possibly infinite loop in MergeRightNodes detected.");
                }

                BitSet ns = graph.Neighbors(vs);
                if (ns.Intersects(vs2))
                {
                    break;
                }
                ns.IntersectWith(available);
                layerList.Add(ns);
                vs.UnionWith(ns);
            }

            BitSet result = new BitSet(vs1);
            result.UnionWith(vs2);

            BitSet back = graph.Neighbors(vs2);
            for (int i = layerList.Count - 1; i >= 0; i--)
            {
                BitSet ns = layerList[i];
                ns.IntersectWith(back);
                int v = ns.First();
                result[v] = true;
                available[v] = false;
                back = graph.neighborSetsWithout[v];
            }

            rightNodes.Remove(rn1);
            rightNodes.Remove(rn2);
            rightNodes.Add(new RightNode(result, graph.Neighbors(result)));
        }

        /// <summary>
        /// tries to find an edge that is not 'potentially covered' by any right node
        /// </summary>
        /// <param name="missingEdges">the list of edges that are still required to form the separator into a clique</param>
        /// <param name="rightNodes">the list of right nodes</param>
        /// <param name="zeroCoveredEdge">a zero covered edge if there is one, else null</param>
        /// <returns>true, iff a zero covered edge could be found</returns>
        bool FindZeroCoveredEdge(List<Edge> missingEdges, List<RightNode> rightNodes, out Edge zeroCoveredEdge)
        {
            foreach (Edge edge in missingEdges)
            {
                bool isCovered = false;
                foreach (RightNode rightNode in rightNodes)
                {
                    if (rightNode.PotentiallyCovers(edge))
                    {
                        isCovered = true;
                        break;
                    }
                }
                if (!isCovered)
                {
                    zeroCoveredEdge = edge;
                    return true;
                }
            }
            zeroCoveredEdge = null;
            return false;
        }

        /// <summary>
        /// finds candidate safe separators using a heuristic. This is Tamaki's code.
        /// </summary>
        /// <param name="mode"></param>
        /// <returns></returns>
        private List<BitSet> Tamaki_CandidateSeparators(Heuristic mode)
        {
            // code adapted from https://github.com/TCS-Meiji/PACE2017-TrackA/blob/master/tw/exact/GreedyDecomposer.java

            List<BitSet> separators = new List<BitSet>();

            // ----- lines 62 to 64 -----

            // copy fields so that we can change them locally
            Graph copy = new Graph(graph);

            List<BitSet> frontier = new List<BitSet>();
            BitSet remaining = BitSet.All(graph.vertexCount);

            // ----- lines 66 to 80 -----

            while (!remaining.IsEmpty())
            {
                // ----- lines 67 to 80 -----

                int vmin = FindMinCostVertex(remaining, copy, mode);

                // ----- line 82 -----

                List<BitSet> joined = new List<BitSet>();

                // lines 84 and 85 -----

                BitSet toBeAclique = new BitSet(graph.vertexCount);
                toBeAclique[vmin] = true;

                // ----- lines 87 to 93 -----

                foreach (BitSet s in frontier)
                {
                    if (s[vmin])
                    {
                        joined.Add(s);
                        toBeAclique.UnionWith(s);
                    }
                }

                // ----- lines 97 to 119 -----

                if (joined.Count == 0)
                {
                    toBeAclique[vmin] = true;
                }
                else if (joined.Count == 1)
                {
                    BitSet uniqueSeparator = joined[0];
                    BitSet test = new BitSet(copy.neighborSetsWithout[vmin]);
                    test.IntersectWith(remaining);
                    if (uniqueSeparator.IsSupersetOf(test))
                    {
                        uniqueSeparator[vmin] = false;
                        if (uniqueSeparator.IsEmpty())
                        {
                            separators.Remove(uniqueSeparator);

                            frontier.Remove(uniqueSeparator);
                        }
                        remaining[vmin] = false;
                        continue;
                    }
                }

                // ----- line 121 -----

                BitSet temp = new BitSet(copy.neighborSetsWithout[vmin]);
                temp.IntersectWith(remaining);
                toBeAclique.UnionWith(temp);

                // ----- line 129 -----

                copy.MakeIntoClique(toBeAclique.Elements());

                // ----- lines 131 and 132 -----
                BitSet sep = new BitSet(toBeAclique);
                sep[vmin] = false;

                // ----- lines 134 to 147 -----

                if (!sep.IsEmpty())
                {
                    BitSet separator = new BitSet(sep);
                    separators.Add(separator);
                    
                    frontier.Add(separator);
                }

                // ----- lines 153 to 161 -----
                foreach (BitSet s in joined)
                {
                    Debug.Assert(!s.IsEmpty());
                    
                    frontier.Remove(s);
                }
                remaining[vmin] = false;
            }

            // TODO: CRITICAL???? set width  ###########################################################################################################################################

            return separators;
        }

        #endregion

        /// <summary>
        /// finds candidate safe separators using every implemented heuristic
        /// </summary>
        /// <returns>an enumerable that lists candidate safe separators</returns>
        public IEnumerable<BitSet> CandidateSeparators()
        {
            HashSet<BitSet> tested = new HashSet<BitSet>();

            // loop over heuristics
            foreach (Heuristic heuristic in Enum.GetValues(typeof(Heuristic)))
            {
                // return candidates one by one
                foreach (BitSet candidate in Tamaki_CandidateSeparators(heuristic))
                {
                    if (!tested.Contains(candidate))
                    {
                        yield return candidate;
                        tested.Add(candidate);
                    }
                }
            }
        }

        /// <summary>
        /// an enumeration of implemented heuristics for finding small tree decompositions
        /// </summary>
        enum Heuristic
        {
            min_fill,
            min_degree
        }

        /// <summary>
        /// finds candidate safe separators using a heuristic
        /// </summary>
        /// <param name="mode">the decomposition heuristic</param>
        /// <returns>an enumerable that lists candidate safe separators</returns>
        private IEnumerable<BitSet> CandidateSeparators(Heuristic mode)
        {
            // copy fields so that we can change them locally
            Graph copy = new Graph(graph);

            BitSet remaining = BitSet.All(graph.vertexCount);

            // only to vertexCount - 1 because the empty set is trivially a safe separator
            for (int i = 0; i < graph.vertexCount - 1; i++)
            {
                int min = FindMinCostVertex(remaining, copy, mode);

                // TODO: separator might be wrong #################################################################################################################
                // TODO: add minK parameter and stop once a separator is too big (perhaps low can be used for that, but it is usually used a bit differently)

                //BitSet result = new BitSet(neighborSetsWithout[min]);

                
                // weird outlet computation, but it should work
                BitSet result = new BitSet(copy.neighborSetsWithout[min]);
                result[min] = true;
                result = graph.Neighbors(result);
                result = graph.Neighbors(result);
                result.IntersectWith(copy.neighborSetsWithout[min]);
                
                yield return result;
                // ################################################################################################################################################

                remaining[min] = false;
                for (int j = 0; j < copy.adjacencyList[min].Count; j++)
                {
                    int u = copy.adjacencyList[min][j];

                    // remove min from the neighbors' adjacency lists
                    copy.Remove(min);

                    // make neighbors into a clique

                    copy.MakeIntoClique(copy.adjacencyList[min]);
                }
            }
        }

        /// <summary>
        /// finds the vertex with the lowest cost to remove from a graph with respect to a heuristic
        /// </summary>
        /// <param name="remaining">the vertices that aren't yet removed (as opposed to ones that have been removed in a previous iteration)</param>
        /// <param name="adjacencyList">the adjacency list for the graph</param>
        /// <param name="neighborSetsWithout">the open neighborhood sets for the graph</param>
        /// <param name="mode">the decomposition heuristic</param>
        /// <returns>the vertex with the lowest cost to remove from the graph with respect to the chosen decomposition heuristic</returns>
        private int FindMinCostVertex(BitSet remaining, Graph mutableGraph, Heuristic mode)
        {
            List<int> remainingVertices = remaining.Elements();
            int min = int.MaxValue;
            int vmin = -1;

            switch (mode)
            {
                case Heuristic.min_fill:
                    foreach (int v in remainingVertices)
                    {
                        int fillEdges = 0;
                        BitSet neighbors = new BitSet(mutableGraph.neighborSetsWithout[v]);
                        
                        foreach (int neighbor in neighbors.Elements())
                        {
                            BitSet newEdges = new BitSet(neighbors);
                            newEdges.ExceptWith(mutableGraph.neighborSetsWithout[neighbor]);
                            fillEdges += (int)newEdges.Count() - 1;
                        }

                        if (fillEdges < min)
                        {
                            min = fillEdges;
                            vmin = v;
                        }
                    }
                    return vmin;

                case Heuristic.min_degree:
                    foreach (int v in remainingVertices)
                    {
                        if (mutableGraph.adjacencyList[v].Count < min)
                        {
                            min = mutableGraph.adjacencyList[v].Count;
                            vmin = v;
                        }
                    }
                    return vmin;

                default:
                    throw new NotImplementedException();
            }
        }

        #endregion

        #region recombination

        /// <summary>
        /// recombines tree decompositions for the subgraphs into a tree decomposition for the original graph
        /// </summary>
        /// <param name="ptds">the tree decompositions for each of the subgraphs</param>
        /// <returns>a tree decomposition for the original graph</returns>
        public PTD RecombineTreeDecompositions(List<PTD> ptds)
        {
            // re-index the tree decompositions for the subgraphs
            for (int i = 0; i < ptds.Count; i++)
            {
                ptds[i].Reindex(reconstructionIndexationMappings[i]);
            }

            // find separator node in the first ptd
            PTD separatorNode = null;
            Stack<PTD> nodeStack = new Stack<PTD>();
            nodeStack.Push(ptds[0]);
            while (nodeStack.Count > 0)
            {
                PTD currentNode = nodeStack.Pop();

                // exit when separator node is found
                if (currentNode.Bag.IsSupersetOf(separator))
                {
                    separatorNode = currentNode;
                    nodeStack.Clear();
                    break;
                }

                // push children onto the stack
                foreach (PTD childNode in currentNode.children)
                {
                    nodeStack.Push(childNode);
                }
            }


            // reroot the other tree decompositions and append them to the first one at the separator node
            for (int i = 1; i < ptds.Count; i++)
            {
                separatorNode.children.Add(ptds[i].Reroot(separator));
            }

            return ptds[0];
        }

        #endregion
    }
}
