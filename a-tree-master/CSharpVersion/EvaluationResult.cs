using System;
using System.Collections.Generic;

namespace ATree 
{
    
    public class EvaluationResult<T>
    {
        private readonly List<bool?> _results; // Using List<bool?> to mimic Slab behavior with Option<bool>
        private readonly List<bool> _isEvaluated; // To track if a node has been evaluated
        private readonly List<int> _andCounts; // For tracking AND node children evaluations

        public EvaluationResult(int capacity)
        {
            _results = new List<bool?>(capacity);
            _isEvaluated = new List<bool>(capacity);
            _andCounts = new List<int>(capacity);
            for (int i = 0; i < capacity; i++)
            {
                _results.Add(null);
                _isEvaluated.Add(false);
                _andCounts.Add(0); 
            }
        }

        public bool IsEvaluated(int nodeId)
        {
            if (nodeId < 0 || nodeId >= _isEvaluated.Count) return false; // Or throw
            return _isEvaluated[nodeId];
        }

        public void SetEvaluated(int nodeId) 
        {
            if (nodeId >= 0 && nodeId < _isEvaluated.Count)
            {
                _isEvaluated[nodeId] = true;
            }
        }

        public bool? GetResult(int nodeId)
        {
            if (nodeId < 0 || nodeId >= _results.Count) return null;
            return _results[nodeId];
        }

        public void SetResult(int nodeId, bool? result)
        {
            if (nodeId < 0) return; 
            EnsureCapacity(nodeId + 1);
            _results[nodeId] = result;
            _isEvaluated[nodeId] = true;
        }

        public void EnsureCapacity(int capacity)
        {
            while (capacity > _results.Count) // Use > instead of >= to add up to capacity
            {
                _results.Add(null);
                _isEvaluated.Add(false);
                _andCounts.Add(0);
            }
        }

        public int IncrementAndCount(int nodeId)
        {
            if (nodeId < 0 || nodeId >= _andCounts.Count) return 0; 
            _andCounts[nodeId]++;
            return _andCounts[nodeId];
        }
    }
}
