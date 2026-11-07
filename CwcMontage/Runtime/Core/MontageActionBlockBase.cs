using System;
using UnityEngine;

namespace Cwcbb.Tools.CwcMontage
{
    /// <summary>
    /// Abstract base class for all montage action blocks.
    /// Custom audiovisual and gameplay blocks (VFX, audio, hitboxes, shakes) inherit from this class.
    /// </summary>
    [Serializable]
    public abstract class MontageActionBlockBase
    {
        #region Serialized Fields

        [Tooltip("Optional custom name for this action block in the editor.")]
        [SerializeField] private string _customName;

        [Tooltip("Whether this block is enabled. Disabled blocks are skipped during runtime and preview.")]
        [SerializeField] private bool _isEnabled = true;

        #endregion

        #region Private Fields

        [NonSerialized] private bool _isActive;
        [NonSerialized] private float _blockDuration = 1.0f;

        #endregion

        #region Properties

        /// <summary>
        /// Custom display name of the block.
        /// </summary>
        public string CustomName => _customName;

        /// <summary>
        /// Whether this block is enabled.
        /// </summary>
        public bool IsEnabled => _isEnabled;

        /// <summary>
        /// Whether this block is disabled (compatibility property).
        /// </summary>
        public bool IsDisabled => !_isEnabled;

        /// <summary>
        /// Whether this block is currently active and executing.
        /// </summary>
        public bool IsActive => _isActive;

        /// <summary>
        /// Duration of this block in seconds on the montage timeline.
        /// </summary>
        public float BlockDuration
        {
            get => _blockDuration;
            set => _blockDuration = Mathf.Max(0.0001f, value);
        }

        /// <summary>
        /// Whether this block supports trimming and time-stretching (e.g., audio, VFX clips).
        /// </summary>
        public virtual bool IsTrimmableClip => false;

        /// <summary>
        /// Start time of the trimmed media clip (seconds).
        /// </summary>
        public virtual float ClipStartTime
        {
            get => 0f;
            set { }
        }

        /// <summary>
        /// End time of the trimmed media clip (seconds).
        /// </summary>
        public virtual float ClipEndTime
        {
            get => BlockDuration;
            set { }
        }

        /// <summary>
        /// Effective duration of the trimmed clip (seconds), i.e. ClipEndTime - ClipStartTime.
        /// </summary>
        public virtual float EffectiveClipDuration => Mathf.Max(0.001f, ClipEndTime - ClipStartTime);

        /// <summary>
        /// Playback speed multiplier after adaptively stretching the clip to the Block length.
        /// When the Block length equals the effective clip duration, the multiplier is 1.0x (normal speed);
        /// when the Block is shortened to half, playback is 2.0x faster; when stretched to twice the length, playback is 0.5x slower.
        /// </summary>
        public virtual float SpeedMultiplier => EffectiveClipDuration / Mathf.Max(0.001f, BlockDuration);

        #endregion

        #region Public Methods (Duration & Trimming)

        /// <summary>
        /// Adaptively fit the trimmed range to the target duration (e.g. the current Block duration).
        /// </summary>
        /// <param name="targetDuration">Target duration (seconds)</param>
        public virtual void FitClipToDuration(float targetDuration)
        {
            ClipEndTime = ClipStartTime + Mathf.Max(0.001f, targetDuration);
        }

        #endregion

        #region Properties (State Metadata and Factory)

        /// <summary>
        /// Concrete type of the action block's runtime state. Returns null when there is no dynamic state.
        /// </summary>
        public virtual Type StateType => null;

        /// <summary>
        /// Dynamic state object required to instantiate this action block.
        /// </summary>
        public virtual IMontageBlockState CreateState() => null;

        #endregion

        #region Public Methods (Runtime Lifecycle Hooks)

        /// <summary>
        /// Evaluates whether this block meets preconditions to enter execution.
        /// </summary>
        /// <param name="context">Current execution context.</param>
        /// <returns>False to skip execution.</returns>
        public virtual bool CanEnter(in MontageActionContext context)
        {
            return _isEnabled;
        }

        /// <summary>
        /// Called when the timeline enters the block range (backward compatibility overload).
        /// </summary>
        /// <param name="context">Current execution context.</param>
        public virtual void OnEnter(in MontageActionContext context)
        {
            _isActive = true;
        }

        /// <summary>
        /// Called when the timeline enters the block range (zero-allocation state overload).
        /// </summary>
        /// <param name="context">Current execution context.</param>
        /// <param name="state">Allocated runtime state instance.</param>
        public virtual void OnEnter(in MontageActionContext context, IMontageBlockState state)
        {
            OnEnter(context);
        }

        /// <summary>
        /// Called each frame while the timeline is within the block range (backward compatibility overload).
        /// </summary>
        /// <param name="context">Current execution context.</param>
        /// <param name="deltaTime">Delta time since last frame.</param>
        public virtual void OnUpdate(in MontageActionContext context, float deltaTime)
        {
        }

        /// <summary>
        /// Called each frame while the timeline is within the block range (zero-allocation state overload).
        /// </summary>
        /// <param name="context">Current execution context.</param>
        /// <param name="state">Allocated runtime state instance.</param>
        /// <param name="deltaTime">Delta time since last frame.</param>
        public virtual void OnUpdate(in MontageActionContext context, IMontageBlockState state, float deltaTime)
        {
            OnUpdate(context, deltaTime);
        }

        /// <summary>
        /// Called when leaving the block range or when interrupted (backward compatibility overload).
        /// </summary>
        /// <param name="context">Current execution context.</param>
        public virtual void OnExit(in MontageActionContext context)
        {
            _isActive = false;
        }

        /// <summary>
        /// Called when leaving the block range or when interrupted (zero-allocation state overload).
        /// </summary>
        /// <param name="context">Current execution context.</param>
        /// <param name="state">Allocated runtime state instance.</param>
        public virtual void OnExit(in MontageActionContext context, IMontageBlockState state)
        {
            OnExit(context);
        }

        #endregion

        #region Public Methods (Preview Lifecycle Hooks)

        /// <summary>
        /// Evaluates whether this block can enter editor viewport preview.
        /// </summary>
        /// <param name="context">Current preview context.</param>
        public virtual bool CanPreviewEnter(in MontageActionContext context)
        {
            return _isEnabled;
        }

        /// <summary>
        /// Called when editor timeline enters the block range during preview.
        /// </summary>
        /// <param name="context">Current preview context.</param>
        public virtual void OnPreviewEnter(in MontageActionContext context)
        {
        }

        /// <summary>
        /// Called each frame while the editor timeline is within the block range during preview.
        /// </summary>
        /// <param name="context">Current preview context.</param>
        /// <param name="deltaTime">Delta time.</param>
        public virtual void OnPreviewUpdate(in MontageActionContext context, float deltaTime)
        {
        }

        /// <summary>
        /// Called when editor timeline leaves the block range, stops, or resets during preview.
        /// </summary>
        /// <param name="context">Current preview context.</param>
        public virtual void OnPreviewExit(in MontageActionContext context)
        {
        }

        /// <summary>
        /// Called when scrubbing or stepping frames in the editor while paused.
        /// </summary>
        /// <param name="context">Current preview context.</param>
        /// <param name="localTime">Current time in seconds relative to block start.</param>
        public virtual void OnPreviewScrub(in MontageActionContext context, float localTime)
        {
        }

        /// <summary>
        /// Triggered when this action block's parameters are modified in the Inspector, allowing the existing preview instance to update in place (e.g. synchronize the Transform) instead of being destroyed and recreated.
        /// </summary>
        /// <param name="context">Current animation frame execution context.</param>
        public virtual void OnPreviewParametersChanged(in MontageActionContext context)
        {
        }

        /// <summary>
        /// Determines polymorphically whether an external action block parameter change in the editor requires the viewport preview instance to be force-destroyed and recreated (for example, when a core asset reference changed).
        /// Returns false by default (in-place smooth updates are supported); derived classes can override and return true when a core asset reference changes.
        /// </summary>
        /// <param name="newBlock">Source action block data containing the latest parameters</param>
        /// <returns>If true, the main framework calls OnPreviewExit to destroy the old instance and instantiate a new one; otherwise it updates in place</returns>
        public virtual bool RequiresPreviewRecreate(MontageActionBlockBase newBlock)
        {
            return false;
        }

        /// <summary>
        /// Additional description provided by the action block to the Inspector Timing panel (e.g. effect natural duration, adaptive speed multiplier). Returns null by default.
        /// </summary>
        public virtual string GetTimingCustomHint()
        {
            return null;
        }

        #endregion

        #region Public Methods (Clone & Instantiate)

        /// <summary>
        /// Creates an independent deep copy of this action block instance for standalone playback at runtime.
        /// </summary>
        /// <returns>Cloned action block instance</returns>
        public virtual MontageActionBlockBase Clone()
        {
            return (MontageActionBlockBase)MemberwiseClone();
        }

        #endregion
    }

    /// <summary>
    /// Abstract generic base class for montage action blocks with strongly typed state.
    /// The TState generic constraint requires a class, completely avoiding value-type boxing; inputs provide a strongly typed state instance,
    /// guiding developers to keep runtime variables in State, enabling 100% read-only action block assets and zero GC per playback.
    /// </summary>
    /// <typeparam name="TState">Reference-type state container deriving from IMontageBlockState</typeparam>
    [Serializable]
    public abstract class MontageActionBlockBase<TState> : MontageActionBlockBase
        where TState : class, IMontageBlockState, new()
    {
        #region Private Non-Serialized Fields (Preview Only)

        [NonSerialized] private TState _previewState;

        #endregion

        #region Properties (State Metadata and Factory)

        public sealed override Type StateType => typeof(TState);
        public sealed override IMontageBlockState CreateState() => new TState();

        #endregion

        #region Public Dispatch Overrides (Non-Boxing Dispatch)

        public sealed override void OnEnter(in MontageActionContext context, IMontageBlockState state)
        {
            base.OnEnter(context);
            if (state is TState typedState)
            {
                OnEnter(context, typedState);
            }
        }

        public sealed override void OnUpdate(in MontageActionContext context, IMontageBlockState state, float deltaTime)
        {
            base.OnUpdate(context, deltaTime);
            if (state is TState typedState)
            {
                OnUpdate(context, typedState, deltaTime);
            }
        }

        public sealed override void OnExit(in MontageActionContext context, IMontageBlockState state)
        {
            if (state is TState typedState)
            {
                OnExit(context, typedState);
            }
            base.OnExit(context);
        }

        #endregion

        #region Protected Generic Lifecycle Methods

        /// <summary>
        /// Triggered when the timeline first enters the action block's time range (strongly typed runtime only).
        /// </summary>
        /// <param name="context">Current animation frame execution context</param>
        /// <param name="state">Currently allocated strongly typed state instance</param>
        protected abstract void OnEnter(in MontageActionContext context, TState state);

        /// <summary>
        /// Continuously updates each frame while within the action block's active time range (strongly typed runtime only).
        /// </summary>
        /// <param name="context">Current animation frame execution context</param>
        /// <param name="state">Currently allocated strongly typed state instance</param>
        /// <param name="deltaTime">Delta time since last frame</param>
        protected virtual void OnUpdate(in MontageActionContext context, TState state, float deltaTime) { }

        /// <summary>
        /// Triggered when the timeline leaves the action block range or the animation is interrupted externally (strongly typed runtime only).
        /// </summary>
        /// <param name="context">Current animation frame execution context</param>
        /// <param name="state">Currently allocated strongly typed state instance</param>
        protected abstract void OnExit(in MontageActionContext context, TState state);

        #endregion

        #region Editor Preview Lifecycle Adapter

        public override void OnPreviewEnter(in MontageActionContext context)
        {
            base.OnPreviewEnter(context);
            if (_previewState == null)
            {
                _previewState = new TState();
            }
            else
            {
                _previewState.Reset();
            }
        }

        public override void OnPreviewUpdate(in MontageActionContext context, float deltaTime)
        {
            base.OnPreviewUpdate(context, deltaTime);
        }

        public override void OnPreviewExit(in MontageActionContext context)
        {
            if (_previewState != null)
            {
                _previewState.Reset();
            }
            base.OnPreviewExit(context);
        }

        /// <summary>
        /// Get the preview state instance dedicated to the current editor viewport.
        /// </summary>
        protected TState GetEditorPreviewState() => _previewState;

        #endregion
    }
}
