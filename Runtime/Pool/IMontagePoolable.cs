namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// 蒙太奇对象池生成与回收生命周期通知接口。
    /// 挂载在 Prefab 上的组件可实现此接口，在从对象池取出或归还时接收重置通知。
    /// </summary>
    public interface IMontagePoolable
    {
        /// <summary>
        /// 当实例从对象池中取出并激活时调用。
        /// </summary>
        void OnSpawnFromMontagePool();

        /// <summary>
        /// 当实例即将被归还回对象池并隐藏时调用。
        /// </summary>
        void OnRecycleToMontagePool();
    }
}
