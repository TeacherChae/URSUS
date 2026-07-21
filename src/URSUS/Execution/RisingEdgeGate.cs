namespace URSUS.Execution
{
    /// <summary>
    /// false를 관찰한 뒤 true로 전환될 때만 한 번 실행 신호를 낸다.
    /// 문서를 true 상태로 다시 열었을 때 자동 실행하지 않는다.
    /// </summary>
    public sealed class RisingEdgeGate
    {
        private bool _armed;
        private bool _previous;

        public bool Observe(bool value)
        {
            if (!value)
            {
                _armed = true;
                _previous = false;
                return false;
            }

            bool triggered = _armed && !_previous;
            _previous = true;
            if (triggered)
                _armed = false;
            return triggered;
        }
    }
}
