namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Lifecycle callbacks for objects spawned from and returned to the montage object pool.
    /// </summary>
    public interface IMontagePoolable
    {
        /// <summary>
        /// Called when the instance is fetched from the pool and activated.
        /// </summary>
        void OnSpawnFromMontagePool();

        /// <summary>
        /// Called right before the instance is returned to the pool and deactivated.
        /// </summary>
        void OnRecycleToMontagePool();
    }
}
