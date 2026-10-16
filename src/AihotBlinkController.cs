using System;

namespace CodexQuotaTray
{
    /// <summary>
    /// AIHOT 新通知「闪烁提醒」状态机(纯逻辑、无 UI 与定时器依赖,由调用方定时器驱动 Tick)。
    /// 规则:
    ///  - 开关关闭:收到新通知也不闪;
    ///  - 全屏/被抑制期间:只记「待提醒」,不显示、不闪(不破坏全屏隐藏逻辑);
    ///  - 解除抑制后:若有待提醒,补闪一次并清除;
    ///  - 闪烁进行中进入抑制:立即停止,不重新排队;
    ///  - 每次提醒 = PulseCount 个亮灭周期,结束后自动停,绝不永久闪烁。
    /// </summary>
    internal sealed class AihotBlinkController
    {
        internal const int PulseCount = 3;
        internal const int TickIntervalMilliseconds = 450;

        private readonly Action<bool> _applyHighlight;
        private bool _enabled;
        private bool _suppressed;
        private bool _pending;
        private bool _highlightOn;
        private bool _phase;
        private int _ticksRemaining;

        public AihotBlinkController(Action<bool> applyHighlight)
        {
            if (applyHighlight == null) throw new ArgumentNullException("applyHighlight");
            _applyHighlight = applyHighlight;
        }

        public bool IsBlinking { get { return _ticksRemaining > 0; } }
        public bool HasPending { get { return _pending; } }

        public void SetEnabled(bool enabled)
        {
            if (_enabled == enabled) return;
            _enabled = enabled;
            if (!enabled)
            {
                _pending = false;
                StopBlink();
            }
        }

        public void SetSuppressed(bool suppressed)
        {
            if (_suppressed == suppressed) return;
            _suppressed = suppressed;
            if (suppressed)
            {
                // 进入抑制(如全屏视频):立即停止当前闪烁,且不重新排队
                StopBlink();
            }
            else if (_pending)
            {
                _pending = false;
                StartBlink();
            }
        }

        /// <summary>收到「新通知」时调用(开关与抑制状态由本类内部判断)。</summary>
        public void NotifyNewEvents()
        {
            if (!_enabled) return;
            if (_suppressed)
            {
                _pending = true;
                return;
            }
            if (IsBlinking) return;
            StartBlink();
        }

        /// <summary>由外部定时器驱动(建议 TickIntervalMilliseconds 间隔)。</summary>
        public void Tick()
        {
            if (_ticksRemaining <= 0) return;
            _ticksRemaining--;
            if (_ticksRemaining <= 0)
            {
                ApplyHighlight(false);
                return;
            }
            _phase = !_phase;
            ApplyHighlight(_phase);
        }

        private void StartBlink()
        {
            _phase = true;
            _ticksRemaining = PulseCount * 2;
            ApplyHighlight(true);
        }

        private void StopBlink()
        {
            _ticksRemaining = 0;
            _phase = false;
            ApplyHighlight(false);
        }

        private void ApplyHighlight(bool on)
        {
            if (_highlightOn == on) return;
            _highlightOn = on;
            _applyHighlight(on);
        }
    }
}
