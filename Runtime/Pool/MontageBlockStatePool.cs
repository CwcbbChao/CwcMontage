using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇动作块运行时动态状态专用类型对象池。
    /// 基于真实类型（Type）维护独立栈结构，在不同蒙太奇资产交替播放时实现终身 0-GC 实例复用。
    /// </summary>
    public static class MontageBlockStatePool
    {
        #region 私有静态字段

        private static readonly Dictionary<Type, Stack<IMontageBlockState>> _pools = new(16);

        #endregion

        #region 公共静态方法

        /// <summary>
        /// 从类型对象池获取指定类型的状态实例。若池为空或未找到则返回 null（零委托分配）。
        /// </summary>
        /// <param name="stateType">目标状态类 Type</param>
        /// <returns>复用的状态实例，若池空则返回 null</returns>
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
        /// 从类型对象池获取指定类型的状态实例。若池为空则通过提供的工厂方法生成。
        /// </summary>
        /// <param name="stateType">目标状态类 Type</param>
        /// <param name="factory">新建实例的工厂委托</param>
        /// <returns>获取或新建的状态实例</returns>
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
        /// 从泛型对象池获取强类型状态实例。
        /// </summary>
        /// <typeparam name="TState">目标状态类型</typeparam>
        /// <returns>复用或新建的状态实例</returns>
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
        /// 将使用完毕的状态实例重置并安全归还至对应类型的对象池。
        /// </summary>
        /// <param name="stateType">状态实例的真实 Type</param>
        /// <param name="state">状态实例引用</param>
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
        /// 将使用完毕的强类型状态实例归还至对象池。
        /// </summary>
        /// <typeparam name="TState">状态类型</typeparam>
        /// <param name="state">状态实例引用</param>
        public static void Release<TState>(TState state) where TState : class, IMontageBlockState
        {
            if (state == null) return;
            Release(typeof(TState), state);
        }

        /// <summary>
        /// 清空所有状态对象池（场景卸载或退出游戏时调用）。
        /// </summary>
        public static void Clear()
        {
            _pools.Clear();
        }

        #endregion

        #region 私有静态辅助方法

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration()
        {
            Clear();
        }

        #endregion
    }
}
