using System;

namespace FixWorld.Pathfinding
{
    internal sealed class ShadowConnectivityGrid
    {
        private const int LeafSize = 8;
        private const int RegionSize = 16;
        private const int SuperChunkSize = 32;
        private const ulong EastSourceMask = 0x7F7F7F7F7F7F7F7FUL;
        private const ulong WestSourceMask = 0xFEFEFEFEFEFEFEFEUL;
        private readonly int leafColumns;
        private readonly int leafRows;
        private readonly int regionColumns;
        private readonly int regionRows;
        private readonly int superChunkColumns;
        private readonly int superChunkRows;
        private readonly ulong[] leafMasks;
        private readonly ulong[] publishedLeafMasks;
        private readonly int[] leafLabels;
        private readonly bool[] leafBuilt;
        private readonly bool[] dirtyLeafFlags;
        private readonly int[] dirtyLeafQueue;
        private readonly int[] regionLabels;
        private readonly int[] regionBoundary;
        private readonly bool[] regionBuilt;
        private readonly bool[] dirtyRegionFlags;
        private readonly int[] dirtyRegionQueue;
        private readonly int[] superChunkLabels;
        private readonly int[] superChunkBoundary;
        private readonly bool[] superChunkBuilt;
        private readonly bool[] dirtySuperChunkFlags;
        private readonly int[] dirtySuperChunkQueue;
        private readonly int[] leafBuildLabels;
        private readonly int[] mappingBuild;
        private readonly int[] boundaryBuild;
        private readonly int[] unionParents;
        private readonly int[] unionMinimums;
        private readonly int[] globalParents;
        private readonly bool[] globalActive;
        private int dirtyLeafCount;
        private int dirtyRegionCount;
        private int dirtySuperChunkCount;
        private bool pendingChanges;

        internal ShadowConnectivityGrid(int width, int height)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            Width = width;
            Height = height;
            leafColumns = ((width - 1) >> 3) + 1;
            leafRows = ((height - 1) >> 3) + 1;
            regionColumns = (leafColumns + 1) >> 1;
            regionRows = (leafRows + 1) >> 1;
            superChunkColumns = (regionColumns + 1) >> 1;
            superChunkRows = (regionRows + 1) >> 1;
            int leafCount = CheckedProduct(leafColumns, leafRows);
            int regionCount = CheckedProduct(regionColumns, regionRows);
            int superChunkCount = CheckedProduct(superChunkColumns, superChunkRows);
            leafMasks = new ulong[leafCount];
            publishedLeafMasks = new ulong[leafCount];
            leafLabels = NewLabels(CheckedProduct(leafCount, 64));
            leafBuilt = new bool[leafCount];
            dirtyLeafFlags = new bool[leafCount];
            dirtyLeafQueue = new int[leafCount];
            regionLabels = NewLabels(CheckedProduct(regionCount, 256));
            regionBoundary = NewLabels(CheckedProduct(regionCount, 64));
            regionBuilt = new bool[regionCount];
            dirtyRegionFlags = new bool[regionCount];
            dirtyRegionQueue = new int[regionCount];
            superChunkLabels = NewLabels(CheckedProduct(superChunkCount, 1024));
            superChunkBoundary = NewLabels(CheckedProduct(superChunkCount, 128));
            superChunkBuilt = new bool[superChunkCount];
            dirtySuperChunkFlags = new bool[superChunkCount];
            dirtySuperChunkQueue = new int[superChunkCount];
            leafBuildLabels = NewLabels(64);
            mappingBuild = NewLabels(1024);
            boundaryBuild = NewLabels(128);
            unionParents = new int[1024];
            unionMinimums = new int[1024];
            int globalNodeCapacity = CheckedProduct(superChunkCount, 1024);
            globalParents = new int[globalNodeCapacity];
            globalActive = new bool[globalNodeCapacity];
            for (int i = 0; i < leafCount; i++)
            {
                EnqueueLeaf(i);
            }

            for (int i = 0; i < regionCount; i++)
            {
                EnqueueRegion(i);
            }

            for (int i = 0; i < superChunkCount; i++)
            {
                EnqueueSuperChunk(i);
            }

            pendingChanges = true;
        }

        public int Width { get; }
        public int Height { get; }
        public long Generation { get; private set; }
        public long GraphRebuildCount { get; private set; }
        public int GraphNodeCount { get; private set; }
        public int GraphEdgeCount { get; private set; }

        public bool SetWalkable(int x, int z, bool walkable)
        {
            ValidateCell(x, z);
            int leafX = x >> 3;
            int leafZ = z >> 3;
            int leafIndex = leafX + (leafZ * leafColumns);
            ulong bit = 1UL << (((z & 7) << 3) | (x & 7));
            bool oldValue = (leafMasks[leafIndex] & bit) != 0;
            if (oldValue == walkable)
            {
                return false;
            }

            if (walkable)
            {
                leafMasks[leafIndex] |= bit;
            }
            else
            {
                leafMasks[leafIndex] &= ~bit;
            }

            MarkDirtyNeighborhood(x, z, leafX, leafZ);
            pendingChanges = true;
            return true;
        }

        public bool IsWalkable(int x, int z)
        {
            ValidateCell(x, z);
            int leafIndex = (x >> 3) + ((z >> 3) * leafColumns);
            ulong bit = 1UL << (((z & 7) << 3) | (x & 7));
            return (leafMasks[leafIndex] & bit) != 0;
        }

        public ShadowRebuildStats Rebuild()
        {
            if (!pendingChanges)
            {
                return new ShadowRebuildStats(0, 0, 0, 0, 0, 0);
            }

            int rebuiltLeaves = 0;
            int changedLeaves = 0;
            for (int i = 0; i < dirtyLeafCount; i++)
            {
                int index = dirtyLeafQueue[i];
                dirtyLeafFlags[index] = false;
                rebuiltLeaves++;
                if (RebuildLeaf(index))
                {
                    changedLeaves++;
                    int leafX = index % leafColumns;
                    int leafZ = index / leafColumns;
                    EnqueueRegion((leafX >> 1) + ((leafZ >> 1) * regionColumns));
                }
            }
            dirtyLeafCount = 0;
            int rebuiltRegions = 0;
            int changedRegions = 0;
            for (int i = 0; i < dirtyRegionCount; i++)
            {
                int index = dirtyRegionQueue[i];
                dirtyRegionFlags[index] = false;
                rebuiltRegions++;
                if (RebuildRegion(index))
                {
                    changedRegions++;
                    int regionX = index % regionColumns;
                    int regionZ = index / regionColumns;
                    EnqueueSuperChunk((regionX >> 1) + ((regionZ >> 1) * superChunkColumns));
                }
            }
            dirtyRegionCount = 0;
            int rebuiltSuperChunks = 0;
            int changedSuperChunks = 0;
            for (int i = 0; i < dirtySuperChunkCount; i++)
            {
                int index = dirtySuperChunkQueue[i];
                dirtySuperChunkFlags[index] = false;
                rebuiltSuperChunks++;
                if (RebuildSuperChunk(index))
                {
                    changedSuperChunks++;
                }
            }
            dirtySuperChunkCount = 0;
            if (changedSuperChunks > 0)
            {
                RebuildGlobalGraph();
            }

            pendingChanges = false;
            Generation++;
            return new ShadowRebuildStats(rebuiltLeaves, changedLeaves, rebuiltRegions, changedRegions, rebuiltSuperChunks, changedSuperChunks);
        }

        public bool AreConnected(int startX, int startZ, int targetX, int targetZ)
        {
            ValidateCell(startX, startZ);
            ValidateCell(targetX, targetZ);
            if (pendingChanges)
            {
                throw new InvalidOperationException("The shadow connectivity grid has pending changes.");
            }

            int startComponent = GetComponent(startX, startZ, SuperChunkSize);
            int targetComponent = GetComponent(targetX, targetZ, SuperChunkSize);
            if (startComponent < 0 || targetComponent < 0)
            {
                return false;
            }

            int startSuperChunk = (startX >> 5) + ((startZ >> 5) * superChunkColumns);
            int targetSuperChunk = (targetX >> 5) + ((targetZ >> 5) * superChunkColumns);
            int startNode = (startSuperChunk * 1024) + startComponent;
            int targetNode = (targetSuperChunk * 1024) + targetComponent;
            return FindGlobal(startNode) == FindGlobal(targetNode);
        }

        public int GetComponent(int x, int z, int chunkSize)
        {
            ValidateCell(x, z);
            if (chunkSize != LeafSize && chunkSize != RegionSize && chunkSize != SuperChunkSize)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be 8, 16, or 32.");
            }

            if (pendingChanges)
            {
                throw new InvalidOperationException("The shadow connectivity grid has pending changes.");
            }

            switch (chunkSize)
            {
                case LeafSize:
                    int leafIndex = (x >> 3) + ((z >> 3) * leafColumns);
                    return leafLabels[(leafIndex * 64) + (((z & 7) << 3) | (x & 7))];
                case RegionSize:
                    return GetRegionComponent((x >> 4) + ((z >> 4) * regionColumns), x & 15, z & 15);
                case SuperChunkSize:
                    int regionIndex = (x >> 4) + ((z >> 4) * regionColumns);
                    int regionComponent = GetRegionComponent(regionIndex, x & 15, z & 15);
                    if (regionComponent < 0)
                    {
                        return -1;
                    }

                    int superIndex = (x >> 5) + ((z >> 5) * superChunkColumns);
                    int quadrant = (((z >> 4) & 1) << 1) | ((x >> 4) & 1);
                    return superChunkLabels[(superIndex * 1024) + (quadrant * 256) + regionComponent];
                default:
                    return -1;
            }
        }

        private bool RebuildLeaf(int index)
        {
            ulong walkable = leafMasks[index];
            for (int i = 0; i < 64; i++)
            {
                leafBuildLabels[i] = -1;
            }

            ulong remaining = walkable;
            while (remaining != 0)
            {
                ulong seed = remaining & (0UL - remaining);
                int component = TrailingZeroCount(seed);
                ulong cells = seed;
                ulong frontier = seed;
                remaining &= ~seed;
                while (frontier != 0)
                {
                    ulong neighbors = ((frontier & EastSourceMask) << 1) | ((frontier & WestSourceMask) >> 1) | (frontier << 8) | (frontier >> 8);
                    ulong next = neighbors & walkable & ~cells;
                    cells |= next;
                    remaining &= ~next;
                    frontier = next;
                }
                ulong componentCells = cells;
                while (componentCells != 0)
                {
                    ulong cell = componentCells & (0UL - componentCells);
                    leafBuildLabels[TrailingZeroCount(cell)] = component;
                    componentCells &= ~cell;
                }
            }
            int offset = index * 64;
            bool changed = !leafBuilt[index] || publishedLeafMasks[index] != walkable;
            if (!changed)
            {
                for (int i = 0; i < 64; i++)
                {
                    if (leafLabels[offset + i] != leafBuildLabels[i])
                    { changed = true; break; }
                }
            }
            for (int i = 0; i < 64; i++)
            {
                leafLabels[offset + i] = leafBuildLabels[i];
            }

            publishedLeafMasks[index] = walkable;
            leafBuilt[index] = true;
            return changed;
        }

        private bool RebuildRegion(int index)
        {
            int regionX = index % regionColumns;
            int regionZ = index / regionColumns;
            InitializeUnion(256);
            for (int childZ = 0; childZ < 2; childZ++)
            {
                for (int childX = 0; childX < 2; childX++)
                {
                    int leafX = (regionX << 1) + childX;
                    int leafZ = (regionZ << 1) + childZ;
                    if (leafX >= leafColumns || leafZ >= leafRows)
                    {
                        continue;
                    }

                    int leafOffset = (leafX + (leafZ * leafColumns)) * 64;
                    int tokenBase = ((childZ << 1) | childX) * 64;
                    for (int position = 0; position < 64; position++)
                    {
                        int label = leafLabels[leafOffset + position];
                        if (label < 0)
                        {
                            continue;
                        }

                        int parentPosition = ((childZ << 3) + (position >> 3)) * 16 + (childX << 3) + (position & 7);
                        int token = tokenBase + label;
                        if (parentPosition < unionMinimums[token])
                        {
                            unionMinimums[token] = parentPosition;
                        }
                    }
                }
            }

            UnionRegionLeafBoundary(regionX, regionZ, true, 0);
            UnionRegionLeafBoundary(regionX, regionZ, true, 1);
            UnionRegionLeafBoundary(regionX, regionZ, false, 0);
            UnionRegionLeafBoundary(regionX, regionZ, false, 1);
            FinalizeMapping(256);
            BuildRegionBoundary(index);
            return PublishMapping(regionLabels, regionBoundary, regionBuilt, index, 256, 64);
        }

        private bool RebuildSuperChunk(int index)
        {
            int superX = index % superChunkColumns;
            int superZ = index / superChunkColumns;
            int regionBaseX = superX << 1;
            int regionBaseZ = superZ << 1;
            InitializeUnion(1024);
            for (int childZ = 0; childZ < 2; childZ++)
            {
                for (int childX = 0; childX < 2; childX++)
                {
                    int regionX = regionBaseX + childX;
                    int regionZ = regionBaseZ + childZ;
                    if (regionX >= regionColumns || regionZ >= regionRows)
                    {
                        continue;
                    }

                    int childOffset = (regionX + (regionZ * regionColumns)) * 256;
                    int tokenBase = ((childZ << 1) | childX) * 256;
                    for (int label = 0; label < 256; label++)
                    {
                        int position = regionLabels[childOffset + label];
                        if (position < 0)
                        {
                            continue;
                        }

                        int parentPosition = ((childZ << 4) + (position >> 4)) * 32 + (childX << 4) + (position & 15);
                        unionMinimums[tokenBase + position] = parentPosition;
                    }
                }
            }

            UnionSuperRegionBoundary(regionBaseX, regionBaseZ, true, 0);
            UnionSuperRegionBoundary(regionBaseX, regionBaseZ, true, 1);
            UnionSuperRegionBoundary(regionBaseX, regionBaseZ, false, 0);
            UnionSuperRegionBoundary(regionBaseX, regionBaseZ, false, 1);
            FinalizeMapping(1024);
            BuildSuperChunkBoundary(index);
            return PublishMapping(superChunkLabels, superChunkBoundary, superChunkBuilt, index, 1024, 128);
        }

        private void RebuildGlobalGraph()
        {
            int nodes = 0;
            int superChunkCount = superChunkColumns * superChunkRows;
            for (int superChunk = 0; superChunk < superChunkCount; superChunk++)
            {
                int offset = superChunk * 1024;
                for (int slot = 0; slot < 1024; slot++)
                {
                    globalActive[offset + slot] = false;
                }

                for (int slot = 0; slot < 1024; slot++)
                {
                    int component = superChunkLabels[offset + slot];
                    if (component < 0)
                    {
                        continue;
                    }

                    int node = offset + component;
                    globalParents[node] = node;
                    if (!globalActive[node])
                    {
                        globalActive[node] = true;
                        nodes++;
                    }
                }
            }

            int edges = 0;
            for (int superZ = 0; superZ < superChunkRows; superZ++)
            {
                for (int superX = 0; superX < superChunkColumns; superX++)
                {
                    int first = superX + (superZ * superChunkColumns);
                    int firstBoundary = first * 128;
                    if (superX + 1 < superChunkColumns)
                    {
                        int second = first + 1;
                        int secondBoundary = second * 128;
                        for (int position = 0; position < SuperChunkSize; position++)
                        {
                            int firstComponent = superChunkBoundary[firstBoundary + 32 + position];
                            int secondComponent = superChunkBoundary[secondBoundary + position];
                            if (firstComponent < 0 || secondComponent < 0)
                            {
                                continue;
                            }

                            UnionGlobal(
                                (first * 1024) + firstComponent,
                                (second * 1024) + secondComponent);
                            edges++;
                        }
                    }

                    if (superZ + 1 < superChunkRows)
                    {
                        int second = first + superChunkColumns;
                        int secondBoundary = second * 128;
                        for (int position = 0; position < SuperChunkSize; position++)
                        {
                            int firstComponent = superChunkBoundary[firstBoundary + 96 + position];
                            int secondComponent = superChunkBoundary[secondBoundary + 64 + position];
                            if (firstComponent < 0 || secondComponent < 0)
                            {
                                continue;
                            }

                            UnionGlobal(
                                (first * 1024) + firstComponent,
                                (second * 1024) + secondComponent);
                            edges++;
                        }
                    }
                }
            }

            GraphNodeCount = nodes;
            GraphEdgeCount = edges;
            GraphRebuildCount++;
        }

        private bool PublishMapping(int[] published, int[] publishedBoundary, bool[] built, int index, int size, int boundarySize)
        {
            int offset = index * size;
            int boundaryOffset = index * boundarySize;
            bool changed = !built[index];
            if (!changed)
            {
                for (int i = 0; i < size; i++)
                {
                    if (published[offset + i] != mappingBuild[i])
                    { changed = true; break; }
                }
            }
            if (!changed)
            {
                for (int i = 0; i < boundarySize; i++)
                {
                    if (publishedBoundary[boundaryOffset + i] != boundaryBuild[i])
                    { changed = true; break; }
                }
            }

            for (int i = 0; i < size; i++)
            {
                published[offset + i] = mappingBuild[i];
            }

            for (int i = 0; i < boundarySize; i++)
            {
                publishedBoundary[boundaryOffset + i] = boundaryBuild[i];
            }

            built[index] = true;
            return changed;
        }

        private void UnionRegionLeafBoundary(int regionX, int regionZ, bool vertical, int offset)
        {
            for (int i = 0; i < 8; i++)
            {
                int left;
                int right;
                int leftBase;
                int rightBase;
                if (vertical)
                {
                    left = GetLeafLabel(regionX << 1, (regionZ << 1) + offset, 7, i);
                    right = GetLeafLabel((regionX << 1) + 1, (regionZ << 1) + offset, 0, i);
                    leftBase = offset == 0 ? 0 : 128;
                    rightBase = offset == 0 ? 64 : 192;
                }
                else
                {
                    left = GetLeafLabel((regionX << 1) + offset, regionZ << 1, i, 7);
                    right = GetLeafLabel((regionX << 1) + offset, (regionZ << 1) + 1, i, 0);
                    leftBase = offset == 0 ? 0 : 64;
                    rightBase = offset == 0 ? 128 : 192;
                }
                if (left >= 0 && right >= 0)
                {
                    Union(leftBase + left, rightBase + right);
                }
            }
        }

        private void UnionSuperRegionBoundary(int regionBaseX, int regionBaseZ, bool vertical, int offset)
        {
            int leftRegionX = vertical ? regionBaseX : regionBaseX + offset;
            int leftRegionZ = vertical ? regionBaseZ + offset : regionBaseZ;
            int rightRegionX = vertical ? leftRegionX + 1 : leftRegionX;
            int rightRegionZ = vertical ? leftRegionZ : leftRegionZ + 1;
            int leftQuadrant = vertical ? (offset << 1) : offset;
            int rightQuadrant = vertical ? leftQuadrant + 1 : leftQuadrant + 2;
            for (int i = 0; i < RegionSize; i++)
            {
                int left = GetRegionComponent(leftRegionX, leftRegionZ, vertical ? 15 : i, vertical ? i : 15);
                int right = GetRegionComponent(rightRegionX, rightRegionZ, vertical ? 0 : i, vertical ? i : 0);
                if (left >= 0 && right >= 0)
                {
                    Union((leftQuadrant * 256) + left, (rightQuadrant * 256) + right);
                }
            }
        }

        private void BuildRegionBoundary(int index)
        {
            int regionX = index % regionColumns;
            int regionZ = index / regionColumns;
            for (int i = 0; i < RegionSize; i++)
            {
                boundaryBuild[i] = GetRegionBuildComponent(regionX, regionZ, 0, i);
                boundaryBuild[16 + i] = GetRegionBuildComponent(regionX, regionZ, RegionSize - 1, i);
                boundaryBuild[32 + i] = GetRegionBuildComponent(regionX, regionZ, i, 0);
                boundaryBuild[48 + i] = GetRegionBuildComponent(regionX, regionZ, i, RegionSize - 1);
            }
        }

        private void BuildSuperChunkBoundary(int index)
        {
            for (int i = 0; i < SuperChunkSize; i++)
            {
                boundaryBuild[i] = GetSuperChunkComponent(index, 0, i, mappingBuild);
                boundaryBuild[32 + i] = GetSuperChunkComponent(index, SuperChunkSize - 1, i, mappingBuild);
                boundaryBuild[64 + i] = GetSuperChunkComponent(index, i, 0, mappingBuild);
                boundaryBuild[96 + i] = GetSuperChunkComponent(index, i, SuperChunkSize - 1, mappingBuild);
            }
        }

        private int GetLeafLabel(int leafX, int leafZ, int localX, int localZ)
        {
            if (leafX < 0 || leafX >= leafColumns || leafZ < 0 || leafZ >= leafRows)
            {
                return -1;
            }

            return leafLabels[((leafX + (leafZ * leafColumns)) * 64) + (localZ << 3) + localX];
        }

        private int GetRegionComponent(int index, int localX, int localZ)
        {
            if (index < 0 || index >= regionColumns * regionRows)
            {
                return -1;
            }

            int regionX = index % regionColumns;
            int regionZ = index / regionColumns;
            return GetRegionComponent(regionX, regionZ, localX, localZ);
        }

        private int GetRegionComponent(int regionX, int regionZ, int localX, int localZ)
        {
            return GetRegionComponent(regionX, regionZ, localX, localZ, regionLabels);
        }

        private int GetRegionComponent(int regionX, int regionZ, int localX, int localZ, int[] mappings)
        {
            if (regionX < 0 || regionX >= regionColumns || regionZ < 0 || regionZ >= regionRows)
            {
                return -1;
            }

            int leafLabel = GetLeafLabel((regionX << 1) + (localX >> 3), (regionZ << 1) + (localZ >> 3), localX & 7, localZ & 7);
            if (leafLabel < 0)
            {
                return -1;
            }

            int quadrant = ((localZ >> 3) << 1) | (localX >> 3);
            return mappings[((regionX + (regionZ * regionColumns)) * 256) + (quadrant * 64) + leafLabel];
        }

        private int GetRegionBuildComponent(int regionX, int regionZ, int localX, int localZ)
        {
            if (regionX < 0 || regionX >= regionColumns || regionZ < 0 || regionZ >= regionRows)
            {
                return -1;
            }

            int leafLabel = GetLeafLabel((regionX << 1) + (localX >> 3), (regionZ << 1) + (localZ >> 3), localX & 7, localZ & 7);
            if (leafLabel < 0)
            {
                return -1;
            }

            int quadrant = ((localZ >> 3) << 1) | (localX >> 3);
            return mappingBuild[(quadrant * 64) + leafLabel];
        }

        private int GetSuperChunkComponent(int index, int localX, int localZ, int[] mappings)
        {
            int superX = index % superChunkColumns;
            int superZ = index / superChunkColumns;
            int regionX = (superX << 1) + (localX >> 4);
            int regionZ = (superZ << 1) + (localZ >> 4);
            int regionComponent = GetRegionComponent(regionX, regionZ, localX & 15, localZ & 15);
            if (regionComponent < 0)
            {
                return -1;
            }

            int quadrant = (((localZ >> 4) & 1) << 1) | ((localX >> 4) & 1);
            return mappings[(quadrant * 256) + regionComponent];
        }

        private void FinalizeMapping(int count)
        {
            for (int token = 0; token < count; token++)
            {
                mappingBuild[token] = unionMinimums[token] == int.MaxValue ? -1 : unionMinimums[Find(token)];
            }
        }

        private void InitializeUnion(int count)
        {
            for (int i = 0; i < count; i++)
            { unionParents[i] = i; unionMinimums[i] = int.MaxValue; }
        }

        private int Find(int token)
        {
            int root = token;
            while (unionParents[root] != root)
            {
                root = unionParents[root];
            }

            while (unionParents[token] != token)
            { int next = unionParents[token]; unionParents[token] = root; token = next; }
            return root;
        }

        private int FindGlobal(int node)
        {
            int root = node;
            while (globalParents[root] != root)
            {
                root = globalParents[root];
            }

            while (globalParents[node] != node)
            {
                int next = globalParents[node];
                globalParents[node] = root;
                node = next;
            }

            return root;
        }

        private void UnionGlobal(int first, int second)
        {
            int firstRoot = FindGlobal(first);
            int secondRoot = FindGlobal(second);
            if (firstRoot == secondRoot)
            {
                return;
            }

            if (firstRoot > secondRoot)
            {
                int swap = firstRoot;
                firstRoot = secondRoot;
                secondRoot = swap;
            }

            globalParents[secondRoot] = firstRoot;
        }

        private void Union(int first, int second)
        {
            int firstRoot = Find(first);
            int secondRoot = Find(second);
            if (firstRoot == secondRoot)
            {
                return;
            }

            if (firstRoot > secondRoot)
            { int swap = firstRoot; firstRoot = secondRoot; secondRoot = swap; }
            unionParents[secondRoot] = firstRoot;
            if (unionMinimums[secondRoot] < unionMinimums[firstRoot])
            {
                unionMinimums[firstRoot] = unionMinimums[secondRoot];
            }
        }

        private void MarkDirtyNeighborhood(int x, int z, int leafX, int leafZ)
        {
            EnqueueLeaf(leafX + (leafZ * leafColumns));
            bool left = (x & 7) == 0 && leafX > 0;
            bool right = (x & 7) == 7 && x + 1 < Width;
            bool down = (z & 7) == 0 && leafZ > 0;
            bool up = (z & 7) == 7 && z + 1 < Height;
            if (left)
            {
                EnqueueLeaf((leafX - 1) + (leafZ * leafColumns));
            }

            if (right)
            {
                EnqueueLeaf((leafX + 1) + (leafZ * leafColumns));
            }

            if (down)
            {
                EnqueueLeaf(leafX + ((leafZ - 1) * leafColumns));
            }

            if (up)
            {
                EnqueueLeaf(leafX + ((leafZ + 1) * leafColumns));
            }

            if (left && down)
            {
                EnqueueLeaf((leafX - 1) + ((leafZ - 1) * leafColumns));
            }

            if (left && up)
            {
                EnqueueLeaf((leafX - 1) + ((leafZ + 1) * leafColumns));
            }

            if (right && down)
            {
                EnqueueLeaf((leafX + 1) + ((leafZ - 1) * leafColumns));
            }

            if (right && up)
            {
                EnqueueLeaf((leafX + 1) + ((leafZ + 1) * leafColumns));
            }
        }

        private void EnqueueLeaf(int index)
        {
            if (dirtyLeafFlags[index])
            {
                return;
            }

            dirtyLeafFlags[index] = true;
            dirtyLeafQueue[dirtyLeafCount++] = index;
        }

        private void EnqueueRegion(int index)
        {
            if (dirtyRegionFlags[index])
            {
                return;
            }

            dirtyRegionFlags[index] = true;
            dirtyRegionQueue[dirtyRegionCount++] = index;
        }

        private void EnqueueSuperChunk(int index)
        {
            if (dirtySuperChunkFlags[index])
            {
                return;
            }

            dirtySuperChunkFlags[index] = true;
            dirtySuperChunkQueue[dirtySuperChunkCount++] = index;
        }

        private void ValidateCell(int x, int z)
        {
            if (x < 0 || x >= Width)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }

            if (z < 0 || z >= Height)
            {
                throw new ArgumentOutOfRangeException(nameof(z));
            }
        }

        private static int CheckedProduct(int first, int second)
        {
            long product = (long)first * second;
            if (product > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException("dimensions", "The grid is too large.");
            }

            return (int)product;
        }

        private static int[] NewLabels(int length)
        {
            int[] labels = new int[length];
            for (int i = 0; i < length; i++)
            {
                labels[i] = -1;
            }

            return labels;
        }

        private static int TrailingZeroCount(ulong value)
        {
            int count = 0;
            while ((value & 1UL) == 0)
            {
                value >>= 1;
                count++;
            }
            return count;
        }
    }

    internal readonly struct ShadowRebuildStats
    {
        internal ShadowRebuildStats(int rebuiltLeaves, int changedLeaves, int rebuiltRegions, int changedRegions, int rebuiltSuperChunks, int changedSuperChunks)
        {
            RebuiltLeaves = rebuiltLeaves;
            ChangedLeaves = changedLeaves;
            RebuiltRegions = rebuiltRegions;
            ChangedRegions = changedRegions;
            RebuiltSuperChunks = rebuiltSuperChunks;
            ChangedSuperChunks = changedSuperChunks;
        }
        public int RebuiltLeaves { get; }
        public int ChangedLeaves { get; }
        public int RebuiltRegions { get; }
        public int ChangedRegions { get; }
        public int RebuiltSuperChunks { get; }
        public int ChangedSuperChunks { get; }
    }
}
