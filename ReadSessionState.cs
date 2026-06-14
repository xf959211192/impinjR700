namespace ImpinjR700
{
    /// <summary>
    /// 维护读取会话状态，明确区分“暂停恢复”和“新一轮读取”。
    /// </summary>
    public sealed class ReadSessionState
    {
        public bool IsReading { get; private set; }

        public bool IsPaused { get; private set; }

        public bool ShouldResetRecordsOnStart => !IsPaused;

        public bool Start()
        {
            var shouldResetRecords = ShouldResetRecordsOnStart;
            IsReading = true;
            IsPaused = false;
            return shouldResetRecords;
        }

        public bool Pause()
        {
            if (!IsReading)
            {
                return false;
            }

            IsReading = false;
            IsPaused = true;
            return true;
        }

        public bool Stop()
        {
            var wasActive = IsReading || IsPaused;
            IsReading = false;
            IsPaused = false;
            return wasActive;
        }

        public void Reset()
        {
            IsReading = false;
            IsPaused = false;
        }
    }
}
