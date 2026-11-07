using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Dedicated type-based object pool for montage action block runtime state.
    /// Maintains independent stacks keyed by concrete Type, achieving lifetime 0-GC instance reuse as different montage assets play in alternation.
    /// </summary>
    public static class MontageBlockStatePool
    {
        #region Private Static Fields

        private static readonly Dictionary<Type, Stack<IMontageBlockState>> _pools = new(16);

        #endregion

        #region Public Static Methods

        /// <summary>
        /// Gets a state instance of the specified type from the type pool. Returns null if the pool is empty or not found (zero delegate allocation).
        /// </summary>
        /// <param name="stateType">Target state class Type</param>
        /// <returns>Reused state instance, or null if the pool is empty</returns>
        public static IMontageBlockState Get(Type stateType)
        {
            if (stateType == null) return null;

            if (_pools.TryGetValue(stateType, out var stack) && stack.Count > 0)
            {
                return stack.Pop();
            }

            return null;
        }

        /// <summary>
        /// Gets a state instance of the specified type from the type pool. Creates one via the supplied factory method if the pool is empty.
        /// </summary>
        /// <param name="stateType">Target state class Type</param>
        /// <param name="factory">Factory delegate for creating a new instance</param>
        /// <returns>The retrieved or newly created state instance</returns>
        public static IMontageBlockState Get(Type stateType, Func<IMontageBlockState> factory)
        {
            if (stateType == null) return null;

            if (_pools.TryGetValue(stateType, out var stack) && stack.Count > 0)
            {
                return stack.Pop();
            }

            return factory?.Invoke();
        }

        /// <summary>
        /// Gets a strongly typed state instance from the generic object pool.
        /// </summary>
        /// <typeparam name="TState">Target state type</typeparam>
        /// <returns>Reused or newly created state instance</returns>
        public static TState Get<TState>() where TState : class, IMontageBlockState, new()
        {
            var type = typeof(TState);
            if (_pools.TryGetValue(type, out var stack) && stack.Count > 0)
            {
                return (TState)stack.Pop();
            }

            return new TState();
        }

        /// <summary>
        /// Resets a used state instance and safely returns it to the object pool for its type.
        /// </summary>
        /// <param name="stateType">Concrete Type of the state instance</param>
        /// <param name="state">State instance reference</param>
        public static void Release(Type stateType, IMontageBlockState state)
        {
            if (stateType == null || state == null) return;

            state.Reset();

            if (!_pools.TryGetValue(stateType, out var stack))
            {
                stack = new Stack<IMontageBlockState>(8);
                _pools[stateType] = stack;
            }

            stack.Push(state);
        }

        /// <summary>
        /// Returns a used strongly typed state instance to the object pool.
        /// </summary>
        /// <typeparam name="TState">State type</typeparam>
        /// <param name="state">State instance reference</param>
        public static void Release<TState>(TState state) where TState : class, IMontageBlockState
        {
            if (state == null) return;
            Release(typeof(TState), state);
        }

        /// <summary>
        /// Clears all state object pools (called on scene unload or when quitting the game).
        /// </summary>
        public static void Clear()
        {
            _pools.Clear();
        }

        #endregion

        #region Private Static Helper Methods

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration()
        {
            Clear();
        }

        #endregion
    }
}
